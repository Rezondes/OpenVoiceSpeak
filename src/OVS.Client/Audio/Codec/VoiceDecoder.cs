using OVS.Client.Audio.Rust;

namespace OVS.Client.Audio.Codec;

/// <summary>
/// Package 142 (A151): always decodes to stereo. An Opus packet carries its channel count, so a mono packet (every voice
/// mode sender) comes out the same on both sides, and music in stereo needs no change in the protocol. Package 175: libopus
/// in the native library (A183); a 1-byte or broken packet is concealed there.
/// </summary>
internal sealed unsafe class VoiceDecoder
{
    readonly NativeAudio native = NativeAudio.Load();
    readonly NativeObjectHandle decoder;
    readonly float[] pcm = new float[AudioFormat.StereoFrameSamples];

    /// <summary>A new decoder; throws when the native library is unusable (A180).</summary>
    public VoiceDecoder() => decoder = NativeObjectHandle.Create(native, native.DecoderCreate, native.DecoderFree);

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
        fixed (byte* data = packet)
        fixed (float* frame = pcm)
            native.DecoderDecode(decoder.Pointer, data, (uint)packet.Length, fec ? (byte)1 : (byte)0, frame);
        return pcm;
    }
}
