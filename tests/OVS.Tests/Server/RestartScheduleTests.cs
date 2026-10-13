using System.Collections.Concurrent;
using OVS.Server;
using OVS.Server.Logging;
using OVS.Shared.Identity;
using OVS.Shared.Protocol.Messages.ServerInfo;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server;

public sealed class RestartScheduleTests
{
    static readonly TimeZoneInfo Plus2 = TimeZoneInfo.CreateCustomTimeZone("Test+2", TimeSpan.FromHours(2), "Test+2", "Test+2");

    static DateTimeOffset Local(int hour, int minute, int second = 0, int day = 27) =>
        new(2026, 9, day, hour, minute, second, TimeSpan.FromHours(2));

    [Theory]
    [InlineData(3, 0, 0, 27)]   // before the time: today
    [InlineData(4, 30, 0, 28)]  // after the time: tomorrow
    [InlineData(4, 0, 0, 28)]   // exactly now: tomorrow
    [InlineData(3, 59, 59, 28)] // less than a second before: tomorrow, a timer firing early must not restart twice
    public void FindNext_TodayOrTomorrow_InLocalTime(int hour, int minute, int second, int expectedDay)
    {
        var next = RestartSchedule.FindNext(Local(hour, minute, second).ToUniversalTime(), new TimeOnly(4, 0, 0), Plus2);
        Assert.Equal(Local(4, 0, 0, expectedDay), next);
    }

    /// <summary>Package 69: switching the restart on, off or to another time in the administration takes effect at once.</summary>
    [Fact]
    public async Task Schedule_AutoRestartChanged_Follows()
    {
        var time = new ManualTimeProvider();
        var admin = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(TestServer.Grant(admin, "Admin"), time: time);
        await using var client = await TestClient.ConnectAsync(server, identity: admin);
        var lines = new ConcurrentQueue<string>();
        var logs = new ServerLogWriter(server.DataDir, 0, time, lines.Enqueue);
        using var stop = new CancellationTokenSource();
        var restart = RestartSchedule.WaitAsync(server.State, time, logs, stop.Token);

        // The loop picks up a change on the thread pool: wait until it logged the new schedule, so the
        // time.Advance after it hits the new timer instead of racing the loop on a slow runner.
        async Task LoggedAsync(string text, int count)
        {
            await AsyncWait.UntilAsync(() => lines.Count(line => line.Contains(text)) >= count);
            Assert.Equal(count, lines.Count(line => line.Contains(text)));
        }
        const string Scheduled = "Automatic restart daily at";

        async Task SetAsync(bool on, TimeSpan fromNow)
        {
            var at = TimeOnly.FromDateTime(time.GetLocalNow().DateTime + fromNow);
            await client.SendAsync(new UpdateServerSettings("S", "", null, new ServerLimits(50, 30, true, on, at)));
            await client.WaitForAsync<ServerSettingsChanged>();
        }

        await SetAsync(true, TimeSpan.FromMinutes(10));
        await LoggedAsync(Scheduled, 1);
        await SetAsync(false, TimeSpan.FromMinutes(10)); // switched off before the time
        await LoggedAsync("Automatic restart switched off", 1);
        time.Advance(TimeSpan.FromMinutes(11));
        await AsyncWait.AssertStaysFalseAsync(() => restart.IsCompleted, TimeSpan.FromMilliseconds(200), "switched off: no restart");
        Assert.False(restart.IsCompleted);

        await SetAsync(true, TimeSpan.FromMinutes(10));
        await LoggedAsync(Scheduled, 2);
        time.Advance(TimeSpan.FromMinutes(9));
        await AsyncWait.AssertStaysFalseAsync(() => restart.IsCompleted, TimeSpan.FromMilliseconds(200), "not before its time");
        Assert.False(restart.IsCompleted);
        time.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(2));
        Assert.True(await restart.WaitAsync(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task WaitAsync_Stopped_ReturnsFalse()
    {
        var time = new ManualTimeProvider();
        await using var server = await TestServer.StartAsync(time: time);
        using var stop = new CancellationTokenSource();
        var restart = RestartSchedule.WaitAsync(server.State, time, new ServerLogWriter(server.DataDir, 0, time, _ => { }), stop.Token);
        await stop.CancelAsync();
        Assert.False(await restart.WaitAsync(TimeSpan.FromSeconds(30)));
    }
}
