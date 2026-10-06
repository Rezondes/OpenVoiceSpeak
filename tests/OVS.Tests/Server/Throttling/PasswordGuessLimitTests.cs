using System.Net;
using OVS.Server;
using OVS.Server.Throttling;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server.Throttling;

public sealed class PasswordGuessLimitTests
{
    static readonly IPAddress Source = IPAddress.Parse("10.0.0.1");
    static readonly IPAddress OtherSource = IPAddress.Parse("10.0.0.2");
    static readonly IPAddress ThirdSource = IPAddress.Parse("10.0.0.3");
    static readonly IPAddress FourthSource = IPAddress.Parse("10.0.0.4");
    static readonly TimeSpan Tick = TimeSpan.FromTicks(1);

    readonly ManualTimeProvider time = new();
    readonly PasswordGuessLimit limit = new();

    DateTimeOffset Now => time.GetUtcNow();

    void Fail(int times, IPAddress? source = null)
    {
        for (int i = 0; i < times; i++) limit.RecordFailure(source ?? Source, Now);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(4, 0)]
    [InlineData(5, 1)]
    [InlineData(6, 2)]
    [InlineData(7, 4)]
    [InlineData(10, 32)]
    [InlineData(11, 60)]
    [InlineData(12, 60)]
    [InlineData(1000, 60)]
    public void BlockFor_Failures_DoublesFromTheFifthUpToAnHour(int failures, int minutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(minutes), PasswordGuessLimit.BlockFor(failures));
    }

    [Fact]
    public void IsBlocked_NoFailure_False()
    {
        Assert.False(limit.IsBlocked(Source, Now));
    }

    [Fact]
    public void IsBlocked_BelowFreeFailures_False()
    {
        Fail(Limits.FreePasswordFailures - 1);
        Assert.False(limit.IsBlocked(Source, Now));
    }

    [Fact]
    public void IsBlocked_FreeFailuresReached_TrueUntilOneMinuteAfterTheLast()
    {
        Fail(Limits.FreePasswordFailures);
        Assert.True(limit.IsBlocked(Source, Now));
        time.Advance(TimeSpan.FromMinutes(1) - Tick);
        Assert.True(limit.IsBlocked(Source, Now));
        time.Advance(Tick);
        Assert.False(limit.IsBlocked(Source, Now));
    }

    [Fact]
    public void IsBlocked_OtherSource_False()
    {
        Fail(Limits.FreePasswordFailures);
        Assert.False(limit.IsBlocked(OtherSource, Now));
    }

    [Fact]
    public void RecordFailure_JustWithinTheMemory_CountGoesOn()
    {
        Fail(Limits.FreePasswordFailures);
        time.Advance(Limits.PasswordFailureMemory - Tick);
        Assert.False(limit.IsBlocked(Source, Now));
        Fail(1);
        Assert.True(limit.IsBlocked(Source, Now + TimeSpan.FromMinutes(2) - Tick)); // the 6th: two minutes
        Assert.False(limit.IsBlocked(Source, Now + TimeSpan.FromMinutes(2)));
    }

    [Fact]
    public void RecordFailure_MemoryAfterTheLast_CountStartsAgain()
    {
        Fail(Limits.FreePasswordFailures);
        time.Advance(Limits.PasswordFailureMemory);
        Fail(1);
        Assert.False(limit.IsBlocked(Source, Now));
    }

    [Fact]
    public void RecordFailure_MemoryAfterTheLastBeforeTheSweep_CountStartsAgain()
    {
        Fail(1, OtherSource); // the first call sweeps and sets the next sweep a memory later
        time.Advance(Limits.PasswordFailureMemory - TimeSpan.FromHours(1));
        Fail(Limits.FreePasswordFailures);
        time.Advance(TimeSpan.FromHours(1));
        Fail(1, ThirdSource); // sweeps OtherSource, keeps Source
        time.Advance(Limits.PasswordFailureMemory - TimeSpan.FromHours(1));
        Fail(1); // Source's last is a memory ago, no sweep is due: the count itself starts again
        Assert.False(limit.IsBlocked(Source, Now));
    }

    [Fact]
    public void RecordFailure_SweepJustBeforeTheMemoryEnds_KeepsTheOldSource()
    {
        Fail(1);
        time.Advance(Limits.PasswordFailureMemory - Tick);
        Fail(1, OtherSource);
        Assert.Equal(2, limit.SourceCount);
    }

    [Fact]
    public void RecordFailure_SweepAMemoryLater_DropsForgottenSources()
    {
        Fail(1);
        time.Advance(Limits.PasswordFailureMemory);
        Fail(1, OtherSource);
        Assert.Equal(1, limit.SourceCount);
    }

    [Fact]
    public void RecordFailure_SweepOncePerMemory_NotBeforeTheNextSweep()
    {
        Fail(1, OtherSource); // the first call sweeps and sets the next sweep a memory later
        time.Advance(Limits.PasswordFailureMemory - TimeSpan.FromHours(1));
        Fail(1);
        time.Advance(TimeSpan.FromHours(1));
        Fail(1, ThirdSource); // sweeps OtherSource, keeps Source, the next sweep a memory later
        time.Advance(Limits.PasswordFailureMemory - TimeSpan.FromHours(1));
        Fail(1, FourthSource); // Source is a memory old now, but no sweep is due yet
        Assert.Equal(3, limit.SourceCount);
    }
}
