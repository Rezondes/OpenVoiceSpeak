using OVS.Client.Audio;

namespace OVS.Tests.Client;

/// <summary>Package 128 (A137): Fluxer's volume curve for the per-user and the output volume.</summary>
public class VolumeCurveTests
{
    [Fact]
    public void Gain_KnownPoints_Monotonic()
    {
        Assert.Equal(1f, VolumeCurve.Gain(1f), 5);
        Assert.Equal(4f, VolumeCurve.Gain(2f), 4);
        Assert.Equal(2f, VolumeCurve.Gain(1.5f), 4);
        Assert.Equal(0.25f, VolumeCurve.Gain(0.5f), 2);
        Assert.Equal(0f, VolumeCurve.Gain(0f));
        float last = -1;
        for (int percent = 0; percent <= 200; percent++)
        {
            float gain = VolumeCurve.Gain(percent / 100f);
            Assert.True(gain > last, $"{percent} %: {gain} after {last}");
            last = gain;
        }
    }

    [Fact]
    public void Combine_CapsAtTwelve()
    {
        Assert.Equal(12f, VolumeCurve.Combine(2f, 2f), 4);
        Assert.Equal(VolumeCurve.MaxGain, VolumeCurve.Combine(10f, 10f));
        Assert.Equal(4f, VolumeCurve.Combine(2f, 1f), 4);
        Assert.Equal(8f, VolumeCurve.Combine(2f, 1.5f), 3);
        Assert.Equal(0f, VolumeCurve.Combine(0f, 2f));
    }
}
