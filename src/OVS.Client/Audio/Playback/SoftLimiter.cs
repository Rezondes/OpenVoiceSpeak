using OVS.Client.Audio.Rust;

namespace OVS.Client.Audio.Playback;

/// <summary>
/// Package 50 (A52): keeps the boosted mix below <see cref="Threshold"/> without cutting the waveform flat. A loud frame
/// is scaled down as a whole at once; afterwards the gain climbs back by <see cref="ReleasePerFrame"/> per frame,
/// ramped sample by sample so it never clicks. Below the threshold the signal is left exactly as it is. Package 176: the
/// mixer limits natively (native/ovs_audio, <c>core/limiter.rs</c>); this is that limiter's managed face for the tests.
/// </summary>
internal sealed unsafe class SoftLimiter
{
    /// <summary>The level above which the gain goes down.</summary>
    public const float Threshold = 0.95f;
    /// <summary>How fast the gain comes back per frame: From half gain back to full in 200 ms.</summary>
    public const float ReleasePerFrame = 0.05f;

    readonly NativeAudio native = NativeAudio.Load();
    readonly NativeObjectHandle limiter;

    /// <summary>A limiter at full gain; throws when the native library is unusable (A180).</summary>
    public SoftLimiter() => limiter = NativeObjectHandle.Create(native, native.LimiterCreate, native.LimiterFree);

    /// <summary>The gain applied now.</summary>
    public float Gain { get; private set; } = 1f;

    /// <summary>Lowers the frame where it would go above the threshold.</summary>
    public void Process(Span<float> frame)
    {
        fixed (float* samples = frame) Gain = native.LimiterProcess(limiter.Pointer, samples, (uint)frame.Length);
    }
}
