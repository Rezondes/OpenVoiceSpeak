namespace OVS.Client.Audio;

/// <summary>
/// Package 123 (A131): a slow automatic gain for quiet microphones. It only raises (a loud voice stays as it is),
/// moves a little per frame so it is not heard pumping, and holds still while nobody speaks, so the noise floor is
/// not pulled up into the pauses.
/// </summary>
public sealed class AutoGain
{
    public const float TargetDb = -20f;
    public const float MaxGainDb = 18f;
    /// <summary>-3 dBFS: the raised voice never peaks above this.</summary>
    public const float PeakLimit = 0.7079f;
    /// <summary>
    /// Quieter frames count as a pause and leave the gain where it is. -45 dBFS: room noise between -50 and -40 dBFS
    /// (with noise suppression off) would otherwise count as a quiet voice and be pulled up.
    /// </summary>
    public const float VoiceFloorDb = -45f;

    // 0.15 dB per 20 ms frame: the full +18 dB within 2.4 s; down faster, so a louder voice is not too loud for long
    const float RiseDbPerFrame = 0.15f;
    const float FallDbPerFrame = 1f;

    float applied = 1f; // linear gain at the end of the last frame, where the next frame's ramp starts

    public float GainDb { get; private set; }

    /// <summary>Back to 0 dB, so switching it on again never resumes with a gain from back then.</summary>
    public void Reset()
    {
        GainDb = 0;
        applied = 1f;
    }

    public void Process(Span<float> frame)
    {
        float level = VoiceActivityDetector.LevelDb(frame);
        if (level >= VoiceFloorDb)
        {
            float wanted = Math.Clamp(TargetDb - level, 0f, MaxGainDb);
            GainDb = Math.Clamp(wanted, GainDb - FallDbPerFrame, GainDb + RiseDbPerFrame);
        }

        float peak = 0;
        foreach (var s in frame) peak = Math.Max(peak, Math.Abs(s));
        // never above the peak limit, but never below 1 either: a signal already that loud is left alone
        float limit = peak > 0 ? Math.Max(1f, PeakLimit / peak) : float.MaxValue;
        float end = Math.Min(MathF.Pow(10, GainDb / 20), limit);
        float start = Math.Min(applied, limit);
        applied = end;
        if (start == 1f && end == 1f) return;

        // a ramp across the frame instead of a step at its start, which would click
        float step = (end - start) / frame.Length;
        for (int i = 0; i < frame.Length; i++) frame[i] *= start + step * i;
    }
}
