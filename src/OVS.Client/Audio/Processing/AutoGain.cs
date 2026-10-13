using OVS.Client.Audio.Transmit;

namespace OVS.Client.Audio.Processing;

/// <summary>
/// Package 123 (A131): a slow automatic gain for quiet microphones. It only raises (a loud voice stays as it is),
/// moves a little per frame so it is not heard pumping, and holds still while nobody speaks, so the noise floor is
/// not pulled up into the pauses. Package 127 (A136): RNNoise's voice probability decides what is speech when it runs,
/// so a very quiet voice is raised too, up to +30 dB.
/// </summary>
public sealed class AutoGain
{
    /// <summary>The level speech is brought to, in dB.</summary>
    public const float TargetDb = -20f;
    /// <summary>The most gain added, in dB.</summary>
    public const float MaxGainDb = 30f;
    /// <summary>-3 dBFS: the raised voice never peaks above this.</summary>
    public const float PeakLimit = 0.7079f;
    /// <summary>
    /// Without a voice probability (noise suppression off or missing), quieter frames count as a pause and leave the
    /// gain where it is. -45 dBFS: room noise between -50 and -40 dBFS would otherwise count as a quiet voice and be pulled up.
    /// </summary>
    public const float VoiceFloorDb = -45f;

    /// <summary>Package 127: a frame RNNoise rates at least this likely to be speech counts as speech, however quiet.</summary>
    public const float SpeechProbability = 0.5f;

    // 0.2 dB per 20 ms frame (Package 127, was 0.15): the full +30 dB within 3 s of speech, so within 5 s with the
    // pauses between syllables; down faster, so a louder voice is not too loud for long
    const float RiseDbPerFrame = 0.2f;
    const float FallDbPerFrame = 1f;

    float applied = 1f; // linear gain at the end of the last frame, where the next frame's ramp starts

    /// <summary>The gain applied now, in dB.</summary>
    public float GainDb { get; private set; }

    /// <summary>Back to 0 dB, so switching it on again never resumes with a gain from back then.</summary>
    public void Reset()
    {
        GainDb = 0;
        applied = 1f;
    }

    /// <param name="voiceProbability">RNNoise's rating of the frame, or null without noise suppression.</param>
    /// <param name="frame">The frame, changed in place.</param>
    public void Process(Span<float> frame, float? voiceProbability = null)
    {
        float level = VoiceActivityDetector.LevelDb(frame);
        if (voiceProbability is { } probability ? probability >= SpeechProbability : level >= VoiceFloorDb)
        {
            float wanted = Math.Clamp(TargetDb - level, 0f, MaxGainDb);
            GainDb = Math.Clamp(wanted, GainDb - FallDbPerFrame, GainDb + RiseDbPerFrame);
        }

        float peak = 0;
        foreach (var sample in frame) peak = Math.Max(peak, Math.Abs(sample));
        // never above the peak limit, but never below 1 either: a signal already that loud is left alone
        float limit = peak > 0 ? Math.Max(1f, PeakLimit / peak) : float.MaxValue;
        float end = Math.Min(MathF.Pow(10, GainDb / 20), limit);
        float start = Math.Min(applied, limit);
        applied = end;
        if (start == 1f && end == 1f) return;

        // a ramp across the frame instead of a step at its start, which would click
        float step = (end - start) / frame.Length;
        for (int index = 0; index < frame.Length; index++) frame[index] *= start + step * index;
    }
}
