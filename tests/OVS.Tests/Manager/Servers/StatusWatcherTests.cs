using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OVS.Manager.Accounts;
using OVS.Manager.Components.Pages.Servers;
using OVS.Manager.Data;
using OVS.Manager.Docker;
using OVS.Manager.Servers;
using OVS.Tests.Manager.Accounts;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Manager.Servers;

/// <summary>Package M8 (AC1, AC3 to AC5): the status of every server from Docker's list and inspect and the TLS handshake.</summary>
public sealed class StatusWatcherTests
{
    internal static ManagerFactory WithAdmin() => new((FirstAdmin.UserVariable, "chef"), (FirstAdmin.PasswordVariable, FirstAdminTests.Password));

    /// <summary>Creates and starts a server through the factory, as the admin's form does.</summary>
    internal static async Task<ManagedServer> CreateAsync(ManagerFactory manager, string name)
    {
        await using var scope = manager.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ServerFactory>().CreateAsync(new CreateServerRequest(name, 20, null, null, null, null), "chef");
    }

    /// <summary>A watcher of its own, not started, on <paramref name="time"/>.</summary>
    static StatusWatcher Watcher(ManagerFactory manager, TimeProvider time) =>
        new(manager.Docker, manager.Probe, manager.Services.GetRequiredService<IServiceScopeFactory>(), time);

    static ServerSnapshot Find(StatusWatcher watcher, ManagedServer server) => watcher.Servers.Single(snapshot => snapshot.Id == server.Id);

    /// <summary>AC1: Docker's states and the handshake become the six statuses, with the exit code of a crash.</summary>
    [Fact]
    public async Task Refresh_DockerStates_StatusMapped()
    {
        await using var manager = WithAdmin();
        manager.CreateClient().Dispose();
        var time = new ManualTimeProvider();
        var now = time.GetUtcNow();
        var cases = new (string Name, InspectedContainer Inspected, bool Answers, ServerStatus Status, int? ExitCode)[]
        {
            ("A", new("running", 0, 0, now.AddHours(-1)), true, ServerStatus.Running, null),
            ("B", new("running", 0, 0, now.AddSeconds(-5)), false, ServerStatus.Starting, null),
            ("C", new("exited", 0, 0, now.AddHours(-1)), false, ServerStatus.Stopped, null),
            ("D", new("exited", 137, 0, now.AddHours(-1)), false, ServerStatus.Crashed, 137),
            ("E", new("restarting", 1, 0, now.AddHours(-1)), false, ServerStatus.Starting, null),
            ("F", new("created", 0, 0, null), false, ServerStatus.Stopped, null),
        };
        var servers = new List<ManagedServer>();
        foreach (var (name, inspected, answers, _, _) in cases)
        {
            var server = await CreateAsync(manager, name);
            manager.Docker.Inspected[server.ContainerId!] = inspected;
            if (!answers) manager.Probe.Silent[ManagedLabels.ContainerName(server.Id)] = 0;
            servers.Add(server);
        }

        using var watcher = Watcher(manager, time);
        await watcher.RefreshAsync();

        Assert.Equal(cases.Select(row => (row.Name, row.Status, row.ExitCode)),
            servers.Select(server => Find(watcher, server)).Select(snapshot => (snapshot.Name, snapshot.Status, snapshot.ExitCode)));
        Assert.Equal(now.AddHours(-1), Find(watcher, servers[0]).StartedAtUtc);
        Assert.Null(Find(watcher, servers[2]).StartedAtUtc);
    }

    /// <summary>AC1: the overview shows the new status without a reload.</summary>
    [Fact]
    public async Task List_StatusChange_RedrawnLive()
    {
        await using var manager = WithAdmin();
        manager.CreateClient().Dispose();
        var server = await CreateAsync(manager, "Raid");
        var watcher = manager.Services.GetRequiredService<StatusWatcher>();
        await watcher.RefreshAsync();
        await using var context = ServerPages.Context(manager);

        var page = context.Render<ServerList>();
        var status = $"tr[data-server='{server.Id}'] td[data-status]";
        Assert.Equal(("Running", "läuft"), (page.Find(status).GetAttribute("data-status"), page.Find(status).TextContent));

        manager.Docker.Inspected[server.ContainerId!] = new InspectedContainer("exited", 1, 0, null);
        await watcher.RefreshAsync();
        page.WaitForAssertion(() => Assert.Equal("abgestürzt (Exit-Code 1)", page.Find(status).TextContent));
    }

    /// <summary>AC3: a fourth restart within ten minutes is a restart loop with the last exit code; it ends when they age out.</summary>
    [Fact]
    public async Task Refresh_FourRestartsInTenMinutes_RestartLoop()
    {
        await using var manager = WithAdmin();
        manager.CreateClient().Dispose();
        var server = await CreateAsync(manager, "Raid");
        var time = new ManualTimeProvider();
        using var watcher = Watcher(manager, time);
        async Task<ServerSnapshot> AfterRestartsAsync(int restarts)
        {
            manager.Docker.Inspected[server.ContainerId!] = new InspectedContainer("restarting", 3, restarts, time.GetUtcNow());
            await watcher.RefreshAsync();
            return Find(watcher, server);
        }

        await AfterRestartsAsync(5); // restarts before the manager started do not count
        for (int restart = 6; restart <= 8; restart++)
        {
            time.Advance(TimeSpan.FromMinutes(2));
            Assert.Equal(ServerStatus.Starting, (await AfterRestartsAsync(restart)).Status);
        }
        time.Advance(TimeSpan.FromMinutes(2));
        var loop = await AfterRestartsAsync(9);
        Assert.Equal((ServerStatus.RestartLoop, (int?)3), (loop.Status, loop.ExitCode));

        time.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(ServerStatus.Starting, (await AfterRestartsAsync(9)).Status);
    }

    /// <summary>AC4: a running container without a handshake is "not reachable" once its start is 30 seconds past.</summary>
    [Fact]
    public async Task Refresh_RunningNoHandshake_NotReachable()
    {
        await using var manager = WithAdmin();
        manager.CreateClient().Dispose();
        var server = await CreateAsync(manager, "Raid");
        var time = new ManualTimeProvider();
        using var watcher = Watcher(manager, time);
        manager.Docker.Inspected[server.ContainerId!] = new InspectedContainer("running", 0, 0, time.GetUtcNow());
        manager.Probe.Silent[ManagedLabels.ContainerName(server.Id)] = 0;

        await watcher.RefreshAsync();
        Assert.Equal(ServerStatus.Starting, Find(watcher, server).Status);
        time.Advance(StatusWatcher.StartGrace + TimeSpan.FromSeconds(1));
        await watcher.RefreshAsync();
        Assert.Equal(ServerStatus.NotReachable, Find(watcher, server).Status);

        manager.Probe.Silent.Clear();
        await watcher.RefreshAsync();
        Assert.Equal(ServerStatus.Running, Find(watcher, server).Status);
    }

    /// <summary>AC5: after the manager's restart a new watcher finds the containers by their labels, even with another Docker id.</summary>
    [Fact]
    public async Task Refresh_NewWatcher_ServersFoundByLabels()
    {
        await using var manager = WithAdmin();
        manager.CreateClient().Dispose();
        var server = await CreateAsync(manager, "Raid");
        Assert.True(manager.Docker.Containers.TryRemove(server.ContainerId!, out var container));
        manager.Docker.Containers["recreated"] = container with { Id = "recreated", State = "exited" };

        using var watcher = Watcher(manager, new ManualTimeProvider());
        await watcher.RefreshAsync();
        Assert.Equal(ServerStatus.Stopped, Find(watcher, server).Status);

        await using var scope = manager.Services.CreateAsyncScope();
        Assert.Equal(server.ContainerId, (await scope.ServiceProvider.GetRequiredService<ManagerDbContext>().Servers.SingleAsync()).ContainerId);
        Assert.True(await scope.ServiceProvider.GetRequiredService<ServerLifecycle>().StartAsync(server.Id, "chef"));
        Assert.Equal("running", manager.Docker.Containers["recreated"].State);
    }
}
