using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using OVS.Server;
using OVS.Server.Tls;

namespace OVS.Tests.TestSupport;

/// <summary>In-process server on 127.0.0.1 with an ephemeral port and its own temp data dir.</summary>
public sealed class TestServer : IAsyncDisposable
{
    public required string DataDir { get; init; }
    public required ServerState State { get; init; }
    public required ControlServer Control { get; init; }
    public required X509Certificate2 Certificate { get; init; }
    public required ConcurrentQueue<string> Log { get; init; }
    bool deleteOnDispose = true;

    public int Port => Control.LocalEndPoint.Port;

    public static Task<TestServer> StartAsync(
        string? dataDir = null, int maxUsers = 50, string password = "",
        TimeProvider? time = null, TimeSpan? idleTimeout = null, TimeSpan? handshakeTimeout = null)
    {
        dataDir ??= Path.Combine(Path.GetTempPath(), "ovs-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDir);
        var config = new ServerConfig(0, dataDir, maxUsers, "Testserver", password);

        var log = new ConcurrentQueue<string>();
        var state = new ServerState(config, time ?? TimeProvider.System, log.Enqueue);
        var certificate = ServerCertificate.LoadOrCreate(dataDir);
        var control = new ControlServer(state, certificate, new IPEndPoint(IPAddress.Loopback, 0))
        {
            IdleTimeout = idleTimeout ?? TimeSpan.FromSeconds(15),
            HandshakeTimeout = handshakeTimeout ?? TimeSpan.FromSeconds(10),
        };
        control.Start();
        return Task.FromResult(new TestServer
        {
            DataDir = dataDir, State = state, Control = control, Certificate = certificate, Log = log,
        });
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

}
