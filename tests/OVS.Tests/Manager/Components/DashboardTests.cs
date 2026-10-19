using Bunit;
using Microsoft.Extensions.DependencyInjection;
using OVS.Manager.Accounts;
using OVS.Manager.Components.Pages.Servers;
using OVS.Manager.Servers;
using OVS.Manager.Telemetry;
using OVS.Shared.Protocol;
using OVS.Tests.Manager.Servers;
using OVS.Tests.TestSupport;
using static OVS.Tests.Manager.Telemetry.TelemetryTesting;

namespace OVS.Tests.Manager.Components;

/// <summary>Package M23: the dashboard on top of the overview.</summary>
public sealed class DashboardTests
{
    /// <summary>A manager whose GitHub offers 201026.zz, with servers "Raid" (3 users, 100 kbit/s in) and "Lobby" (4 users, 10 kbit/s out).</summary>
    static async Task<ManagerFactory> TwoServersAsync()
    {
        // a fixed clock in the middle of a minute: with the system's, a minute that ends between the setup and the render moves "the last minute" away
        var wall = DateTimeOffset.UtcNow;
        var now = new DateTimeOffset(wall.Year, wall.Month, wall.Day, wall.Hour, wall.Minute, 30, TimeSpan.Zero);
        var manager = new ManagerFactory((FirstAdmin.UserVariable, "chef"), (FirstAdmin.PasswordVariable, OVS.Tests.Manager.Accounts.FirstAdminTests.Password))
        {
            Time = new ManualTimeProvider(now),
        };
        manager.GitHub.Respond = _ => FakeHttp.Json(new[]
        {
            new Dictionary<string, object> { ["tag_name"] = "deploy-201026z", ["name"] = "OpenVoiceSpeak 201026.zz", ["published_at"] = DateTimeOffset.UtcNow, ["draft"] = false, ["body"] = $"Protokoll: {WireProtocol.Version}" },
        });
        manager.CreateClient().Dispose();
        var raid = await StatusWatcherTests.CreateAsync(manager, "Raid");
        var lobby = await StatusWatcherTests.CreateAsync(manager, "Lobby");
        await manager.Services.GetRequiredService<ReleaseCatalog>().RefreshAsync();
        await manager.Services.GetRequiredService<StatusWatcher>().RefreshAsync();
        var lastMinute = Ms(now) - Ms(now) % 60_000 - 60_000;
        await AddAsync(manager, Gauge(raid.Id, "ovs.sessions", now.AddSeconds(-10), 3), Gauge(lobby.Id, "ovs.sessions", now.AddSeconds(-20), 4),
            Gauge(lobby.Id, "ovs.sessions", now.AddMinutes(-5), 9), // too old: not counted
            new MetricRollup { ServerId = raid.Id, Name = "ovs.voice.bytes", Kind = MetricKind.Sum, PeriodUnixMs = lastMinute, Value = 750_000, Attributes = "{\"ovs.direction\":\"in\"}" },
            new MetricRollup { ServerId = lobby.Id, Name = "ovs.voice.bytes", Kind = MetricKind.Sum, PeriodUnixMs = lastMinute, Value = 75_000, Attributes = "{\"ovs.direction\":\"out\"}" });
        return manager;
    }

    static string Tile(IRenderedComponent<ServerList> page, string name) => page.Find($"[data-dashboard] [data-tile={name}] [data-value]").TextContent;

    /// <summary>AC2: users online and bandwidth over all servers, the ports taken of 100, the servers by status and the updates.</summary>
    [Fact]
    public async Task Dashboard_TwoServers_TotalsPortsStatusUpdates()
    {
        await using var manager = await TwoServersAsync();
        await using var context = ServerPages.Context(manager);

        var page = context.Render<ServerList>();

        Assert.Equal("7", Tile(page, "users"));
        Assert.Equal("100 / 10", Tile(page, "bandwidth"));
        Assert.Equal("2 / 100", Tile(page, "ports"));
        Assert.StartsWith("2 ", Tile(page, "status"), StringComparison.Ordinal);
        Assert.Equal("2", Tile(page, "updates"));
    }

    /// <summary>AC3: a manager's dashboard counts only the servers assigned to them.</summary>
    [Fact]
    public async Task Dashboard_Manager_OnlyAssignedCounted()
    {
        await using var manager = await TwoServersAsync();
        var raid = manager.Services.GetRequiredService<StatusWatcher>().Servers.Single(server => server.Name == "Raid");
        var anna = await ManagerSignIn.CreateAsync(manager, "anna");
        await using (var scope = manager.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ServerAssignmentList>().SetAsync(anna, raid.Id, true, "chef");
        await using var context = ServerPages.Context(manager, ManagerRoles.Manager, anna);

        var page = context.Render<ServerList>();

        Assert.Equal("3", Tile(page, "users"));
        Assert.Equal("1 / 100", Tile(page, "ports"));
        Assert.Equal("1", Tile(page, "updates"));
    }
}
