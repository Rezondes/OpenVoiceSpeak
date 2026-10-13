namespace OVS.Client.Audio.Processing;

/// <summary>
/// Package 128 (A137): Fluxer's volume curve. A volume is the slider value (1 = 100 %, 0 to 2); up to 100 % it falls
/// logarithmically (50 % is about a quarter), above it rises exponentially to four times at 200 %.
/// </summary>
public static class VolumeCurve
{
    /// <summary>Per-user and output gain together never exceed this (the voice boost and the limiter come after).</summary>
    public const float MaxGain = 12f;

    /// <summary>The gain of a slider value: 1 is unchanged, 2 is four times (+12 dB).</summary>
    public static float Gain(float volume) =>
        volume <= 0f ? 0f : volume <= 1f ? MathF.Pow(10f, (volume - 1f) * 1.2f) : MathF.Pow(4f, volume - 1f);

    /// <summary>The gain of a per-speaker and an output volume together, at most MaxGain.</summary>
    public static float Combine(float user, float output) => Math.Min(Gain(user) * Gain(output), MaxGain);
}
