using NAudio.Dsp;

namespace OVS.Client.Audio.Capture;

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
    // 20 kHz tone 45 dB louder (SendPathTests.Resampler_ToneAbove24k_NoAliasing). The table costs 65 KB once.
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
