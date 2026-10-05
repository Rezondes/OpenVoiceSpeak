using OVS.Client.Audio.Playback;
using OVS.Client.Models;
using OVS.Client.ViewModels;
using OVS.Shared.Voice;

namespace OVS.Tests.Client;

public class ConnectionStatsTests
{
    static void Sample(ConnectionStatsViewModel stats, double rttMs, LossReport uplink = default) =>
        stats.Update(TimeSpan.FromMilliseconds(rttMs), uplink, default, default, 64);

    /// <summary>
    /// Package 133 (AC4, A142): the round trip is rated against the user's own baseline (median of the last 30), so a far
    /// server is not red for good.
    /// </summary>
    [Fact]
    public void Rating_RelativeToBaseline()
    {
        Assert.Equal(SignalRating.Good, ConnectionStatsViewModel.Rate(100 + 69, 100));
        Assert.Equal(SignalRating.Fair, ConnectionStatsViewModel.Rate(100 + 70, 100));
        Assert.Equal(SignalRating.Fair, ConnectionStatsViewModel.Rate(100 + 159, 100));
        Assert.Equal(SignalRating.Poor, ConnectionStatsViewModel.Rate(100 + 160, 100));
        Assert.Equal(SignalRating.Poor, ConnectionStatsViewModel.Rate(100 + 299, 100));
        Assert.Equal(SignalRating.Bad, ConnectionStatsViewModel.Rate(100 + 300, 100));

        var stats = new ConnectionStatsViewModel();
        Assert.Equal((SignalRating.Unknown, 0, "-"), (stats.Rating, stats.Bars, stats.RoundTripText));
        for (int i = 0; i < 40; i++) Sample(stats, 250); // far away, but steady
        Assert.Equal((SignalRating.Good, 4, 250.0), (stats.Rating, stats.Bars, stats.BaselineMs));

        foreach (var (above, rating, bars) in new[] { (69, SignalRating.Good, 4), (159, SignalRating.Fair, 3), (299, SignalRating.Poor, 2), (300, SignalRating.Bad, 1) })
        {
            Sample(stats, 250 + above); // one sample hardly moves the median
            Assert.Equal((rating, bars), (stats.Rating, stats.Bars));
        }
        Assert.Equal(250.0, stats.BaselineMs);

        for (int i = 0; i < 30; i++) Sample(stats, 600); // the line got slower for good: the baseline follows
        Assert.Equal((SignalRating.Good, 600.0), (stats.Rating, stats.BaselineMs));

        stats.Unreachable = true; // crossed out, no bars
        Assert.Equal(0, stats.Bars);
        stats.Unreachable = false;
        Sample(stats, 600, new LossReport(250, 25));
        Assert.Contains("10", stats.UplinkText);
        Assert.Contains("25/250", stats.UplinkText);

        stats.Reset(default);
        Assert.Equal((SignalRating.Unknown, 0, (double?)null), (stats.Rating, stats.Bars, stats.BaselineMs));
    }

    /// <summary>Package 133: the frame counts start at the connection, though the mixer counts on.</summary>
    [Fact]
    public void Counts_SinceReset()
    {
        var stats = new ConnectionStatsViewModel();
        stats.Reset(new ReceiveStats(100, 10, 4, 7, 0, 0));
        stats.Update(null, default, default, new ReceiveStats(150, 12, 5, 9, 3.25, 40), 32);
        Assert.Equal("-", stats.RoundTripText);
        Assert.Contains("2", stats.ConcealedText);
        Assert.Contains("1", stats.ConcealedText);
        Assert.DoesNotContain("9", stats.ConcealedText);
        Assert.Equal("40 ms", stats.DelayText);
        Assert.Equal("32 kbps", stats.BitrateText);
    }
}
