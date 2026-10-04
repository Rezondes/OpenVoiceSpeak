using Concentus;
using Concentus.Enums;
using OVS.Shared.Protocol;
using OVS.Shared.Voice;

namespace OVS.Client.Audio;

public static class AudioFormat
{
    public const int SampleRate = 48_000;
    public const int FrameSamples = 960; // 20 ms mono
    public static readonly TimeSpan FrameDuration = TimeSpan.FromMilliseconds(20);
}

public sealed class VoiceEncoder
{
    /// <summary>
    /// Package 124: Opus defaults to its most expensive setting. Measured on a speech-like signal at 32 kbps (PLAN.md,
    /// Package 124): 5 encodes in about 60 % of the time of 10 with the same spectral distance. It is the lowest value
    /// that keeps the voice part's spectral interpolation and warped noise shaping (from 4) and the music part's pitch
    /// pre-filter (from 5); what dropping them costs a real voice a synthetic signal cannot show.
    /// </summary>
    public const int Complexity = 5;

    readonly IOpusEncoder encoder = OpusCodecFactory.CreateEncoder(AudioFormat.SampleRate, 1, OpusApplication.OPUS_APPLICATION_VOIP);
    readonly byte[] buffer = new byte[VoiceHeader.MaxOpusSize];

    public VoiceEncoder()
    {
        encoder.Bitrate = ProtocolInfo.DefaultBitrate * 1000;
        encoder.SignalType = OpusSignal.OPUS_SIGNAL_VOICE;
        encoder.Complexity = Complexity;
        // Package 124: DTX stays off. Push-to-talk and voice activation already stop sending in pauses; within one, DTX
        // would send a comfort-noise frame about every 400 ms, which a receiver plays as a lone frame plus concealment,
        // a faint pulse. It also takes a steady tone for noise after a second and stops sending it.
        encoder.UseDTX = false;
        // In-band FEC: each packet also carries a coarse copy of the previous frame, so the receiver can rebuild a
        // single lost packet instead of guessing it. Opus only adds it when told to expect some loss.
        encoder.UseInbandFEC = true;
        encoder.PacketLossPercent = 10;
    }

    internal IOpusEncoder Opus => encoder;

    /// <summary>Package 132 (A141): the channel's bitrate in kbps; set on the thread that encodes.</summary>
    public int BitrateKbps
    {
        get => encoder.Bitrate / 1000;
        set => encoder.Bitrate = value * 1000;
    }

    /// <returns>A new array: it is sent or kept by the mixer (and only made while sending, never per idle frame).</returns>
    public byte[] Encode(ReadOnlySpan<float> frame)
    {
        int length = encoder.Encode(frame, AudioFormat.FrameSamples, buffer, buffer.Length);
        return buffer[..length];
    }
}

public sealed class VoiceDecoder
{
    readonly IOpusDecoder decoder = OpusCodecFactory.CreateDecoder(AudioFormat.SampleRate, 1);
    readonly float[] pcm = new float[AudioFormat.FrameSamples];

    /// <summary>An empty packet asks Opus to conceal a lost frame (PLC).</summary>
    /// <returns>20 ms of PCM in a buffer reused by the next call (Package 124: nothing allocated per frame).</returns>
    public float[] Decode(ReadOnlySpan<byte> packet) => Decode(packet, false);

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
