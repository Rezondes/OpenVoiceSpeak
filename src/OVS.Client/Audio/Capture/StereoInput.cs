namespace OVS.Client.Audio.Capture;

/// <summary>
/// Package 142 (review): whether a stereo device in music mode carries its signal on one side only, e.g. an instrument
/// on one input of a two-input interface. With hysteresis, as the two mistakes cost differently: a wrong stereo
/// decision only leaves the instrument 6 dB quieter on one side, a wrong one-sided one discards the other input. So it
/// goes one-sided only after one side has been at least 6 dB above the other (Package 50's rule) in every chunk with
/// signal for <see cref="OneSidedAfter"/>, and back to stereo as soon as the weak side, averaged over about 100 ms,
/// rises to within 10 dB of the strong one (a second input joining, e.g. a voice microphone next to a guitar). Silence
/// changes nothing. Capture thread; nothing allocated per chunk.
/// </summary>
public sealed class StereoInput
{
    /// <summary>A side silent this long while the other carries sound counts as unused.</summary>
    public static readonly TimeSpan OneSidedAfter = TimeSpan.FromSeconds(2.5);
    const double SilentBelow = 1e-7; // mean square of the louder side: -70 dBFS, so a quiet instrument is still judged
    const double FastSeconds = 0.1; // the averages that bring stereo back
    double fastLeft, fastRight;
    long run; // sample frames with signal in a row on which the same side was the only one
    int runSide = -1;
    int? only;

    /// <returns>The side (0 left, 1 right) that alone carries the signal, or null for stereo.</returns>
    public int? OnlySide(ReadOnlySpan<float> stereo, int sampleRate)
    {
        double left = 0, right = 0;
        for (int index = 0; index + 1 < stereo.Length; index += 2)
        {
            left += stereo[index] * stereo[index];
            right += stereo[index + 1] * stereo[index + 1];
        }
        int frames = stereo.Length / 2;
        if (frames == 0 || Math.Max(left, right) / frames < SilentBelow) return only; // silence says nothing
        left /= frames;
        right /= frames;
        double step = Math.Min(1, frames / (FastSeconds * sampleRate));
        fastLeft += step * (left - fastLeft);
        fastRight += step * (right - fastRight);

        int side = left >= 4 * right ? 0 : right >= 4 * left ? 1 : -1;
        run = side >= 0 && side == runSide ? run + frames : side >= 0 ? frames : 0;
        runSide = side;
        if (only is { } one)
        {
            double strong = one == 0 ? fastLeft : fastRight, weak = one == 0 ? fastRight : fastLeft;
            if (weak >= strong / 10) only = null; // within 10 dB: the other input carries something
        }
        else if (side >= 0 && run >= OneSidedAfter.TotalSeconds * sampleRate)
        {
            only = side;
        }
        return only;
    }
}
