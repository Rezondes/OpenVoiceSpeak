using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OVS.Manager.Accounts;
using OVS.Manager.Audit;
using OVS.Manager.Components.Pages.Servers;
using OVS.Manager.Data;
using OVS.Manager.Docker;
using OVS.Manager.Exceptions;
using OVS.Manager.Servers;
using OVS.Tests.Manager.Accounts;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Manager.Servers;

/// <summary>Package M9 (MA9): deleting into the trash, restoring, the sweep after 7 days and the audit lines.</summary>
public sealed class ServerDeletionTests
{
    static ManagerFactory WithAdmin(TimeProvider? time = null) =>
        new((FirstAdmin.UserVariable, "chef"), (FirstAdmin.PasswordVariable, FirstAdminTests.Password)) { Time = time };

    static async Task<T> InScopeAsync<T>(ManagerFactory manager, Func<IServiceProvider, Task<T>> body)
    {
        await using var scope = manager.Services.CreateAsyncScope();
        return await body(scope.ServiceProvider);
    }

    static async Task DeleteAsync(ManagerFactory manager, ManagedServer server, string typed)
    {
        await using var scope = manager.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ServerDeletion>().DeleteAsync(server.Id, typed, "chef");
    }

    static Task<List<ManagedServer>> TrashAsync(ManagerFactory manager) =>
        InScopeAsync(manager, services => services.GetRequiredService<ServerDeletion>().ListDeletedAsync());

    /// <summary>AC1: a name that is not exactly the server's changes nothing.</summary>
    [Fact]
    public async Task Delete_NameWrong_Refused()
    {
        await using var manager = WithAdmin();
        manager.CreateClient().Dispose();
        var server = await StatusWatcherTests.CreateAsync(manager, "Raid");

        var refused = await Assert.ThrowsAsync<ServerRequestException>(() => DeleteAsync(manager, server, "raid"));
        Assert.Equal(ServerProblem.NameMismatch, refused.Problem);
        Assert.Empty(await TrashAsync(manager));
        Assert.Equal("running", manager.Docker.Containers[server.ContainerId!].State);
    }

    /// <summary>AC1: the admin's deletion stops the server, takes it off the overview and lists it in the trash; only the admin sees the dialog and the trash.</summary>
    [Fact]
    public async Task Delete_Admin_StoppedInTrash()
    {
        await using var manager = WithAdmin();
        manager.CreateClient().Dispose();
        var server = await StatusWatcherTests.CreateAsync(manager, "Raid");
        var watcher = manager.Services.GetRequiredService<StatusWatcher>();
        await watcher.RefreshAsync();
        await using (var context = ServerPages.Context(manager))
        {
            var page = context.Render<ServerPage>(parameters => parameters.Add(component => component.Id, server.Id));
            await page.Find("section.danger-zone input").InputAsync(new ChangeEventArgs { Value = "Raid" });
            await page.Find("section.danger-zone button").ClickAsync(new());
        }

        Assert.Equal("exited", manager.Docker.Containers[server.ContainerId!].State);
        Assert.Equal(server.Id, Assert.Single(await TrashAsync(manager)).Id);
        Assert.DoesNotContain(watcher.Servers, snapshot => snapshot.Id == server.Id);

        await using var managerContext = ServerPages.Context(manager, ManagerRoles.Manager);
        await StatusWatcherTests.CreateAsync(manager, "Lobby");
        await watcher.RefreshAsync();
        var other = watcher.Servers.Single();
        Assert.Empty(managerContext.Render<ServerPage>(parameters => parameters.Add(component => component.Id, other.Id)).FindAll("section.danger-zone"));

        await ManagerSignIn.CreateAsync(manager, "anna");
        using var annaClient = await ManagerSignIn.LoginAsync(manager, "anna");
        using var trash = await annaClient.GetAsync(new Uri("/servers/trash", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Forbidden, trash.StatusCode);
    }

    /// <summary>AC2: a restore within 7 days brings back the same container, volume and port, started.</summary>
    [Fact]
    public async Task Restore_WithinPeriod_SameServer()
    {
        var time = new ManualTimeProvider();
        await using var manager = WithAdmin(time);
        manager.CreateClient().Dispose();
        var server = await StatusWatcherTests.CreateAsync(manager, "Raid");
        await DeleteAsync(manager, server, "Raid");
        time.Advance(TimeSpan.FromDays(6));

        await InScopeAsync(manager, async services =>
        {
            await services.GetRequiredService<ServerDeletion>().RestoreAsync(server.Id, "chef");
            return true;
        });

        Assert.Empty(await TrashAsync(manager));
        Assert.Equal("running", manager.Docker.Containers[server.ContainerId!].State);
        Assert.Empty(manager.Docker.RemovedVolumes);
        var restored = await InScopeAsync(manager, services => services.GetRequiredService<ManagerDbContext>().Servers.SingleAsync());
        Assert.Equal((server.Id, server.Port, server.ContainerId, server.AdminFingerprint), (restored.Id, restored.Port, restored.ContainerId, restored.AdminFingerprint));
        Assert.Contains(manager.Services.GetRequiredService<StatusWatcher>().Servers, snapshot => snapshot.Id == server.Id);
    }

    /// <summary>AC3: the sweep keeps a server 7 days, then removes container, volume and rows, and the port is free again.</summary>
    [Fact]
    public async Task Sweep_AfterSevenDays_RemovedPortFree()
    {
        var time = new ManualTimeProvider();
        await using var manager = WithAdmin(time);
        manager.CreateClient().Dispose();
        var server = await StatusWatcherTests.CreateAsync(manager, "Raid");
        await DeleteAsync(manager, server, "Raid");
        Assert.Equal(7001, (await StatusWatcherTests.CreateAsync(manager, "Lobby")).Port); // the port stays taken in the trash

        time.Advance(ServerDeletion.KeepFor - TimeSpan.FromMinutes(1));
        Assert.Equal(0, await InScopeAsync(manager, services => services.GetRequiredService<ServerDeletion>().SweepAsync()));
        time.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(1, await InScopeAsync(manager, services => services.GetRequiredService<ServerDeletion>().SweepAsync()));

        Assert.False(manager.Docker.Containers.ContainsKey(server.ContainerId!));
        Assert.Equal([server.Id], manager.Docker.RemovedVolumes);
        Assert.Empty(await TrashAsync(manager));
        Assert.False(await InScopeAsync(manager, services => services.GetRequiredService<ManagerDbContext>().ServerIdentities.AnyAsync(row => row.ServerId == server.Id)));
        Assert.Equal(7000, (await StatusWatcherTests.CreateAsync(manager, "Neu")).Port);
    }

    /// <summary>AC3: "delete for good now" needs a second click, then removes the server at once.</summary>
    [Fact]
    public async Task DeleteNow_Confirmed_Removed()
    {
        await using var manager = WithAdmin();
        manager.CreateClient().Dispose();
        var server = await StatusWatcherTests.CreateAsync(manager, "Raid");
        await DeleteAsync(manager, server, "Raid");
        await using var context = ServerPages.Context(manager);
        var page = context.Render<Trash>();

        await page.Find($"tr[data-server='{server.Id}'] button.danger").ClickAsync(new());
        Assert.Single(await TrashAsync(manager));
        await page.Find($"tr[data-server='{server.Id}'] button[data-confirm]").ClickAsync(new());

        Assert.Empty(await TrashAsync(manager));
        Assert.False(manager.Docker.Containers.ContainsKey(server.ContainerId!));
        page.WaitForAssertion(() => Assert.Empty(page.FindAll("tr[data-server]")));
    }

    /// <summary>AC4: deleting, restoring and removing for good each write a line; the sweep writes as "system".</summary>
    [Fact]
    public async Task Steps_DeleteRestorePurge_AuditLines()
    {
        var time = new ManualTimeProvider();
        await using var manager = WithAdmin(time);
        manager.CreateClient().Dispose();
        var server = await StatusWatcherTests.CreateAsync(manager, "Raid");
        await DeleteAsync(manager, server, "Raid");
        await InScopeAsync(manager, async services =>
        {
            await services.GetRequiredService<ServerDeletion>().RestoreAsync(server.Id, "chef");
            return true;
        });
        await DeleteAsync(manager, server, "Raid");
        time.Advance(TimeSpan.FromDays(8));
        await InScopeAsync(manager, services => services.GetRequiredService<ServerDeletion>().SweepAsync());

        var lines = await InScopeAsync(manager, services =>
            services.GetRequiredService<AuditLog>().QueryAsync(new AuditFilter(ServerId: server.Id), servers: null));
        Assert.Equal([(AuditActions.ServerCreated, "chef"), (AuditActions.ServerDeleted, "chef"), (AuditActions.ServerRestored, "chef"),
                      (AuditActions.ServerDeleted, "chef"), (AuditActions.ServerPurged, ServerDeletion.SweepActor)],
            lines.OrderBy(line => line.Id).Select(line => (line.Action, line.UserName)));
    }
}
