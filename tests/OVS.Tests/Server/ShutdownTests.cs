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

        var watch = Stopwatch.StartNew();
        await server.Control.StopAsync();

        foreach (var client in new[] { a, b })
        {
            Assert.Equal(Codes.ServerShutdown, (await client.WaitForAsync<Disconnected>()).Reason);
            Assert.True(await client.WaitClosedAsync());
        }
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"took {watch.Elapsed}");
    }
}
