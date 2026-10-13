using System.Diagnostics;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server;

public class ShutdownTests
{
    [Fact]
    public async Task StopAsync_ConnectedClients_ReceiveShutdownAndAreClosed()
    {
        await using var server = await TestServer.StartAsync();
        await using var testClient = await TestClient.ConnectAsync(server);
        await using var otherClient = await TestClient.ConnectAsync(server);

        // Timed alone: StopAsync gives up waiting after 2 s, so finishing sooner proves the connections
        // closed by themselves. The clients' own reading below is not part of it.
        var watch = Stopwatch.StartNew();
        await server.Control.StopAsync();
        var stopped = watch.Elapsed;

        foreach (var client in new[] { testClient, otherClient })
        {
            Assert.Equal(ReasonCodes.ServerShutdown, (await client.WaitForAsync<Disconnected>()).Reason);
            Assert.True(await client.WaitClosedAsync());
        }
        Assert.True(stopped < TimeSpan.FromSeconds(2), $"took {stopped}");
    }
}
