using OVS.Client.Audio.Playback;

namespace OVS.Tests.Client.Audio.Playback;

/// <summary>Package 133: the jitter buffers' counts summed over the speakers, the worst jitter and delay of those present.</summary>
public sealed class ReceiveStatsTests
{
    [Fact]
    public void Add_TwoSpeakers_SumsTheCountsAndKeepsTheWorstJitterAndDelay()
    {
        var calm = new ReceiveStats(Received: 100, Lost: 2, Rebuilt: 1, Concealed: 3, JitterMs: 4.5, DelayMs: 80, Recovered: 5);
        var shaky = new ReceiveStats(Received: 50, Lost: 7, Rebuilt: 2, Concealed: 4, JitterMs: 12.25, DelayMs: 40, Recovered: 1);

        var both = calm.Add(shaky);

        Assert.Equal(new ReceiveStats(150, 9, 3, 7, 12.25, 80, 6), both);
        Assert.Equal(both, shaky.Add(calm)); // the order of the speakers does not matter
    }

    [Fact]
    public void Add_ToNothing_KeepsTheOneSpeaker()
    {
        var one = new ReceiveStats(Received: 10, Lost: 1, Rebuilt: 0, Concealed: 2, JitterMs: 3, DelayMs: 60);

        Assert.Equal(one, default(ReceiveStats).Add(one));
    }
}
