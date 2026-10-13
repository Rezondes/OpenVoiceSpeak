using OVS.Client.Services;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Services;

/// <summary>The first page of one server list: at most one request in flight and at most one per second, wishes coalesced.</summary>
public sealed class ListRefreshTests
{
    readonly ManualTimeProvider clock = new();
    readonly List<string> sent = [];
    bool allowed = true;
    readonly ListRefresh refresh;

    public ListRefreshTests() => refresh = new ListRefresh("u", () => allowed, id =>
    {
        lock (sent) sent.Add(id);
        return Task.CompletedTask;
    }, clock);

    int SentCount
    {
        get { lock (sent) return sent.Count; }
    }

    [Fact]
    public async Task RequestAsync_Allowed_SendsAtOnceUnderTheNextId()
    {
        await refresh.RequestAsync();

        Assert.Equal(["u1"], sent);
        Assert.Equal(1, refresh.Sent);
    }

    [Fact]
    public async Task RequestAsync_NotAllowed_SendsNothing()
    {
        allowed = false;

        await refresh.RequestAsync();

        Assert.Empty(sent);
    }

    [Fact]
    public async Task RequestAsync_WhileOneIsInFlight_CoalescesTheWishesIntoOneAfterTheAnswer()
    {
        await refresh.RequestAsync();
        await refresh.RequestAsync();
        await refresh.RequestAsync();
        Assert.Equal(1, SentCount);

        clock.Advance(TimeSpan.FromSeconds(1));
        refresh.Completed();

        Assert.Equal(["u1", "u2"], sent);
    }

    [Fact]
    public async Task RequestAsync_WithinASecondOfTheLast_WaitsForTheInterval()
    {
        await refresh.RequestAsync();
        refresh.Completed();

        await refresh.RequestAsync();
        clock.Advance(TimeSpan.FromMilliseconds(999));
        Assert.Equal(1, SentCount);
        clock.Advance(TimeSpan.FromMilliseconds(1));

        await AsyncWait.Until(() => SentCount == 2); // the delayed send continues on the pool
        Assert.Equal(["u1", "u2"], sent);
    }

    [Fact]
    public async Task RequestAsync_NoLongerAllowedWhenTheWaitEnds_SendsNothing()
    {
        await refresh.RequestAsync();
        refresh.Completed();
        await refresh.RequestAsync();

        allowed = false;
        clock.Advance(TimeSpan.FromSeconds(1));

        await AsyncWait.AssertStaysFalseAsync(() => SentCount > 1, TimeSpan.FromMilliseconds(50), "the page closed meanwhile");
    }

    [Fact]
    public async Task Failed_AnotherRequestId_KeepsWaitingForTheOneInFlight()
    {
        await refresh.RequestAsync();
        await refresh.RequestAsync();
        clock.Advance(TimeSpan.FromSeconds(1));

        refresh.Failed("u7");
        refresh.Failed(null);
        Assert.Equal(1, SentCount);

        refresh.Failed("u1");
        Assert.Equal(["u1", "u2"], sent);
    }

    [Theory]
    [InlineData("u3", 3)]
    [InlineData("b3", 0)]
    [InlineData("u", 0)]
    [InlineData("uX", 0)]
    [InlineData(null, 0)]
    public void RoundOf_RequestId_TheNumberOnlyForTheOwnPrefix(string? requestId, int round)
    {
        Assert.Equal(round, refresh.RoundOf(requestId));
    }
}
