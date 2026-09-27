using OVS.Shared.Voice;

namespace OVS.Client.Audio;

public enum TransmitMode { PushToTalk, VoiceActivation }

public static class TransmitController
{
    /// <returns>Voice target to send with, or null to stay silent. Voice activation never uses links.</returns>
    public static byte? Decide(TransmitMode mode, bool pttDown, bool linkPttDown, bool vadActive, bool selfMuted, bool hasSpeakLinked)
    {
        if (selfMuted) return null;
        if (linkPttDown) return hasSpeakLinked ? VoiceHeader.TargetLinked : VoiceHeader.TargetChannel;
        if (pttDown) return VoiceHeader.TargetChannel;
        if (mode == TransmitMode.VoiceActivation && vadActive) return VoiceHeader.TargetChannel;
        return null;
    }
}

public sealed class VoiceActivityDetector
{
    public const int HangoverFrames = 15; // keep sending 300 ms after the level drops

    int hangover;

    public float ThresholdDb { get; set; } = -40f;
    public float LastLevelDb { get; private set; } = -120f;

    public bool Process(ReadOnlySpan<float> frame)
    {
        LastLevelDb = LevelDb(frame);
        if (LastLevelDb >= ThresholdDb)
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

    public static float LevelDb(ReadOnlySpan<float> frame)
    {
        if (frame.IsEmpty) return -120f;
        double sum = 0;
        foreach (var s in frame) sum += s * s;
        double rms = Math.Sqrt(sum / frame.Length);
        return rms <= 1e-6 ? -120f : (float)(20 * Math.Log10(rms));
    }
}

/// <summary>Turns arbitrary sized capture buffers into exact 960 sample frames.</summary>
public sealed class FrameChunker
{
    readonly float[] buffer = new float[AudioFormat.FrameSamples];
    int filled;

    public List<float[]> Add(ReadOnlySpan<float> samples)
    {
        var frames = new List<float[]>();
        while (!samples.IsEmpty)
        {
            int take = Math.Min(samples.Length, buffer.Length - filled);
            samples[..take].CopyTo(buffer.AsSpan(filled));
            filled += take;
            samples = samples[take..];
            if (filled == buffer.Length)
            {
                frames.Add((float[])buffer.Clone());
                filled = 0;
            }
        }
        return frames;
    }
}

/// <summary>Linear interpolation resampler, good enough for speech. Keeps state across chunks.</summary>
public sealed class LinearResampler(int fromRate, int toRate)
{
    readonly double step = (double)fromRate / toRate;
    double position; // in the virtual stream [last, input...], index 0 = last sample of the previous chunk
    float last;

    public float[] Process(ReadOnlySpan<float> input)
    {
        if (fromRate == toRate || input.IsEmpty) return input.ToArray();
        var output = new List<float>((int)(input.Length / step) + 2);
        while (position < input.Length)
        {
            int i = (int)position;
            float frac = (float)(position - i);
            float a = i == 0 ? last : input[i - 1];
            float b = input[i];
            output.Add(a + (b - a) * frac);
            position += step;
        }
        position -= input.Length;
        last = input[^1];
        return output.ToArray();
    }
}
