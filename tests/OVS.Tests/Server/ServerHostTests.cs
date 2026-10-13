using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using OVS.Server;
using OVS.Shared;
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

    /// <summary>Starts a run on a free port with <paramref name="configureLogging"/>; a reserved port ends with 1, then another is tried.</summary>
    async Task<StartedRun> StartAsync(Action<ILoggingBuilder> configureLogging, ConcurrentQueue<string> output,
        Dictionary<string, string>? extra, CancellationToken stop)
    {
        for (int attempt = 0; ; attempt++)
        {
            int port = Random.Shared.Next(20_000, 45_000);
            var env = new Dictionary<string, string> { ["OVS_DATA_DIR"] = dir, ["OVS_PORT"] = port.ToString(CultureInfo.InvariantCulture) };
            foreach (var (key, value) in extra ?? []) env[key] = value;
            var host = ServerHost.RunAsync(env.GetValueOrDefault, IPAddress.Loopback, TimeProvider.System, output.Enqueue, output.Enqueue, stop, configureLogging: configureLogging);
            if (await ConnectWhenUpAsync(port, "anna", host) is { } client) return new(host, port, client);
            Assert.True(attempt < 20, string.Join("\n", output));
            Assert.Equal(1, await host);
        }
    }

    /// <summary>Package 144 (A154): a provider added to the run's logger factory gets the server and the channel lines.</summary>
    [Fact]
    public async Task Run_ExtraProvider_GetsServerAndChannelLines()
    {
        #pragma warning disable CA2000 // the run's logger factory owns and disposes it
        var recorder = new RecordingLoggerProvider();
        #pragma warning restore CA2000
        var output = new ConcurrentQueue<string>();
        using var stop = new CancellationTokenSource();
        var (host, _, client) = await StartAsync(recorder.AddTo, output, null, stop.Token);
        await client.DisposeAsync();
        await stop.CancelAsync();
        Assert.Equal(0, await host.WaitAsync(TimeSpan.FromSeconds(30)));

        var server = recorder.Lines.Where(line => line.Category == "server").Select(line => line.Text).ToList();
        Assert.Contains(server, text => text.Contains("OpenVoiceSpeak server starting", StringComparison.Ordinal));
        Assert.Contains(server, text => text.Contains(BuildInfo.Current.Version, StringComparison.Ordinal));
        Assert.Contains(recorder.Lines, line => line.Category.StartsWith("channels/", StringComparison.Ordinal) && line.Text.Contains("anna joined the channel", StringComparison.Ordinal));
        Assert.Equal(1, recorder.Disposed);
    }

    /// <summary>Package 144 (A154): each run has its own logger factory; the old one is disposed once when its run ends.</summary>
    [Fact]
    public async Task Restart_NewFactory_OldDisposedOnce()
    {
        var recorders = new ConcurrentQueue<RecordingLoggerProvider>();
        var output = new ConcurrentQueue<string>();
        using var stop = new CancellationTokenSource();
        var restartAt = TimeProvider.System.GetLocalNow().AddSeconds(5).ToString("HH:mm:ss", CultureInfo.InvariantCulture); // the run's own clock
        var (host, port, client) = await StartAsync(builder =>
        {
            #pragma warning disable CA2000 // the run's logger factory owns and disposes it
            #pragma warning disable CA2000 // the run's logger factory owns and disposes it
        var recorder = new RecordingLoggerProvider();
        #pragma warning restore CA2000
            #pragma warning restore CA2000
            recorders.Enqueue(recorder);
            recorder.AddTo(builder);
        }, output, new() { ["OVS_AUTO_RESTART"] = "an", ["OVS_AUTO_RESTART_TIME"] = restartAt }, stop.Token);
        await using (client)
        {
            await client.WaitForAsync<Disconnected>(timeoutMs: 30_000);
        }
        var bert = await ConnectWhenUpAsync(port, "bert", host);
        Assert.NotNull(bert);
        await bert.DisposeAsync();

        var (first, second) = (recorders.ElementAt(0), recorders.ElementAt(1));
        Assert.Equal((1, 0), (first.Disposed, second.Disposed));
        Assert.DoesNotContain(first.Lines, line => line.Text.Contains("bert", StringComparison.Ordinal));
        Assert.Contains(second.Lines, line => line.Text.Contains("bert connected", StringComparison.Ordinal));

        await stop.CancelAsync();
        Assert.Equal(0, await host.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Equal((1, 1, 2), (first.Disposed, second.Disposed, recorders.Count));
    }

    /// <summary>A run started by <see cref="StartAsync"/>: its task, its port and the client that found it up.</summary>
    sealed record StartedRun(Task<int> Host, int Port, TestClient Client);
}
