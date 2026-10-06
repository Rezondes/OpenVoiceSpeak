using OVS.Server;
using OVS.Server.Throttling;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server.Throttling;

public sealed class ChannelPasswordGuessLimitTests
{
    static readonly Guid Channel = Guid.NewGuid();
    static readonly Guid OtherChannel = Guid.NewGuid();
    static readonly TimeSpan Tick = TimeSpan.FromTicks(1);

    readonly ManualTimeProvider time = new();
    readonly ChannelPasswordGuessLimit limit = new();

    DateTimeOffset Now => time.GetUtcNow();

    int Fail(int times, uint session = 1, Guid? channel = null)
    {
        int count = 0;
        for (int i = 0; i < times; i++) count = limit.RecordFailure(session, channel ?? Channel, Now);
        return count;
    }

    [Fact]
    public void IsBlocked_NoFailure_False()
    {
        Assert.False(limit.IsBlocked(1, Channel, Now));
    }

    [Fact]
    public void RecordFailure_EachFailure_ReturnsItsNumber()
    {
        for (int i = 1; i <= Limits.ChannelPasswordFailures; i++)
            Assert.Equal(i, limit.RecordFailure(1, Channel, Now));
    }

    [Fact]
    public void IsBlocked_BelowLimit_False()
    {
        Fail(Limits.ChannelPasswordFailures - 1);
        Assert.False(limit.IsBlocked(1, Channel, Now));
    }

    [Fact]
    public void IsBlocked_LimitReached_TrueForTheBlock()
    {
        Fail(Limits.ChannelPasswordFailures);
        Assert.True(limit.IsBlocked(1, Channel, Now));
        time.Advance(Limits.ChannelPasswordBlock - Tick);
        Assert.True(limit.IsBlocked(1, Channel, Now));
        time.Advance(Tick);
        Assert.False(limit.IsBlocked(1, Channel, Now));
    }

    [Fact]
    public void RecordFailure_AfterTheBlock_CountStartsAgain()
    {
        Fail(Limits.ChannelPasswordFailures);
        time.Advance(Limits.ChannelPasswordBlock);
        Assert.Equal(1, Fail(1));
    }

    [Fact]
    public void RecordFailure_ExactlyAtTheWindowEnd_CountGoesOn()
    {
        Fail(Limits.ChannelPasswordFailures - 1);
        time.Advance(Limits.ChannelPasswordWindow);
        Assert.Equal(Limits.ChannelPasswordFailures, Fail(1));
        Assert.True(limit.IsBlocked(1, Channel, Now));
    }

    [Fact]
    public void RecordFailure_PastTheWindow_CountStartsAgain()
    {
        Fail(Limits.ChannelPasswordFailures - 1);
        time.Advance(Limits.ChannelPasswordWindow + Tick);
        Assert.Equal(1, Fail(1));
        Assert.False(limit.IsBlocked(1, Channel, Now));
    }

    [Fact]
    public void Forget_AfterFailures_CountStartsAgain()
    {
        Fail(Limits.ChannelPasswordFailures - 1);
        limit.Forget(1, Channel);
        Assert.Equal(1, Fail(1));
    }

    [Fact]
    public void Forget_NothingRecorded_NoSessionKept()
    {
        limit.Forget(1, Channel);
        Assert.Equal(0, limit.SessionCount);
    }

    [Fact]
    public void IsBlocked_OtherChannel_False()
    {
        Fail(Limits.ChannelPasswordFailures);
        Assert.False(limit.IsBlocked(1, OtherChannel, Now));
    }

    [Fact]
    public void RecordFailure_OtherChannel_OwnCount()
    {
        Fail(Limits.ChannelPasswordFailures);
        Assert.Equal(1, Fail(1, channel: OtherChannel));
    }

    [Fact]
    public void IsBlocked_OtherSession_False()
    {
        Fail(Limits.ChannelPasswordFailures);
        Assert.False(limit.IsBlocked(2, Channel, Now));
    }

    [Fact]
    public void RecordFailure_OtherSession_OwnCount()
    {
        Fail(Limits.ChannelPasswordFailures);
        Assert.Equal(1, Fail(1, session: 2));
    }

    [Fact]
    public void ForgetSession_Blocked_NothingLeft()
    {
        Fail(Limits.ChannelPasswordFailures);
        Fail(1, channel: OtherChannel);
        limit.ForgetSession(1);
        Assert.False(limit.IsBlocked(1, Channel, Now));
        Assert.Equal(0, limit.SessionCount);
    }

    [Fact]
    public void ForgetSession_OtherSession_Kept()
    {
        Fail(Limits.ChannelPasswordFailures, session: 2);
        limit.ForgetSession(1);
        Assert.True(limit.IsBlocked(2, Channel, Now));
    }
}
