using OVS.Client.Localization;
using OVS.Client.ViewModels;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client;

/// <summary>Package 97 (A113): the shared waiting mark for every action that waits for the server.</summary>
public class PendingTests
{
    static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void BusyVisibleWithin150ms_NotForFastActions()
    {
        var time = new ManualTimeProvider();
        var pending = new Pending(time);

        // answered after 100 ms: running at once, but the busy UI never shows
        pending.Start("r1");
        Assert.True(pending.IsRunning);
        Assert.True(pending.Answers("r1"));
        time.Advance(Ms(100));
        Assert.False(pending.IsBusy);
        pending.Done();
        Assert.False(pending.IsRunning);
        time.Advance(TimeSpan.FromSeconds(11));
        Assert.False(pending.IsBusy);
        Assert.Null(pending.Error);

        // answered after 1 s: busy from 150 ms on until the answer
        var seen = new List<string?>();
        pending.PropertyChanged += (_, e) => seen.Add(e.PropertyName);
        pending.Start("r2");
        Assert.False(pending.Answers("r1"));
        time.Advance(Ms(149));
        Assert.False(pending.IsBusy);
        time.Advance(Ms(1));
        Assert.True(pending.IsBusy);
        Assert.Contains(nameof(Pending.IsBusy), seen);
        time.Advance(Ms(850));
        Assert.True(pending.IsBusy);
        pending.Done();
        Assert.False(pending.IsBusy);
        Assert.False(pending.IsRunning);
    }

    [Fact]
    public void NoAnswer_TimesOutWithError_Retryable()
    {
        var time = new ManualTimeProvider();
        var pending = new Pending(time);
        int timeouts = 0;
        pending.TimedOut += () => timeouts++;

        pending.Start("r1");
        time.Advance(TimeSpan.FromSeconds(9.9));
        Assert.True(pending.IsBusy);
        time.Advance(Ms(100));
        Assert.False(pending.IsRunning);
        Assert.False(pending.IsBusy);
        Assert.Equal(Strings.Pending_NoAnswer, pending.Error);
        Assert.Equal(1, timeouts);


        // retried: the error goes, the new try gets its own 10 s
        pending.Start("r2");
        Assert.Null(pending.Error);
        time.Advance(TimeSpan.FromSeconds(5));
        pending.Fail("abgelehnt");
        Assert.Equal("abgelehnt", pending.Error);
        time.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal("abgelehnt", pending.Error);
        Assert.Equal(1, timeouts);

        // a late answer after the timeout: it happened after all, the error goes
        pending.Start("r3");
        time.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(Strings.Pending_NoAnswer, pending.Error);
        pending.Done();
        Assert.Null(pending.Error);
        Assert.False(pending.IsRunning);
    }

    /// <summary>The server's error for the request ends the matching mark with the error's text.</summary>
    [Fact]
    public async Task ServerError_ForTheRequest_EndsIt()
    {
        var server = FakeServers.Admin(time: new ManualTimeProvider());
        var pending = server.NewPending();
        var id = await server.SendAsync(new OVS.Shared.Protocol.CreateBackup(), pending: pending);
        Assert.True(pending.Answers(id));
        server.Apply(new OVS.Shared.Protocol.Error("anderes", OVS.Shared.Protocol.Codes.PermissionDenied));
        Assert.True(pending.IsRunning);
        server.Apply(new OVS.Shared.Protocol.Error(id, OVS.Shared.Protocol.Codes.PermissionDenied));
        Assert.False(pending.IsRunning);
        Assert.Equal(OVS.Client.ErrorTexts.For(OVS.Shared.Protocol.Codes.PermissionDenied), pending.Error);
    }
}
