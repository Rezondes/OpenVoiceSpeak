using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OVS.Manager.Audit;
using OVS.Manager.Components.Pages.Servers;
using OVS.Manager.Control;
using OVS.Manager.Data;
using OVS.Manager.Docker;
using OVS.Manager.Servers;
using OVS.Shared.Protocol;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Manager.Servers;

/// <summary>Package M18: a server updated to a new release, rolled back on a failed health check, and all servers one by one.</summary>
public sealed class ServerUpdateTests
{
    static readonly Dictionary<string, string> Variables = new() { ["OVS_PORT"] = "7000", ["OVS_SERVER_NAME"] = "Raid", ["TZ"] = "UTC" };

    /// <summary>The manager with a 2 s health check and GitHub offering 201026.zz of the manager's protocol.</summary>
    static ManagerFactory Manager()
    {
        var manager = ServerLinks.Manager(("OVS_MANAGER_UPDATE_HEALTH_SECONDS", "2"));
        manager.GitHub.Respond = _ => FakeHttp.Json(new[]
        {
            new Dictionary<string, object> { ["tag_name"] = "deploy-201026z", ["name"] = "OpenVoiceSpeak 201026.zz", ["published_at"] = DateTimeOffset.UtcNow, ["draft"] = false, ["body"] = $"Protokoll: {WireProtocol.Version}" },
        });
        return manager;
    }

    /// <summary>A linked server whose container carries its variables, as Docker would report them.</summary>
    static async Task<LinkedServer> StartAsync(ManagerFactory manager)
    {
        var linked = await ServerLinks.StartAsync(manager);
        manager.Docker.Inspected["c-" + linked.Id] = new InspectedContainer("running", 0, 0, DateTimeOffset.UtcNow, Variables);
        await manager.Services.GetRequiredService<ReleaseCatalog>().RefreshAsync();
        return linked;
    }

    static async Task<ManagedServerRow> RowAsync(ManagerFactory manager, string id)
    {
        await using var scope = manager.Services.CreateAsyncScope();
        var server = await scope.ServiceProvider.GetRequiredService<ManagerDbContext>().Servers.SingleAsync(row => row.Id == id);
        return new ManagedServerRow(server.Version, server.ContainerId);
    }

    /// <summary>AC1, AC5: pull, backup, stop, the same container with the new tag, start and health, in this order, each in the audit log.</summary>
    [Fact]
    public async Task Update_StepsInOrder_SameSpecNewTag()
    {
        await using var manager = Manager();
        manager.CreateClient().Dispose();
        await using var linked = await StartAsync(manager);
        await using var scope = manager.Services.CreateAsyncScope();
        var steps = new List<UpdateStep>();

        Assert.Null(await scope.ServiceProvider.GetRequiredService<ServerUpdate>().UpdateAsync(linked.Id, "201026.zz", "chef", new SynchronousProgress(steps.Add)));

        Assert.Equal([UpdateStep.Pull, UpdateStep.Backup, UpdateStep.Stop, UpdateStep.Recreate, UpdateStep.Start, UpdateStep.Health, UpdateStep.Done], steps);
        Assert.Equal("ghcr.io/rezondes/openvoicespeak-server:201026.zz", Assert.Single(manager.Docker.Pulled));
        var spec = Assert.Single(manager.Docker.Created);
        Assert.Equal((linked.Id, "201026.zz", linked.Server.Port, "ovs-managed"), (spec.ServerId, spec.Version, spec.Port, spec.Network));
        Assert.Equal(Variables, spec.Environment);
        Assert.False(manager.Docker.Containers.ContainsKey("c-" + linked.Id));
        Assert.Equal("201026.zz", (await RowAsync(manager, linked.Id)).Version);
        var marks = (await scope.ServiceProvider.GetRequiredService<BackupCommands>().ListAsync(linked.Id)).Marks.Values;
        Assert.Equal("vor Update 201026.zz", Assert.Single(marks).Note);
        var lines = await scope.ServiceProvider.GetRequiredService<AuditLog>().QueryAsync(new AuditFilter(ServerId: linked.Id), servers: null);
        Assert.Equal(7, lines.Count(line => line.Action == AuditActions.ServerUpdateStep));
        Assert.Contains(lines, line => line.Action == AuditActions.ServerUpdated && line.Detail == "test -> 201026.zz");
    }

    /// <summary>AC2: a failed health check brings back the old version and restores the backup, with the reason.</summary>
    [Fact]
    public async Task Update_HealthFails_OldVersionAndBackupBack()
    {
        await using var manager = Manager();
        manager.CreateClient().Dispose();
        await using var linked = await StartAsync(manager);
        manager.Probe.Silent[$"127.0.0.1:{linked.Server.Port}"] = 0;
        await using var scope = manager.Services.CreateAsyncScope();

        var failed = await scope.ServiceProvider.GetRequiredService<ServerUpdate>().UpdateAsync(linked.Id, "201026.zz", "chef");

        Assert.Equal("Update fehlgeschlagen, alte Version wiederhergestellt: Der Server hat nicht rechtzeitig geantwortet.", failed);
        Assert.Equal(["201026.zz", "test"], manager.Docker.Created.Select(spec => spec.Version));
        var row = await RowAsync(manager, linked.Id);
        Assert.Equal("test", row.Version);
        Assert.Equal("test", manager.Docker.Containers[row.ContainerId!].Version);
        var lines = await scope.ServiceProvider.GetRequiredService<AuditLog>().QueryAsync(new AuditFilter(ServerId: linked.Id), servers: null);
        Assert.Contains(lines, line => line.Action == AuditActions.BackupRestored);
        Assert.Contains(lines, line => line.Action == AuditActions.ServerUpdateFailed);
    }

    /// <summary>AC3: "Alle aktualisieren" goes by port and stops at the first failure; the later server is not touched.</summary>
    [Fact]
    public async Task UpdateAll_OneFails_StopsAtFirstFailure()
    {
        await using var manager = Manager();
        manager.CreateClient().Dispose();
        await using var one = await StartAsync(manager);
        await using var two = await StartAsync(manager);
        var (first, second) = one.Server.Port < two.Server.Port ? (one, two) : (two, one);
        manager.Probe.Silent[$"127.0.0.1:{first.Server.Port}"] = 0;
        await using var scope = manager.Services.CreateAsyncScope();

        var result = await scope.ServiceProvider.GetRequiredService<ServerUpdate>().UpdateAllAsync(null, "chef");

        Assert.Equal(0, result.Updated);
        Assert.StartsWith("Raid: Update fehlgeschlagen", result.Error, StringComparison.Ordinal);
        Assert.Equal("test", (await RowAsync(manager, second.Id)).Version);
        Assert.DoesNotContain(manager.Docker.Created, spec => spec.ServerId == second.Id);
    }

    /// <summary>AC5: the dialog shows each step as it happens and the result.</summary>
    [Fact]
    public async Task Dialog_Update_StepsShownLive()
    {
        await using var manager = Manager();
        manager.CreateClient().Dispose();
        await using var linked = await StartAsync(manager);
        await using var context = ServerPages.Context(manager);
        var dialog = context.Render<UpdateDialog>(parameters => parameters.Add(component => component.ServerId, linked.Id).Add(component => component.Version, "test"));

        await dialog.Find("[data-update]").ClickAsync(new());

        dialog.WaitForAssertion(() => Assert.Equal("Aktualisiert auf 201026.zz.", dialog.Find("[data-result]").TextContent));
        Assert.Equal(7, dialog.FindAll("[data-steps] li").Count);
        Assert.Equal("Image laden", dialog.FindAll("[data-steps] li")[0].TextContent);
    }

    /// <summary>A server row's version and container.</summary>
    sealed record ManagedServerRow(string Version, string? ContainerId);

    /// <summary>Reports at once on the caller's thread, unlike <see cref="Progress{T}"/>.</summary>
    sealed class SynchronousProgress(Action<UpdateStep> report) : IProgress<UpdateStep>
    {
        public void Report(UpdateStep value) => report(value);
    }
}
