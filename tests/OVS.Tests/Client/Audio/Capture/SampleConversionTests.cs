using OVS.Client.Audio.Capture;
using OVS.Client.Audio.Codec;

namespace OVS.Tests.Client.Audio.Capture;

/// <summary>The microphone's samples as mono or stereo, and the stereo downmix.</summary>
public class SampleConversionTests
{
    static bool Near(float actual, float expected) => Math.Abs(actual - expected) < 1e-6f;

    static float[] ReadMono(float[] interleaved, int channels)
    {
        float[] buffer = [];
        return SampleConversion.ReadMono(interleaved, channels, ref buffer).ToArray();
    }

    [Fact]
    public void ReadMono_FloatStereo_Averages()
    {
        var mono = ReadMono([0.2f, 0.4f, -1f, 1f], 2);
        Assert.Equal(0.3f, mono[0], 5);
        Assert.Equal(0f, mono[1], 5);
    }

    /// <summary>Package 50 (A53): interfaces and some headsets deliver the voice on one channel only; averaging halved it.</summary>
    [Fact]
    public void ReadMono_StereoSignalOnOneChannel_KeepsFullLevel()
    {
        foreach (int voiced in new[] { 0, 1 })
        {
            var stereo = new float[2 * AudioFormat.HalfFrameSamples];
            for (int index = 0; index < AudioFormat.HalfFrameSamples; index++) stereo[2 * index + voiced] = 0.5f * MathF.Sin(2 * MathF.PI * 440 * index / 48_000f);
            var mono = ReadMono(stereo, 2);
            Assert.Equal(AudioFormat.HalfFrameSamples, mono.Length);
            Assert.InRange(mono.Max(), 0.49f, 0.5f);
            for (int index = 0; index < AudioFormat.HalfFrameSamples; index++) Assert.Equal(stereo[2 * index + voiced], mono[index]);
        }
    }

    [Fact]
    public void ReadMono_FourSimilarChannels_AveragesAll()
    {
        Assert.Equal([0.25f, -0.25f], ReadMono([0.1f, 0.2f, 0.3f, 0.4f, -0.1f, -0.2f, -0.3f, -0.4f], 4), Near);
    }

    [Fact]
    public void ReadMono_LastOfThreeChannelsDominant_TakesItAlone()
    {
        Assert.Equal([0.5f, -0.5f], ReadMono([0.01f, 0.02f, 0.5f, -0.01f, 0.02f, -0.5f], 3));
    }

    /// <summary>The rule's edge: exactly four times the others' energy (6 dB) is dominant, a little less is averaged.</summary>
    [Fact]
    public void ReadMono_LoudestAtAndBelowSixDb_DominantThenAveraged()
    {
        Assert.Equal([0.5f, 0.5f], ReadMono([0.5f, 0.25f, 0.5f, 0.25f], 2));
        Assert.Equal([0.38f, 0.38f], ReadMono([0.5f, 0.26f, 0.5f, 0.26f], 2), Near);
    }

    /// <summary>Package 124: nothing allocated per read while the caller's buffer is large enough.</summary>
    [Fact]
    public void ReadMono_BufferLargeEnough_KeptAndOnlyTheGivenSamplesRead()
    {
        float[] samples = [0.2f, 0.4f, 0.6f, 0.8f];
        float[] buffer = new float[16];
        var kept = buffer;
        var mono = SampleConversion.ReadMono(samples.AsSpan(0, 2), 1, ref buffer).ToArray();
        Assert.Same(kept, buffer);
        Assert.Equal([0.2f, 0.4f], mono);
    }

    [Fact]
    public void ReadMono_BufferTooSmall_GrowsToTheSampleCount()
    {
        float[] samples = [0.2f, 0.4f, 0.6f, 0.8f];
        float[] small = new float[1];
        SampleConversion.ReadMono(samples, 1, ref small);
        Assert.Equal(4, small.Length);
    }

    [Fact]
    public void ReadStereo_FourChannels_KeepsTheFirstTwo()
    {
        float[] four = [0.1f, 0.2f, 0.3f, 0.4f, -0.1f, -0.2f, -0.3f, -0.4f];
        float[] buffer = [];
        var stereo = SampleConversion.ReadStereo(four, 4, ref buffer).ToArray();
        Assert.Equal([0.1f, 0.2f, -0.1f, -0.2f], stereo);
    }

    [Fact]
    public void ReadStereo_OneSidedWithoutSides_StaysAsItCame()
    {
        float[] samples = [0.5f, 0f, -0.5f, 0f];
        float[] buffer = [];
        var stereo = SampleConversion.ReadStereo(samples, 2, ref buffer).ToArray();
        Assert.Equal(samples, stereo);
    }

    [Fact]
    public void Downmix_StereoPairs_AveragedIntoMono()
    {
        float[] stereo = [0.2f, 0.4f, -1f, 1f, 0.5f, 0.25f, 0.9f, 0.9f];
        var mono = new float[3];
        SampleConversion.Downmix(stereo, mono);
        Assert.Equal([0.5f * (0.2f + 0.4f), 0f, 0.5f * (0.5f + 0.25f)], mono);
    }
}
