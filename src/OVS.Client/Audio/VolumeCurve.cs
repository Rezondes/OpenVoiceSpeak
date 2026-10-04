namespace OVS.Client.Audio;

/// <summary>
/// Package 128 (A137): Fluxer's volume curve. A volume is the slider value (1 = 100 %, 0 to 2); up to 100 % it falls
/// logarithmically (50 % is about a quarter), above it rises exponentially to four times at 200 %.
/// </summary>
public static class VolumeCurve
{
    /// <summary>Per-user and output gain together never exceed this (the voice boost and the limiter come after).</summary>
    public const float MaxGain = 12f;

    public static float Gain(float volume) =>
        volume <= 0f ? 0f : volume <= 1f ? MathF.Pow(10f, (volume - 1f) * 1.2f) : MathF.Pow(4f, volume - 1f);

    public static float Combine(float user, float output) => Math.Min(Gain(user) * Gain(output), MaxGain);
}
