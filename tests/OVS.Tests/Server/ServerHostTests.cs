using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using OVS.Server;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server;

public sealed class ServerHostTests() : TempDirectoryTest("ovs-host-", quiet: true)
{
    [Fact]
    public async Task Handshake_WhileShuttingDown_RejectedWithTheReason()
    {
        // Regression: a client connecting right after the restart notice was admitted into the run that was going away.
        await using var server = await TestServer.StartAsync();
        await using var client = await TestClient.OpenAsync(server.Port);
        server.State.CloseAll(new Disconnected(ReasonCodes.ServerRestart));
        var rejected = Assert.IsType<Rejected>(await client.HandshakeAsync("spaet"));
        Assert.Equal(ReasonCodes.ServerRestart, rejected.Code);
    }

    /// <summary>Package 4 (AC14): a start logs the address it listens on and the certificate's fingerprint.</summary>
    [Fact]
    public async Task Start_Run_LogsListeningAddressAndCertificateFingerprint()
    {
        var output = new ConcurrentQueue<string>();
        using var stop = new CancellationTokenSource();
        Task<int> host;
        int port;
        // Random ports clash with Windows' reserved ranges now and then: a run that cannot bind ends with 1, so try another.
        for (int attempt = 0; ; attempt++)
        {
            port = Random.Shared.Next(20_000, 45_000);
            var env = new Dictionary<string, string> { ["OVS_DATA_DIR"] = dir, ["OVS_PORT"] = port.ToString(CultureInfo.InvariantCulture) };
            host = ServerHost.RunAsync(env.GetValueOrDefault, IPAddress.Loopback, TimeProvider.System, output.Enqueue, output.Enqueue, stop.Token);
            var anna = await ConnectWhenUpAsync(port, "anna", host);
            if (anna is not null)
            {
                await anna.DisposeAsync();
                break;
            }
            Assert.True(attempt < 20, string.Join("\n", output));
            Assert.Equal(1, await host);
        }

        await stop.CancelAsync();
        Assert.Equal(0, await host.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Contains(output, line => line.Contains($"Listening on 127.0.0.1:{port}", StringComparison.Ordinal));
        Assert.Contains(output, line => System.Text.RegularExpressions.Regex.IsMatch(line, "Certificate fingerprint: [0-9a-f]{64}$"));
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
        // Generous: a loaded CI runner can take seconds to bring a run up; it returns as soon as one answers.
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline && !host.IsCompleted)
        {
            if (await TryConnectAsync(port, nickname) is { } client) return client;
            await Task.Delay(100);
        }
        return null;
    }

    [Fact]
    public async Task AutoRestart_RunEnds_ClientsToldAndNewRunServesWithOwnLogs()
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
                ["OVS_PORT"] = port.ToString(CultureInfo.InvariantCulture),
                ["OVS_AUTO_RESTART"] = "an",
                // Far enough ahead that anna is surely connected before it on a slow runner.
                ["OVS_AUTO_RESTART_TIME"] = DateTime.Now.AddSeconds(10).ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            };
            host = ServerHost.RunAsync(env.GetValueOrDefault, IPAddress.Loopback, TimeProvider.System, output.Enqueue, output.Enqueue, stop.Token);
            anna = await ConnectWhenUpAsync(port, "anna", host);
            if (anna is not null) break;
            Assert.True(attempt < 20, string.Join("\n", output));
            Assert.Equal(1, await host);
        }

        await using (anna)
        {
            var bye = await anna.WaitForAsync<Disconnected>(timeoutMs: 30_000);
            Assert.Equal(ReasonCodes.ServerRestart, bye.Reason);
        }

        var bert = await ConnectWhenUpAsync(port, "bert", host);
        Assert.NotNull(bert);
        await bert.DisposeAsync();

        await stop.CancelAsync();
        Assert.Equal(0, await host.WaitAsync(TimeSpan.FromSeconds(30)));

        var files = Directory.GetFiles(Path.Combine(dir, "logs", "server")).Order().Select(File.ReadAllText).ToList();
        Assert.Equal(2, files.Count);
        Assert.Contains("Automatic restart daily at", files[0], StringComparison.Ordinal);
        Assert.Contains("Server restarting, 1 users disconnected", files[0], StringComparison.Ordinal);
        Assert.Contains("Server stopped, restarting", files[0], StringComparison.Ordinal);
        Assert.Contains("OpenVoiceSpeak server starting (automatic restart)", files[1], StringComparison.Ordinal);
        Assert.Contains("bert connected", files[1], StringComparison.Ordinal);
        Assert.DoesNotContain("anna", files[1], StringComparison.Ordinal);
        Assert.Contains("Server stopped", files[1], StringComparison.Ordinal);
    }
}
