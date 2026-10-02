using System.Diagnostics;
using OVS.Shared.Protocol;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server;

public class ShutdownTests
{
    [Fact]
    public async Task StopAsync_ConnectedClients_ReceiveShutdownAndAreClosed()
    {
        await using var server = await TestServer.StartAsync();
        await using var a = await TestClient.ConnectAsync(server);
        await using var b = await TestClient.ConnectAsync(server);

        // Timed alone: StopAsync gives up waiting after 2 s, so finishing sooner proves the connections
        // closed by themselves. The clients' own reading below is not part of it.
        var watch = Stopwatch.StartNew();
        await server.Control.StopAsync();
        var stopped = watch.Elapsed;

        foreach (var client in new[] { a, b })
        {
            Assert.Equal(Codes.ServerShutdown, (await client.WaitForAsync<Disconnected>()).Reason);
            Assert.True(await client.WaitClosedAsync());
        }
        Assert.True(stopped < TimeSpan.FromSeconds(2), $"took {stopped}");
    }
}
