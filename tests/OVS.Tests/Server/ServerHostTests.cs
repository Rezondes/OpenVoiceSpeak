using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using OVS.Server;
using OVS.Shared.Protocol;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server;

public sealed class ServerHostTests : IDisposable
{
    static readonly TimeZoneInfo Plus2 = TimeZoneInfo.CreateCustomTimeZone("Test+2", TimeSpan.FromHours(2), "Test+2", "Test+2");
    readonly string dir = Directory.CreateTempSubdirectory("ovs-host-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(dir, true);
        }
        catch (IOException)
        {
        }
    }

    static DateTimeOffset Local(int hour, int minute, int second = 0, int day = 27) =>
        new(2026, 9, day, hour, minute, second, TimeSpan.FromHours(2));

    [Theory]
    [InlineData(3, 0, 0, 27)]   // before the time: today
    [InlineData(4, 30, 0, 28)]  // after the time: tomorrow
    [InlineData(4, 0, 0, 28)]   // exactly now: tomorrow
    [InlineData(3, 59, 59, 28)] // less than a second before: tomorrow, a timer firing early must not restart twice
    public void NextRestart_TodayOrTomorrow_InLocalTime(int hour, int minute, int second, int expectedDay)
    {
        var next = ServerHost.NextRestart(Local(hour, minute, second).ToUniversalTime(), new TimeOnly(4, 0, 0), Plus2);
        Assert.Equal(Local(4, 0, 0, expectedDay), next);
    }

    [Fact]
    public async Task ShuttingDown_HandshakeRejectedWithTheReason()
    {
        // Regression: a client connecting right after the restart notice was admitted into the run that was going away.
        await using var server = await TestServer.StartAsync();
        await using var client = await TestClient.OpenAsync(server.Port);
        server.State.CloseAll(new Disconnected(Codes.ServerRestart));
        var rejected = Assert.IsType<Rejected>(await client.HandshakeAsync("spaet"));
        Assert.Equal(Codes.ServerRestart, rejected.Code);
    }

    static async Task<TestClient?> TryConnectAsync(int port, string nickname)
    {
        try
        {
            var client = await TestClient.OpenAsync(port);
            Assert.IsType<Welcome>(await client.HandshakeAsync(nickname));
            return client;
        }
        catch (SocketException)
        {
            return null;
        }
    }

    static async Task<TestClient?> ConnectWhenUpAsync(int port, string nickname, Task host)
    {
        for (int i = 0; i < 50 && !host.IsCompleted; i++)
        {
            if (await TryConnectAsync(port, nickname) is { } client) return client;
            await Task.Delay(100);
        }
        return null;
    }

    [Fact]
    public async Task AutoRestart_TellsClients_NewRunServes_WithOwnLogFiles()
    {
        var output = new ConcurrentQueue<string>();
        using var stop = new CancellationTokenSource();
        Task<int> host;
        TestClient? anna;
        int port;
        // Random ports clash with Windows' reserved ranges now and then: a run that cannot bind ends with 1, so try another.
        for (int attempt = 0; ; attempt++)
        {
            port = Random.Shared.Next(20_000, 45_000);
            var env = new Dictionary<string, string>
            {
                ["OVS_DATA_DIR"] = dir,
                ["OVS_PORT"] = port.ToString(),
                ["OVS_AUTO_RESTART"] = "an",
                ["OVS_AUTO_RESTART_TIME"] = DateTime.Now.AddSeconds(5).ToString("HH:mm:ss"),
            };
            host = ServerHost.RunAsync(env.GetValueOrDefault, IPAddress.Loopback, TimeProvider.System, output.Enqueue, output.Enqueue, stop.Token);
            anna = await ConnectWhenUpAsync(port, "anna", host);
            if (anna is not null) break;
            Assert.True(attempt < 20, string.Join("\n", output));
            Assert.Equal(1, await host);
        }

        await using (anna)
        {
            var bye = await anna.WaitForAsync<Disconnected>(timeoutMs: 10_000);
            Assert.Equal(Codes.ServerRestart, bye.Reason);
        }

        var bert = await ConnectWhenUpAsync(port, "bert", host);
        Assert.NotNull(bert);
        await bert.DisposeAsync();

        stop.Cancel();
        Assert.Equal(0, await host.WaitAsync(TimeSpan.FromSeconds(10)));

        var files = Directory.GetFiles(Path.Combine(dir, "logs", "server")).Order().Select(File.ReadAllText).ToList();
        Assert.Equal(2, files.Count);
        Assert.Contains("Automatischer Neustart täglich um", files[0]);
        Assert.Contains("Server startet neu, 1 Nutzer getrennt", files[0]);
        Assert.Contains("Server beendet, startet neu", files[0]);
        Assert.Contains("OpenVoiceSpeak-Server startet (automatischer Neustart)", files[1]);
        Assert.Contains("bert verbunden", files[1]);
        Assert.DoesNotContain("anna", files[1]);
        Assert.Contains("Server beendet", files[1]);
    }
}
