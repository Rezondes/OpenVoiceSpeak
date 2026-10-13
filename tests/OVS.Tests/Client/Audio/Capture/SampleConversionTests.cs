using NAudio.Wave;
using OVS.Client.Audio.Capture;
using OVS.Client.Audio.Codec;

namespace OVS.Tests.Client.Audio.Capture;

/// <summary>The capture buffer's conversions to float samples, mono or stereo, and the stereo downmix.</summary>
public class SampleConversionTests
{
    static byte[] Bytes(Array samples)
    {
        var bytes = new byte[Buffer.ByteLength(samples)];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    static bool Near(float a, float b) => Math.Abs(a - b) < 1e-6f;

    static float[] ReadMono(float[] interleaved, int channels)
    {
        float[] buffer = [];
        return SampleConversion.ReadMono(Bytes(interleaved), interleaved.Length * sizeof(float), WaveFormat.CreateIeeeFloatWaveFormat(48_000, channels), ref buffer).ToArray();
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
    public void ReadMono_Pcm16Mono_Scales()
    {
        short[] pcm = [16384, -32768];
        float[] buffer = [];
        Assert.Equal([0.5f, -1f], SampleConversion.ReadMono(Bytes(pcm), 4, new WaveFormat(48_000, 16, 1), ref buffer).ToArray());
    }

    [Fact]
    public void ReadMono_Pcm16Stereo_ScalesAndAverages()
    {
        short[] pcm = [16384, 16384, -32768, -16384];
        float[] buffer = [];
        Assert.Equal([0.5f, -0.75f], SampleConversion.ReadMono(Bytes(pcm), 8, new WaveFormat(48_000, 16, 2), ref buffer).ToArray());
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

    [Fact]
    public void ReadMono_ExtensibleFloatFormat_ReadsFloats()
    {
        float[] stereo = [0.2f, 0.4f, -1f, 1f];
        float[] buffer = [];
        var mono = SampleConversion.ReadMono(Bytes(stereo), 16, new WaveFormatExtensible(48_000, 32, 2), ref buffer).ToArray();
        Assert.Equal([0.3f, 0f], mono, Near);
    }

    [Fact]
    public void ReadMono_Pcm24_ThrowsNotSupportedInEnglish()
    {
        var format = new WaveFormat(48_000, 24, 1);
        float[] buffer = [];
        var error = Assert.Throws<NotSupportedException>(() => SampleConversion.ReadMono(new byte[6], 6, format, ref buffer));
        Assert.Equal($"Capture format {format} is not supported", error.Message);
    }

    /// <summary>Package 124: nothing allocated per callback while the caller's buffer is large enough.</summary>
    [Fact]
    public void ReadMono_BufferLargeEnough_KeptAndOnlyTheGivenBytesRead()
    {
        float[] samples = [0.2f, 0.4f, 0.6f, 0.8f];
        float[] buffer = new float[16];
        var kept = buffer;
        var mono = SampleConversion.ReadMono(Bytes(samples), 8, WaveFormat.CreateIeeeFloatWaveFormat(48_000, 1), ref buffer).ToArray();
        Assert.Same(kept, buffer);
        Assert.Equal([0.2f, 0.4f], mono);
    }

    [Fact]
    public void ReadMono_BufferTooSmall_GrowsToTheSampleCount()
    {
        float[] samples = [0.2f, 0.4f, 0.6f, 0.8f];
        float[] small = new float[1];
        SampleConversion.ReadMono(Bytes(samples), 16, WaveFormat.CreateIeeeFloatWaveFormat(48_000, 1), ref small);
        Assert.Equal(4, small.Length);
    }

    [Fact]
    public void ReadStereo_FourChannels_KeepsTheFirstTwo()
    {
        float[] four = [0.1f, 0.2f, 0.3f, 0.4f, -0.1f, -0.2f, -0.3f, -0.4f];
        float[] buffer = [];
        var stereo = SampleConversion.ReadStereo(Bytes(four), 32, WaveFormat.CreateIeeeFloatWaveFormat(48_000, 4), ref buffer).ToArray();
        Assert.Equal([0.1f, 0.2f, -0.1f, -0.2f], stereo);
    }

    [Fact]
    public void ReadStereo_OneSidedWithoutSides_StaysAsItCame()
    {
        float[] samples = [0.5f, 0f, -0.5f, 0f];
        float[] buffer = [];
        var stereo = SampleConversion.ReadStereo(Bytes(samples), 16, WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2), ref buffer).ToArray();
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
