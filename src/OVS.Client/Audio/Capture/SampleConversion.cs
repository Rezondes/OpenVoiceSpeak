using System.Runtime.InteropServices;
using NAudio.Wave;

namespace OVS.Client.Audio.Capture;

/// <summary>
/// The capture buffer's bytes as float samples, mono (one-sided rule or average) or stereo, and the stereo average to
/// mono (Downmix) that the capture and the echo canceller's far end share. Static and stateless; nothing is allocated
/// while the caller's buffer is large enough.
/// </summary>
internal static class SampleConversion
{
    /// <summary>
    /// The capture buffer as mono samples, written into <paramref name="mono"/>, which grows when needed and is kept by
    /// the caller (Package 124: nothing allocated per callback). The result is valid until the next call with it.
    /// </summary>
    public static Span<float> ReadMono(byte[] buffer, int bytes, WaveFormat format, ref float[] mono)
    {
        int channels = format.Channels;
        var interleaved = ReadInterleaved(buffer, bytes, format, ref mono);
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
            for (int c = 0; c < channels; c++) sum += interleaved[index * channels + c];
            result[index] = sum / channels;
        }
        return result;
    }

    /// <summary>
    /// Package 142 (A151): the capture buffer as interleaved stereo from its first two channels, without the mono mix of
    /// <see cref="ReadMono"/> (one-sided rule or average), written into <paramref name="stereo"/> like there. The format has at least 2 channels.
    /// Package 142 (review): with <paramref name="sides"/> an instrument on one input of a two-input interface plays on
    /// both sides at full level (Package 50's one-sided rule, with hysteresis, see StereoInput).
    /// </summary>
    public static Span<float> ReadStereo(byte[] buffer, int bytes, WaveFormat format, ref float[] stereo, StereoInput? sides = null)
    {
        int channels = format.Channels;
        var interleaved = ReadInterleaved(buffer, bytes, format, ref stereo);
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
        if (sides?.OnlySide(result, format.SampleRate) is { } only)
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

    /// <summary>The capture buffer's samples as floats, still interleaved, in <paramref name="samples"/> (grown when needed).</summary>
    static Span<float> ReadInterleaved(byte[] buffer, int bytes, WaveFormat format, ref float[] samples)
    {
        bool isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat || (format is WaveFormatExtensible ext && ext.SubFormat == NAudio.MediaFoundation.AudioSubtypes.MFAudioFormat_Float);
        if (!isFloat && format.BitsPerSample != 16) throw new NotSupportedException($"Capture format {format} is not supported");
        int count = bytes / (isFloat ? sizeof(float) : sizeof(short));
        if (samples.Length < count) samples = new float[count];
        var interleaved = samples.AsSpan(0, count);
        if (isFloat)
        {
            MemoryMarshal.Cast<byte, float>(buffer.AsSpan(0, bytes)).CopyTo(interleaved);
        }
        else
        {
            var shorts = MemoryMarshal.Cast<byte, short>(buffer.AsSpan(0, bytes));
            for (int index = 0; index < shorts.Length; index++) interleaved[index] = shorts[index] / 32768f;
        }
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
        for (int c = 0; c < channels; c++)
        {
            total += energy[c];
            if (energy[c] > energy[loudest]) loudest = c;
        }
        double others = (total - energy[loudest]) / (channels - 1);
        return energy[loudest] > 0 && energy[loudest] >= 4 * others ? loudest : null;
    }
}
