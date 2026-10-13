namespace OVS.Client.Audio.Transmit;

/// <summary>
/// Package 129 (A138): Fluxer's voice gate. It always learns the noise floor (10th percentile of the last 2 s of frame
/// levels, times 1.5); in automatic mode the threshold is 3 × that floor, after 0.3 s of learning.
/// </summary>
public sealed class VoiceActivityDetector
{
    public const int HangoverFrames = 9; // Package 129: keep sending 180 ms after the level drops below 0.7 × the threshold
    public const int FloorFrames = 100; // 2 s of frame levels
    public const int LearningFrames = 15; // 0.3 s: before that the manual threshold applies
    static readonly float FloorAboveP10Db = 20 * MathF.Log10(3 * 1.5f); // 3 × (1.5 × the 10th percentile), about 13 dB
    static readonly float HysteresisDb = 20 * MathF.Log10(0.7f); // about -3 dB
    // Fluxer's -110 lower bound becomes -60: RNNoise leaves near-silence between words, which would set the threshold
    // near -107 dBFS and let breaths and clicks through; speech raised by the automatic gain sits near -20 dBFS.
    const float MinAutoDb = -60f, MaxAutoDb = -28f;

    readonly float[] levels = new float[FloorFrames]; // ring of frame levels in dB
    readonly float[] sorted = new float[FloorFrames];
    int count, next, hangover;

    public float ThresholdDb { get; set; } = -40f;
    public bool Automatic { get; set; }
    public float ThresholdInUseDb { get; private set; } = -40f;
    public float LastLevelDb { get; private set; } = -120f;

    /// <param name="channels">Package 142: 2 for an interleaved stereo frame, whose louder side counts.</param>
    public bool Process(ReadOnlySpan<float> frame, int channels = 1)
    {
        LastLevelDb = LevelDb(frame, channels);
        ThresholdInUseDb = Automatic && count >= LearningFrames ? AutoThresholdDb() : ThresholdDb;
        levels[next] = LastLevelDb;
        next = (next + 1) % FloorFrames;
        if (count < FloorFrames) count++;

        // Hysteresis: between 0.7 × and the threshold an open gate stays open without counting down.
        if (LastLevelDb >= ThresholdInUseDb || (hangover > 0 && LastLevelDb >= ThresholdInUseDb + HysteresisDb))
        {
            hangover = HangoverFrames;
            return true;
        }
        if (hangover > 0)
        {
            hangover--;
            return true;
        }
        return false;
    }

    float AutoThresholdDb()
    {
        var window = sorted.AsSpan(0, count);
        levels.AsSpan(0, count).CopyTo(window);
        window.Sort(); // ponytail: a full sort of at most 100 floats per 20 ms; a histogram like Fluxer's if it ever shows up
        return Math.Clamp(window[count / 10] + FloorAboveP10Db, MinAutoDb, MaxAutoDb);
    }

    /// <param name="channels">Package 142 (A151): 2 for interleaved stereo: the level of the louder side.</param>
    public static float LevelDb(ReadOnlySpan<float> frame, int channels = 1)
    {
        if (frame.IsEmpty) return -120f;
        double loudest = 0;
        for (int c = 0; c < channels; c++)
        {
            double sum = 0;
            for (int index = c; index < frame.Length; index += channels) sum += frame[index] * frame[index];
            loudest = Math.Max(loudest, sum);
        }
        double rms = Math.Sqrt(loudest / (frame.Length / channels));
        return rms <= 1e-6 ? -120f : (float)(20 * Math.Log10(rms));
    }
}
