using OVS.Server;
using OVS.Server.Throttling;
using OVS.Shared.Protocol;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server.Throttling;

public sealed class SessionThrottleTests
{
    readonly ManualTimeProvider time = new();
    readonly SessionThrottle throttle;

    public SessionThrottleTests() => throttle = new SessionThrottle(time);

    /// <summary>Takes requests until one is refused and returns that answer.</summary>
    bool RefuseNextRequest()
    {
        while (true)
            if (throttle.TakeRequest() is { } answer) return answer;
    }

    /// <summary>Refuses a request every half FloodPause (no pause counts) from now until <paramref name="end"/> later.</summary>
    void RefuseRequestsFor(TimeSpan end)
    {
        var half = Limits.FloodPause / 2;
        Assert.False(RefuseNextRequest());
        for (var elapsed = half; elapsed <= end; elapsed += half)
        {
            time.Advance(half);
            Assert.False(RefuseNextRequest());
        }
    }

    [Fact]
    public void TakeRequest_WithinBurst_MayRun()
    {
        for (int i = 0; i < Limits.RequestBurst; i++) Assert.Null(throttle.TakeRequest());
    }

    [Fact]
    public void TakeRequest_PastBurst_Refused()
    {
        for (int i = 0; i < Limits.RequestBurst; i++) throttle.TakeRequest();
        Assert.False(throttle.TakeRequest());
    }

    [Fact]
    public void TakeRequest_RefusedWithoutPause_DisconnectAfterFloodTime()
    {
        RefuseRequestsFor(Limits.FloodDisconnectAfter - Limits.FloodPause / 2);
        time.Advance(Limits.FloodPause / 2);
        Assert.True(RefuseNextRequest());
    }

    [Fact]
    public void TakeRequest_GapOfFloodPause_FloodGoesOn()
    {
        RefuseRequestsFor(Limits.FloodDisconnectAfter - Limits.FloodPause);
        time.Advance(Limits.FloodPause);
        Assert.True(RefuseNextRequest());
    }

    [Fact]
    public void TakeRequest_GapLongerThanFloodPause_StartsTheFloodTimeAgain()
    {
        RefuseRequestsFor(Limits.FloodDisconnectAfter - Limits.FloodPause);
        time.Advance(Limits.FloodPause + TimeSpan.FromTicks(1));
        Assert.False(RefuseNextRequest());
    }

    [Fact]
    public void RefuseVoice_RefusedWithoutPause_DisconnectAfterFloodTime()
    {
        var step = TimeSpan.FromMilliseconds(500);
        Assert.False(throttle.RefuseVoice());
        for (var elapsed = step; elapsed < Limits.FloodDisconnectAfter; elapsed += step)
        {
            time.Advance(step);
            Assert.False(throttle.RefuseVoice());
        }
        time.Advance(step);
        Assert.True(throttle.RefuseVoice());
    }

    [Fact]
    public void RefuseVoice_RequestsRefused_CountedApart()
    {
        var step = TimeSpan.FromMilliseconds(500);
        Assert.False(throttle.RefuseVoice());
        for (var elapsed = step; elapsed < Limits.FloodDisconnectAfter; elapsed += step)
        {
            time.Advance(step);
            Assert.False(throttle.RefuseVoice());
        }
        time.Advance(step);
        Assert.False(RefuseNextRequest()); // the voice flood does not count for requests
    }

    [Fact]
    public void TakeFollowPage_AfterFirstPage_EachOffsetOnce()
    {
        throttle.StartListRound(typeof(string));

        Assert.True(throttle.TakeFollowPage(typeof(string), 200));
        Assert.False(throttle.TakeFollowPage(typeof(string), 200));
        Assert.True(throttle.TakeFollowPage(typeof(string), 400));
        Assert.False(throttle.TakeFollowPage(typeof(int), 200)); // another list has no round
    }

    [Fact]
    public void TakeFollowPage_AfterListRoundWindow_NotFree()
    {
        throttle.StartListRound(typeof(string));
        time.Advance(Limits.ListRoundWindow + TimeSpan.FromSeconds(1));

        Assert.False(throttle.TakeFollowPage(typeof(string), 200));
    }

    [Fact]
    public void TakeFollowPage_AtListRoundWindow_Free()
    {
        throttle.StartListRound(typeof(string));
        time.Advance(Limits.ListRoundWindow);

        Assert.True(throttle.TakeFollowPage(typeof(string), 200));
    }

    [Fact]
    public void TakeFollowPage_NewRound_ServesTheOffsetAgain()
    {
        throttle.StartListRound(typeof(string));
        Assert.True(throttle.TakeFollowPage(typeof(string), 200));

        throttle.StartListRound(typeof(string));
        Assert.True(throttle.TakeFollowPage(typeof(string), 200));
    }

    [Fact]
    public void TryTakeCostly_OtherKind_OwnBucket()
    {
        Assert.True(throttle.TryTakeCostly(typeof(string), 0.5, 1));
        Assert.False(throttle.TryTakeCostly(typeof(string), 0.5, 1));
        Assert.True(throttle.TryTakeCostly(typeof(int), 0.5, 1));
    }

    [Fact]
    public void TryTakeCostly_AfterRefillTime_TakesAgain()
    {
        Assert.True(throttle.TryTakeCostly(typeof(string), 0.5, 1));

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.False(throttle.TryTakeCostly(typeof(string), 0.5, 1)); // half a token at 0.5 per second
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(throttle.TryTakeCostly(typeof(string), 0.5, 1));
    }

    [Fact]
    public void TryChat_ChatBurstPerWindow_RefusedUntilTheWindowPassed()
    {
        for (int i = 0; i < ProtocolInfo.ChatBurst; i++) Assert.True(throttle.TryChat());
        Assert.False(throttle.TryChat());

        time.Advance(ProtocolInfo.ChatWindow);
        Assert.True(throttle.TryChat());
    }
}
