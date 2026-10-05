namespace OVS.Client.Audio.Codec;

public static class AudioFormat
{
    public const int SampleRate = 48_000;
    public const int FrameSamples = 960; // 20 ms mono
    /// <summary>Package 142 (A151): 20 ms of interleaved stereo, as the decoders, the mixer and the speaker work.</summary>
    public const int StereoFrameSamples = 2 * FrameSamples;
    public static readonly TimeSpan FrameDuration = TimeSpan.FromMilliseconds(20);
}
