using OVS.Client.Audio.Playback;
using OVS.Client.Models;
using OVS.Client.Services;
using OVS.Client.ViewModels;
using OVS.Shared.Voice;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.ViewModels;

public class ConnectionStatsViewModelTests
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

        var stats = ConnectionStats.CreateBlank();
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

        stats.Reset();
        Assert.Equal((SignalRating.Unknown, 0, (double?)null), (stats.Rating, stats.Bars, stats.BaselineMs));
    }

    /// <summary>Package 133: the frame counts start at the connection, though the mixer counts on.</summary>
    [Fact]
    public void Counts_SinceReset()
    {
        mixerCounts = new ReceiveStats(100, 10, 4, 7, 0, 0);
        var stats = CreateStats();
        stats.Reset();
        stats.Update(null, default, default, new ReceiveStats(150, 12, 5, 9, 3.25, 40), 32);
        Assert.Equal("-", stats.RoundTripText);
        Assert.Contains("2", stats.ConcealedText);
        Assert.Contains("1", stats.ConcealedText);
        Assert.DoesNotContain("9", stats.ConcealedText);
        Assert.Equal("40 ms", stats.DelayText);
        Assert.Equal("32 kbps", stats.BitrateText);
    }

    // ---- Refresh: what the voice connection, the mixer and the encoder give it, and when ----

    static readonly VoiceLinkFigures FirstAnswer = new(TimeSpan.FromMilliseconds(40), RoundTripCount: 1, Uplink: new LossReport(250, 25), Downlink: new LossReport(100, 5));

    readonly ManualTimeProvider clock = new();
    bool? udpReachable = true;
    VoiceLinkFigures figures = FirstAnswer;
    ReceiveStats mixerCounts = new(0, 0, 0, 0, JitterMs: 3.5, DelayMs: 60);    int encoderBitrate = 64;
    int figuresRead;

    ConnectionStatsViewModel CreateStats() => new(clock, () => udpReachable, () =>
    {
        figuresRead++;
        return figures;
    }, () => mixerCounts, () => encoderBitrate);

    [Fact]
    public void Refresh_NoVoiceConnection_ShowsNothingAndAsksForNoFigures()
    {
        udpReachable = null;
        var stats = CreateStats();

        stats.Refresh();

        Assert.Equal((false, "-", 0), (stats.Unreachable, stats.BitrateText, figuresRead));
    }

    [Fact]
    public void Refresh_FirstCall_ShowsTheRoundTripBothLossFiguresAndTheMixerAndEncoderFigures()
    {
        var stats = CreateStats();

        stats.Refresh();

        Assert.Contains("40 ms", stats.RoundTripText);
        Assert.Contains("25/250", stats.UplinkText);
        Assert.Contains("5/100", stats.DownlinkText);
        Assert.Equal("3.5 ms", stats.JitterText);
        Assert.Equal("60 ms", stats.DelayText);
        Assert.Equal("64 kbps", stats.BitrateText);
    }

    [Fact]
    public void Refresh_UdpUnreachable_CrossesTheIconOut()
    {
        udpReachable = false;
        var stats = CreateStats();

        stats.Refresh();

        Assert.True(stats.Unreachable);
    }

    [Fact]
    public void Refresh_UdpReachableAgainWithinTheInterval_ClearsTheCrossingAtOnce()
    {
        udpReachable = false;
        var stats = CreateStats();
        stats.Refresh();
        udpReachable = true;

        stats.Refresh();

        Assert.False(stats.Unreachable);
    }

    [Fact]
    public void Refresh_JustBeforeTheInterval_KeepsTheFiguresAndAsksForNoNewOnes()
    {
        var stats = CreateStats();
        stats.Refresh();
        encoderBitrate = 96;
        clock.Advance(ConnectionStatsViewModel.RefreshInterval - TimeSpan.FromMilliseconds(1));
        int readsBefore = figuresRead;

        stats.Refresh();

        Assert.Equal(("64 kbps", readsBefore), (stats.BitrateText, figuresRead));
    }

    [Fact]
    public void Refresh_AfterTheInterval_ReadsAndShowsTheNewFigures()
    {
        var stats = CreateStats();
        stats.Refresh();
        encoderBitrate = 96;
        clock.Advance(ConnectionStatsViewModel.RefreshInterval);
        int readsBefore = figuresRead;

        stats.Refresh();

        Assert.Equal(("96 kbps", readsBefore + 1), (stats.BitrateText, figuresRead));
    }

    [Fact]
    public void RefreshInterval_Constant_IsTwoSeconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), ConnectionStatsViewModel.RefreshInterval);
    }

    [Fact]
    public void Refresh_NewAnswer_TakesTheRoundTripIntoTheBaselineOnce()
    {
        var stats = CreateStats();

        stats.Refresh();

        Assert.Equal(1, stats.Samples);
    }

    [Fact]
    public void Refresh_NoNewAnswerSinceTheLast_KeepsTheBaseline()
    {
        var stats = CreateStats();
        stats.Refresh();
        clock.Advance(ConnectionStatsViewModel.RefreshInterval);
        int samplesBefore = stats.Samples;

        stats.Refresh();

        Assert.Equal(samplesBefore, stats.Samples);
    }

    [Fact]
    public void Refresh_AnotherAnswer_TakesItIntoTheBaseline()
    {
        var stats = CreateStats();
        stats.Refresh();
        figures = FirstAnswer with { RoundTrip = TimeSpan.FromMilliseconds(55), RoundTripCount = 2 };
        clock.Advance(ConnectionStatsViewModel.RefreshInterval);
        int samplesBefore = stats.Samples;

        stats.Refresh();

        Assert.Equal(samplesBefore + 1, stats.Samples);
    }

    [Fact]
    public void Reset_AfterARefresh_MakesTheNextRefreshDueAtOnce()
    {
        var stats = CreateStats();
        stats.Refresh();
        encoderBitrate = 96;

        stats.Reset();
        stats.Refresh();

        Assert.Equal("96 kbps", stats.BitrateText);
    }

    [Fact]
    public void Reset_AfterAnAnswer_CountsTheSameAnswerCountAsNewAgain()
    {
        var stats = CreateStats();
        stats.Refresh();

        stats.Reset();
        stats.Refresh();

        Assert.Equal(1, stats.Samples);
    }

    [Fact]
    public void Refresh_AfterAResetAtSevenConcealedFrames_CountsOnlyTheFramesSince()
    {
        var stats = CreateStats();
        mixerCounts = new ReceiveStats(0, 0, 0, Concealed: 7, JitterMs: 0, DelayMs: 0);

        stats.Reset();
        mixerCounts = new ReceiveStats(0, 0, 0, Concealed: 9, JitterMs: 0, DelayMs: 0);
        stats.Refresh();

        Assert.Contains("2", stats.ConcealedText);
        Assert.DoesNotContain("9", stats.ConcealedText);
    }
}
