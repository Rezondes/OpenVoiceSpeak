using Bunit;
using Microsoft.Extensions.DependencyInjection;
using OVS.Manager.Audit;
using OVS.Manager.Components.Pages.Servers;
using OVS.Manager.Exceptions;
using OVS.Manager.Servers;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Manager.Servers;

/// <summary>Package M8 (AC2): start, stop and restart from the overview, with audit lines and the buttons off while one runs.</summary>
public sealed class ServerLifecycleTests
{
    /// <summary>AC2: each action reaches Docker and writes its line; a running one turns the server's buttons off and refuses a second.</summary>
    [Fact]
    public async Task StartStopRestart_Overview_AuditAndButtonsDisabled()
    {
        await using var manager = StatusWatcherTests.WithAdmin();
        manager.CreateClient().Dispose();
        var server = await StatusWatcherTests.CreateAsync(manager, "Raid");
        var watcher = manager.Services.GetRequiredService<StatusWatcher>();
        await watcher.RefreshAsync();
        await using var context = ServerPages.Context(manager);
        var page = context.Render<ServerList>();
        var buttons = $"tr[data-server='{server.Id}'] button";

        manager.Docker.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopping = page.FindAll(buttons)[1].ClickAsync(new()); // stop, held by Docker
        page.WaitForAssertion(() => Assert.All(page.FindAll(buttons), button => Assert.True(button.HasAttribute("disabled"))));
        await using (var scope = manager.Services.CreateAsyncScope())
            Assert.False(await scope.ServiceProvider.GetRequiredService<ServerLifecycle>().StartAsync(server.Id, "chef"));
        manager.Docker.Hold.SetResult();
        manager.Docker.Hold = null;
        await stopping;
        page.WaitForAssertion(() => Assert.All(page.FindAll(buttons), button => Assert.False(button.HasAttribute("disabled"))));
        Assert.Equal("exited", manager.Docker.Containers[server.ContainerId!].State);

        await page.FindAll(buttons)[0].ClickAsync(new()); // start
        await page.FindAll(buttons)[2].ClickAsync(new()); // restart
        Assert.Equal("running", manager.Docker.Containers[server.ContainerId!].State);

        await using var audit = manager.Services.CreateAsyncScope();
        var lines = await audit.ServiceProvider.GetRequiredService<AuditLog>().QueryAsync(new AuditFilter(ServerId: server.Id), servers: null);
        Assert.Equal([AuditActions.ServerCreated, AuditActions.ServerStopped, AuditActions.ServerStarted, AuditActions.ServerRestarted],
            lines.OrderBy(line => line.Id).Select(line => line.Action));
        Assert.All(lines, line => Assert.Equal(("chef", "Raid"), (line.UserName, line.Target)));
    }

    /// <summary>AC2: an action on a server that is gone is refused with its problem.</summary>
    [Fact]
    public async Task Start_UnknownServer_NotFound()
    {
        await using var manager = StatusWatcherTests.WithAdmin();
        manager.CreateClient().Dispose();
        await using var scope = manager.Services.CreateAsyncScope();
        var refused = await Assert.ThrowsAsync<ServerRequestException>(() => scope.ServiceProvider.GetRequiredService<ServerLifecycle>().StartAsync("gone", "chef"));
        Assert.Equal(ServerProblem.NotFound, refused.Problem);
        Assert.False(manager.Services.GetRequiredService<StatusWatcher>().IsBusy("gone"));
    }
}
