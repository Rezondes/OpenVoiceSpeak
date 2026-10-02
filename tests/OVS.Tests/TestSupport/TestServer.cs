using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using OVS.Server;
using OVS.Server.Data;
using OVS.Server.Logging;
using OVS.Server.Tls;
using OVS.Server.Voice;
using OVS.Shared.Identity;

namespace OVS.Tests.TestSupport;

/// <summary>In-process server on 127.0.0.1 with ephemeral TCP and UDP ports and its own temp data dir.</summary>
public sealed class TestServer : IAsyncDisposable
{
    public required string DataDir { get; init; }
    public required ServerState State { get; init; }
    public required ControlServer Control { get; init; }
    public required UdpVoiceServer Voice { get; init; }
    public required X509Certificate2 Certificate { get; init; }
    public required ConcurrentQueue<string> Log { get; init; }
    bool deleteOnDispose = true;

    public int Port => Control.LocalEndPoint.Port;
    public IPEndPoint VoiceEndPoint => Voice.LocalEndPoint;

    public static Task<TestServer> StartAsync(
        Action<ServerData>? seed = null, string? dataDir = null, int maxUsers = 50, string password = "",
        TimeProvider? time = null, TimeSpan? idleTimeout = null, TimeSpan? handshakeTimeout = null,
        TimeSpan? writeTimeout = null, int? maxPendingHandshakes = null)
    {
        dataDir ??= Path.Combine(Path.GetTempPath(), "ovs-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDir);
        var config = new ServerConfig(0, dataDir, maxUsers, "Testserver", password);

        if (seed is not null)
        {
            var store = new DataStore(Path.Combine(dataDir, DataStore.FileName));
            var data = store.LoadOrCreate(() => ServerData.CreateDefault(config));
            seed(data);
            store.Save(data);
        }

        var log = new ConcurrentQueue<string>();
        var logs = new ServerLogs(dataDir, config.LogDays, time ?? TimeProvider.System, log.Enqueue);
        var certificate = ServerCertificate.LoadOrCreate(dataDir);
        // Same port number for TCP and UDP, like in production, so real clients find the voice socket.
        // Windows (Hyper-V, Docker) reserves chunks of the dynamic port range per protocol, so an ephemeral
        // TCP port is often blocked for UDP. Random ports below the dynamic range avoid that; retry on clashes.
        for (int attempt = 0; ; attempt++)
        {
            // A fresh state per attempt: stopping a failed attempt closes its state, which then rejects everyone.
            var state = new ServerState(config, time ?? TimeProvider.System, logs);
            var endpoint = new IPEndPoint(IPAddress.Loopback, Random.Shared.Next(20_000, 45_000));
            var control = new ControlServer(state, certificate, endpoint)
            {
                // Far above the tests' longest waits: a client that waits 30 s for a stalled answer sends nothing
                // meanwhile and must not be dropped as idle (the timeouts themselves have their own tests).
                IdleTimeout = idleTimeout ?? TimeSpan.FromSeconds(60),
                HandshakeTimeout = handshakeTimeout ?? TimeSpan.FromSeconds(30),
                WriteTimeout = writeTimeout ?? Limits.WriteTimeout,
                MaxPendingHandshakes = maxPendingHandshakes ?? Limits.MaxPendingHandshakes,
            };
            UdpVoiceServer voice;
            try
            {
                control.Start();
                voice = new UdpVoiceServer(state, endpoint);
            }
            catch (SocketException) when (attempt < 100)
            {
                control.DisposeAsync().AsTask().Wait();
                continue;
            }
            voice.Start();
            return Task.FromResult(new TestServer
            {
                DataDir = dataDir, State = state, Control = control, Voice = voice, Certificate = certificate, Log = log,
            });
        }
    }

    /// <summary>Stops this instance and starts a new one on the same data dir.</summary>
    public async Task<TestServer> RestartAsync(TimeProvider? time = null)
    {
        deleteOnDispose = false;
        await DisposeAsync();
        return await StartAsync(dataDir: DataDir, time: time);
    }

    public async ValueTask DisposeAsync()
    {
        await Control.DisposeAsync();
        Voice.Dispose();
        if (deleteOnDispose)
        {
            try
            {
                Directory.Delete(DataDir, true);
            }
            catch (IOException)
            {
            }
        }
    }

    public static Action<ServerData> Grant(ClientIdentity identity, string groupName) => data =>
    {
        var groupId = data.Groups.Single(g => g.Name == groupName).Id;
        var user = data.Users.FirstOrDefault(u => u.Fingerprint == identity.Fingerprint);
        if (user is null)
        {
            user = new UserRecord { Fingerprint = identity.Fingerprint, LastNickname = groupName };
            data.Users.Add(user);
        }
        user.GroupIds.Add(groupId);
    };
}
