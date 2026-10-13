using Concentus;

namespace OVS.Client.Audio.Codec;

/// <summary>
/// Package 142 (A151): always decodes to stereo. An Opus packet carries its channel count, so a mono packet (every voice
/// mode sender) comes out the same on both sides, and music in stereo needs no change in the protocol.
/// </summary>
internal sealed class VoiceDecoder
{
    readonly IOpusDecoder decoder = OpusCodecFactory.CreateDecoder(AudioFormat.SampleRate, 2);
    readonly float[] pcm = new float[AudioFormat.StereoFrameSamples];

    /// <summary>An empty packet asks Opus to conceal a lost frame (PLC).</summary>
    /// <returns>20 ms of interleaved stereo PCM in a buffer reused by the next call (Package 124: nothing allocated per frame).</returns>
    public float[] Decode(ReadOnlySpan<byte> packet) => Decode(packet, false);

    /// <summary>
    /// Package 142: only the speech coder's packets (SILK or hybrid, configurations 0 to 15 in the table of contents, RFC
    /// 6716 3.1) can carry FEC; a music coder packet (CELT only, as music in stereo) never does, so a frame lost before
    /// it is concealed. Whether a SILK packet actually carries it would need its range coder, so those count as rebuilt.
    /// </summary>
    public static bool MayCarryFec(ReadOnlySpan<byte> packet) => packet.Length > 1 && packet[0] >> 3 < 16;

    /// <summary>Rebuilds the lost frame before <paramref name="nextPacket"/> from the FEC copy it carries.</summary>
    public float[] DecodeLost(ReadOnlySpan<byte> nextPacket) => Decode(nextPacket, true);

    float[] Decode(ReadOnlySpan<byte> packet, bool fec)
    {
        // Package 124: a 1-byte frame (DTX in other Opus senders) means "nothing to play" like a lost one, but Concentus
        // throws on it; that and a broken packet from anyone must not end the playback thread: both are concealed.
        if (packet.Length <= 1) packet = default;
        try
        {
            decoder.Decode(packet, pcm, AudioFormat.FrameSamples, fec && !packet.IsEmpty);
        }
        catch (OpusException)
        {
            decoder.Decode(default, pcm, AudioFormat.FrameSamples, false);
        }
        return pcm;
    }
}
