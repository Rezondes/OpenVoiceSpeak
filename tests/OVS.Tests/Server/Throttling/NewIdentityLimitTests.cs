using System.Net;
using OVS.Server;
using OVS.Server.Throttling;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server.Throttling;

public sealed class NewIdentityLimitTests
{
    static readonly IPAddress Source = IPAddress.Parse("10.0.0.1");
    static readonly IPAddress OtherSource = IPAddress.Parse("10.0.0.2");
    static readonly TimeSpan Tick = TimeSpan.FromTicks(1);

    readonly ManualTimeProvider time = new();
    readonly NewIdentityLimit limit = new();

    DateTimeOffset Now => time.GetUtcNow();

    bool Count(IPAddress? source = null) => limit.TryCount(source ?? Source, Now, out _);

    void Fill(IPAddress? source = null)
    {
        for (int i = 0; i < ServerTuning.NewIdentitiesPerHour; i++) Assert.True(Count(source));
    }

    [Fact]
    public void TryCount_UpToTheLimit_Counted()
    {
        Fill();
    }

    [Fact]
    public void TryCount_PastTheLimit_RefusedFirstRefusalOnce()
    {
        Fill();
        Assert.False(limit.TryCount(Source, Now, out bool first));
        Assert.True(first);
        Assert.False(limit.TryCount(Source, Now, out first));
        Assert.False(first);
    }

    [Fact]
    public void TryCount_Counted_NoFirstRefusal()
    {
        Assert.True(limit.TryCount(Source, Now, out bool first));
        Assert.False(first);
    }

    [Fact]
    public void TryCount_JustBeforeTheWindowEnds_Refused()
    {
        Fill();
        time.Advance(ServerTuning.NewIdentityWindow - Tick);
        Assert.False(Count());
    }

    [Fact]
    public void TryCount_WindowAfterTheFirst_CountedAgain()
    {
        Assert.True(Count());
        time.Advance(ServerTuning.NewIdentityWindow / 2);
        for (int i = 1; i < ServerTuning.NewIdentitiesPerHour; i++) Assert.True(Count()); // later ones do not move the window
        Assert.False(Count());
        time.Advance(ServerTuning.NewIdentityWindow / 2);
        Assert.True(Count());
    }

    [Fact]
    public void TryCount_NextWindowPastTheLimit_FirstRefusalAgain()
    {
        Fill();
        Assert.False(Count());
        time.Advance(ServerTuning.NewIdentityWindow);
        Fill();
        Assert.False(limit.TryCount(Source, Now, out bool first));
        Assert.True(first);
    }

    [Fact]
    public void TryCount_WindowEndedBeforeTheSweep_CountedAndFirstRefusalAgain()
    {
        Assert.True(Count(OtherSource)); // the first call sweeps and sets the next sweep a window later
        time.Advance(ServerTuning.NewIdentityWindow - TimeSpan.FromMinutes(1));
        Fill();
        Assert.False(Count());
        time.Advance(TimeSpan.FromMinutes(1));
        Assert.True(Count(IPAddress.Parse("10.0.0.3"))); // sweeps OtherSource, keeps Source
        time.Advance(ServerTuning.NewIdentityWindow - TimeSpan.FromMinutes(1));
        Fill(); // Source's window ended, no sweep is due: the window itself starts again
        Assert.False(limit.TryCount(Source, Now, out bool first));
        Assert.True(first);
    }

    [Fact]
    public void TryCount_OtherSource_OwnLimit()
    {
        Fill();
        Assert.False(Count());
        Fill(OtherSource);
    }

    [Fact]
    public void TryCount_SweepJustBeforeTheWindowEnds_KeepsTheOldSource()
    {
        Assert.True(Count());
        time.Advance(ServerTuning.NewIdentityWindow - Tick);
        Assert.True(Count(OtherSource));
        Assert.Equal(2, limit.SourceCount);
    }

    [Fact]
    public void TryCount_SweepAWindowLater_DropsEndedSources()
    {
        Assert.True(Count());
        time.Advance(ServerTuning.NewIdentityWindow);
        Assert.True(Count(OtherSource));
        Assert.Equal(1, limit.SourceCount);
    }

    [Fact]
    public void TryCount_SweepOncePerWindow_NotBeforeTheNextSweep()
    {
        Assert.True(Count(OtherSource)); // the first call sweeps and sets the next sweep a window later
        time.Advance(ServerTuning.NewIdentityWindow - TimeSpan.FromMinutes(1));
        Assert.True(Count());
        time.Advance(TimeSpan.FromMinutes(1));
        Assert.True(Count(IPAddress.Parse("10.0.0.3"))); // sweeps OtherSource, keeps Source, the next sweep a window later
        time.Advance(ServerTuning.NewIdentityWindow - TimeSpan.FromMinutes(1));
        Assert.True(Count(IPAddress.Parse("10.0.0.4"))); // Source's window ended now, but no sweep is due yet
        Assert.Equal(3, limit.SourceCount);
    }
}
