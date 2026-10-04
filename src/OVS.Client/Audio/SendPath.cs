using OVS.Shared.Voice;

namespace OVS.Client.Audio;

public enum TransmitMode { PushToTalk, VoiceActivation }

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
    int filled;

    /// <summary>Package 124: the one frame buffer, reused; valid until the next <see cref="Take"/>.</summary>
    public float[] Frame { get; } = new float[AudioFormat.FrameSamples];

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

/// <summary>Linear interpolation resampler, good enough for speech. Keeps state across chunks.</summary>
public sealed class LinearResampler(int fromRate, int toRate)
{
    readonly double step = (double)fromRate / toRate;
    double position; // in the virtual stream [last, input...], index 0 = last sample of the previous chunk
    float last;
    float[] output = [];

    /// <returns>The resampled chunk in a buffer reused by the next call (Package 124); the input itself at equal rates.</returns>
    public ReadOnlySpan<float> Process(ReadOnlySpan<float> input)
    {
        if (fromRate == toRate || input.IsEmpty) return input;
        int most = (int)((input.Length - position) / step) + 2;
        if (output.Length < most) output = new float[most];
        int count = 0;
        while (position < input.Length)
        {
            int i = (int)position;
            float frac = (float)(position - i);
            float a = i == 0 ? last : input[i - 1];
            float b = input[i];
            output[count++] = a + (b - a) * frac;
            position += step;
        }
        position -= input.Length;
        last = input[^1];
        return output.AsSpan(0, count);
    }
}
