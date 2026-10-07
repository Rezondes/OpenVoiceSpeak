using System.Net;
using OVS.Server;
using OVS.Server.Throttling;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server.Throttling;

public sealed class AdminTokenGuessLimitTests
{
    static readonly IPAddress Source = IPAddress.Parse("10.0.0.1");
    static readonly IPAddress OtherSource = IPAddress.Parse("10.0.0.2");
    static readonly TimeSpan Tick = TimeSpan.FromTicks(1);

    readonly ManualTimeProvider time = new();
    readonly AdminTokenGuessLimit limit = new();

    DateTimeOffset Now => time.GetUtcNow();

    void Fail(int times)
    {
        for (int i = 0; i < times; i++) limit.RecordFailure(Source, Now);
    }

    [Fact]
    public void IsBlocked_BelowLimit_False()
    {
        Fail(ServerTuning.AdminTokenFailures - 1);
        Assert.False(limit.IsBlocked(Source, Now));
    }

    [Fact]
    public void IsBlocked_LimitReached_True()
    {
        Fail(ServerTuning.AdminTokenFailures);
        Assert.True(limit.IsBlocked(Source, Now));
    }

    [Fact]
    public void IsBlocked_JustBeforeTheWindowEnds_True()
    {
        Fail(ServerTuning.AdminTokenFailures);
        time.Advance(ServerTuning.AdminTokenWindow - Tick);
        Assert.True(limit.IsBlocked(Source, Now));
    }

    [Fact]
    public void IsBlocked_WindowAfterTheFirstFailure_False()
    {
        Fail(1);
        time.Advance(ServerTuning.AdminTokenWindow - TimeSpan.FromMinutes(1));
        Fail(ServerTuning.AdminTokenFailures - 1); // later failures do not move the window
        Assert.True(limit.IsBlocked(Source, Now));
        time.Advance(TimeSpan.FromMinutes(1));
        Assert.False(limit.IsBlocked(Source, Now));
    }

    [Fact]
    public void RecordFailure_AfterTheWindowWasChecked_CountStartsAgain()
    {
        Fail(ServerTuning.AdminTokenFailures);
        time.Advance(ServerTuning.AdminTokenWindow);
        Assert.False(limit.IsBlocked(Source, Now));
        Fail(ServerTuning.AdminTokenFailures - 1);
        Assert.False(limit.IsBlocked(Source, Now));
    }

    [Fact]
    public void IsBlocked_OtherSource_False()
    {
        Fail(ServerTuning.AdminTokenFailures);
        Assert.False(limit.IsBlocked(OtherSource, Now));
    }
}
