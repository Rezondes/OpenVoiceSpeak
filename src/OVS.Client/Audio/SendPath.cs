using NAudio.Dsp;
using OVS.Shared.Voice;

namespace OVS.Client.Audio;

public enum TransmitMode { PushToTalk, VoiceActivation }

/// <summary>Package 142 (A151): "Übertragungsart": speech (mono, voice filters, Opus VOIP) or music (stereo, no filters).</summary>
public enum Transmission { Voice, Music }

public static class TransmitController
{
    /// <returns>Voice target to send with, or null to stay silent. Voice activation never uses links.</returns>
    /// <remarks>
    /// The PTT key only counts in push-to-talk mode: with voice activation the voice alone opens the microphone.
    /// Link-PTT works in both modes, it is the only way to reach linked channels.
    /// </remarks>
    public static byte? Decide(TransmitMode mode, bool pttDown, bool linkPttDown, bool vadActive, bool selfMuted, bool hasSpeakLinked)
    {
        if (selfMuted) return null;
        if (linkPttDown) return hasSpeakLinked ? VoiceHeader.TargetLinked : VoiceHeader.TargetChannel;
        return mode switch
        {
            TransmitMode.PushToTalk when pttDown => VoiceHeader.TargetChannel,
            TransmitMode.VoiceActivation when vadActive => VoiceHeader.TargetChannel,
            _ => null,
        };
    }
}

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
            for (int i = c; i < frame.Length; i += channels) sum += frame[i] * frame[i];
            loudest = Math.Max(loudest, sum);
        }
        double rms = Math.Sqrt(loudest / (frame.Length / channels));
        return rms <= 1e-6 ? -120f : (float)(20 * Math.Log10(rms));
    }
}

/// <summary>Turns arbitrary sized capture buffers into exact 960 sample frames (Package 142: 2 x 960 interleaved for stereo).</summary>
public sealed class FrameChunker(int channels = 1)
{
    int filled;

    /// <summary>Package 124: the one frame buffer, reused; valid until the next <see cref="Take"/>.</summary>
    public float[] Frame { get; } = new float[AudioFormat.FrameSamples * channels];

    /// <summary>Package 142: drops a part frame, when the capture switches between mono and stereo.</summary>
    public void Reset() => filled = 0;

    /// <summary>Takes samples until <see cref="Frame"/> is full; <paramref name="samples"/> keeps the rest.</summary>
    /// <returns>True when a full frame is ready.</returns>
    public bool Take(ref ReadOnlySpan<float> samples)
    {
        if (filled == Frame.Length) filled = 0; // the last full frame has been used
        int take = Math.Min(samples.Length, Frame.Length - filled);
        samples[..take].CopyTo(Frame.AsSpan(filled));
        filled += take;
        samples = samples[take..];
        return filled == Frame.Length;
    }
}

/// <summary>
/// Package 138 (A147): mono resampler around NAudio's windowed-sinc <see cref="WdlResampler"/>, which low-passes
/// before converting, so tones above the voice band do not fold back into it as linear interpolation let them.
/// Keeps state across chunks. Package 142: interleaved stereo too, which WDL resamples per channel.
/// </summary>
/// <remarks>
/// WDL resizes its input buffer to whatever a call needs (<see cref="Array.Resize{T}"/>), so chunks of changing size
/// would allocate on every call. Asked for a fixed number of output samples it always needs the same buffer, so this
/// keeps the input in a queue and takes <see cref="Block"/> output samples at a time. Latency: about 36 input samples
/// (half the sinc plus WDL's 4 of look-ahead) plus up to 1 ms in the queue; under 2 ms from 44.1 kHz up, about 5 ms at 8 kHz.
/// </remarks>
public sealed class SincResampler
{
    // WDL interpolates linearly in its coefficient table; at its default of 32 steps that error alone left a folded
    // 20 kHz tone 45 dB louder (SendPathTests.Resampler_NoAliasing). The table costs 65 KB once.
    const int Taps = 64, Oversampling = 256;
    const int Block = 48; // output samples per step, 1 ms at 48 kHz

    readonly WdlResampler? wdl; // null at equal rates
    readonly double ratio;      // output samples per input sample
    readonly int channels;
    float[] queue = [], output = [];
    int queued; // floats, so channels per sample frame

    public SincResampler(int fromRate, int toRate, int channels = 1)
    {
        this.channels = channels;
        if (fromRate == toRate) return;
        ratio = (double)toRate / fromRate;
        wdl = new WdlResampler();
        wdl.SetMode(true, 0, true, Taps, Oversampling); // windowed sinc; it low-passes at the lower of the two rates
        wdl.SetRates(fromRate, toRate);
    }

    /// <returns>The resampled chunk in a buffer reused by the next call (Package 124); the input itself at equal rates.</returns>
    public ReadOnlySpan<float> Process(ReadOnlySpan<float> input)
    {
        if (wdl is null || input.IsEmpty) return input;
        if (queue.Length < queued + input.Length) Array.Resize(ref queue, queued + input.Length);
        input.CopyTo(queue.AsSpan(queued));
        queued += input.Length;
        int most = ((int)(queued / channels * ratio) + Block) * channels;
        if (output.Length < most) output = new float[most];

        int taken = 0, count = 0;
        while (true)
        {
            // safe without a ResampleOut after it; the same block size keeps WDL's buffer the same size
            int wanted = wdl.ResamplePrepare(Block, channels, out var buffer, out int offset) * channels; // WDL counts sample frames
            if (queued - taken < wanted) break;
            queue.AsSpan(taken, wanted).CopyTo(buffer.AsSpan(offset));
            count += wdl.ResampleOut(output, count, wanted / channels, Block, channels) * channels;
            taken += wanted;
        }
        queue.AsSpan(taken, queued - taken).CopyTo(queue);
        queued -= taken;
        return output.AsSpan(0, count);
    }
}
