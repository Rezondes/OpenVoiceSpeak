using OVS.Client.Audio.Codec;

namespace OVS.Client.Audio.Capture;

/// <summary>
/// The microphone's interleaved float samples, mono (one-sided rule or average) or stereo, and the stereo average to
/// mono (Downmix) that the capture and the echo canceller's far end share. Static and stateless; nothing is allocated
/// while the caller's buffer is large enough.
/// </summary>
internal static class SampleConversion
{
    /// <summary>
    /// The microphone's samples as mono, written into <paramref name="mono"/>, which grows when needed and is kept by
    /// the caller (Package 124: nothing allocated per callback). The result is valid until the next call with it.
    /// </summary>
    public static Span<float> ReadMono(ReadOnlySpan<float> samples, int channels, ref float[] mono)
    {
        var interleaved = Copy(samples, ref mono);
        if (channels == 1) return interleaved;
        // Mixed down in place: sample i is written only after everything before sample i * channels was read.
        var result = interleaved[..(interleaved.Length / channels)];
        if (FindDominantChannel(interleaved, channels) is { } only)
        {
            for (int index = 0; index < result.Length; index++) result[index] = interleaved[index * channels + only];
            return result;
        }
        for (int index = 0; index < result.Length; index++)
        {
            float sum = 0;
            for (int channel = 0; channel < channels; channel++) sum += interleaved[index * channels + channel];
            result[index] = sum / channels;
        }
        return result;
    }

    /// <summary>
    /// Package 142 (A151): the microphone's samples as interleaved stereo from its first two channels, without the mono mix of
    /// <see cref="ReadMono"/> (one-sided rule or average), written into <paramref name="stereo"/> like there. There are at least 2 channels.
    /// Package 142 (review): with <paramref name="sides"/> an instrument on one input of a two-input interface plays on
    /// both sides at full level (Package 50's one-sided rule, with hysteresis, see StereoInput).
    /// </summary>
    public static Span<float> ReadStereo(ReadOnlySpan<float> samples, int channels, ref float[] stereo, StereoInput? sides = null)
    {
        var interleaved = Copy(samples, ref stereo);
        var result = interleaved[..(interleaved.Length / channels * 2)];
        if (channels > 2)
        {
            // in place like ReadMono: frame i is written only after everything before sample i * channels was read
            for (int index = 0; index < result.Length / 2; index++)
            {
                float left = interleaved[index * channels], right = interleaved[index * channels + 1];
                result[2 * index] = left;
                result[2 * index + 1] = right;
            }
        }
        if (sides?.OnlySide(result, AudioFormat.SampleRate) is { } only)
            for (int index = 0; index < result.Length / 2; index++) result[2 * index + 1 - only] = result[2 * index + only];
        return result;
    }

    /// <summary>
    /// Interleaved stereo averaged into <paramref name="mono"/>, one sample per pair, as many as
    /// <paramref name="mono"/> holds; <paramref name="stereo"/> holds at least two samples per entry of it. A mono
    /// signal played on both sides comes out unchanged.
    /// </summary>
    public static void Downmix(ReadOnlySpan<float> stereo, Span<float> mono)
    {
        for (int index = 0; index < mono.Length; index++) mono[index] = 0.5f * (stereo[2 * index] + stereo[2 * index + 1]);
    }

    /// <summary>Package 180: the samples, still interleaved, copied into <paramref name="buffer"/> (grown when needed); the mixdown works in place there.</summary>
    static Span<float> Copy(ReadOnlySpan<float> samples, ref float[] buffer)
    {
        if (buffer.Length < samples.Length) buffer = new float[samples.Length];
        var interleaved = buffer.AsSpan(0, samples.Length);
        samples.CopyTo(interleaved);
        return interleaved;
    }

    /// <summary>
    /// Package 50 (A53): a channel at least 6 dB (four times the energy) above the average of the others carries the
    /// voice alone, e.g. an interface or headset that reports stereo but fills one side. Averaging would halve it.
    /// </summary>
    static int? FindDominantChannel(ReadOnlySpan<float> interleaved, int channels)
    {
        Span<double> energy = channels <= 32 ? stackalloc double[channels] : new double[channels];
        for (int index = 0; index < interleaved.Length; index++) energy[index % channels] += interleaved[index] * interleaved[index];
        int loudest = 0;
        double total = 0;
        for (int channel = 0; channel < channels; channel++)
        {
            total += energy[channel];
            if (energy[channel] > energy[loudest]) loudest = channel;
        }
        double others = (total - energy[loudest]) / (channels - 1);
        return energy[loudest] > 0 && energy[loudest] >= 4 * others ? loudest : null;
    }
}
