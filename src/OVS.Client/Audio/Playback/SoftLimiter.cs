namespace OVS.Client.Audio.Playback;

/// <summary>
/// Package 50 (A52): keeps the boosted mix below <see cref="Threshold"/> without cutting the waveform flat. A loud frame
/// is scaled down as a whole at once; afterwards the gain climbs back by <see cref="ReleasePerFrame"/> per frame,
/// ramped sample by sample so it never clicks. Below the threshold the signal is left exactly as it is.
/// </summary>
internal sealed class SoftLimiter
{
    /// <summary>The level above which the gain goes down.</summary>
    public const float Threshold = 0.95f;
    /// <summary>How fast the gain comes back per frame: From half gain back to full in 200 ms.</summary>
    public const float ReleasePerFrame = 0.05f;

    /// <summary>The gain applied now.</summary>
    public float Gain { get; private set; } = 1f;

    /// <summary>Lowers the frame where it would go above the threshold.</summary>
    public void Process(Span<float> frame)
    {
        float peak = 0;
        foreach (float sample in frame) peak = Math.Max(peak, Math.Abs(sample));
        float allowed = peak > Threshold ? Threshold / peak : 1f;
        if (allowed <= Gain)
        {
            Gain = allowed; // attack at once, the frame's peak lands exactly on the threshold
            if (Gain < 1f) for (int index = 0; index < frame.Length; index++) frame[index] *= Gain;
            return;
        }
        float start = Gain, end = Math.Min(allowed, Gain + ReleasePerFrame);
        Gain = end;
        if (start >= 1f) return;
        for (int index = 0; index < frame.Length; index++) frame[index] *= start + (end - start) * (index + 1) / frame.Length;
    }
}
