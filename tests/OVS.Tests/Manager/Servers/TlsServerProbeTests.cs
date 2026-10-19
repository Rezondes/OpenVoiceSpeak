using System.Net;
using System.Net.Sockets;
using OVS.Manager.Servers;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Manager.Servers;

/// <summary>Package M8 (AC4): the probe's TLS handshake against a real server and a closed port.</summary>
public sealed class TlsServerProbeTests
{
    /// <summary>AC4: a running OVS server finishes the handshake.</summary>
    [Fact]
    public async Task Answers_RunningServer_True()
    {
        await using var server = await TestServer.StartAsync();
        Assert.True(await new TlsServerProbe().AnswersAsync("127.0.0.1", server.Port));
    }

    /// <summary>AC4: nothing on the port is no answer.</summary>
    [Fact]
    public async Task Answers_ClosedPort_False()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        Assert.False(await new TlsServerProbe().AnswersAsync("127.0.0.1", port));
    }
}
