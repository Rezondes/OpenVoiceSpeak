using Concentus;
using Concentus.Enums;
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
    readonly IOpusEncoder encoder = OpusCodecFactory.CreateEncoder(AudioFormat.SampleRate, 1, OpusApplication.OPUS_APPLICATION_VOIP);
    readonly byte[] buffer = new byte[VoiceHeader.MaxOpusSize];

    public VoiceEncoder() => encoder.Bitrate = 32_000;

    public byte[] Encode(ReadOnlySpan<float> frame)
    {
        int length = encoder.Encode(frame, AudioFormat.FrameSamples, buffer, buffer.Length);
        return buffer[..length];
    }
}

public sealed class VoiceDecoder
{
    readonly IOpusDecoder decoder = OpusCodecFactory.CreateDecoder(AudioFormat.SampleRate, 1);

    /// <summary>An empty packet asks Opus to conceal a lost frame (PLC).</summary>
    public float[] Decode(ReadOnlySpan<byte> packet)
    {
        var pcm = new float[AudioFormat.FrameSamples];
        decoder.Decode(packet, pcm, AudioFormat.FrameSamples);
        return pcm;
    }
}
