namespace OVS.Client.Audio.Codec;

public static class AudioFormat
{
    public const int SampleRate = 48_000;
    public const int FrameSamples = 960; // 20 ms mono
    /// <summary>Package 142 (A151): 20 ms of interleaved stereo, as the decoders, the mixer and the speaker work.</summary>
    public const int StereoFrameSamples = 2 * FrameSamples;
    /// <summary>Half a frame, 10 ms: RNNoise and the echo canceller work on 10 ms at 48 kHz, so one 20 ms frame is two halves.</summary>
    public const int HalfFrameSamples = 480;
    public static readonly TimeSpan FrameDuration = TimeSpan.FromMilliseconds(20);
}
