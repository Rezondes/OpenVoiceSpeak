using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using OVS.Manager.Accounts;
using OVS.Manager.Audit;
using OVS.Manager.Components.Pages.Servers;
using OVS.Manager.Servers;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Manager.Servers;

/// <summary>Package M10 (D3, D4): the admin assigns servers to managers, and a manager sees and operates only those.</summary>
public sealed class ServerAccessTests
{
    static async Task<IReadOnlySet<string>> ServersOfAsync(ManagerFactory manager, string userId)
    {
        await using var scope = manager.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ServerAssignmentList>().ServersOfAsync(userId);
    }

    static async Task AssignAsync(ManagerFactory manager, string userId, string serverId, bool assigned = true)
    {
        await using var scope = manager.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ServerAssignmentList>().SetAsync(userId, serverId, assigned, "chef");
    }

    static Task TickAsync(IRenderedComponent<Assignments> page, string choice, bool ticked) =>
        page.Find($"label[data-choice='{choice}'] input").ChangeAsync(new ChangeEventArgs { Value = ticked });

    /// <summary>AC1: the account page ticks servers, the server page ticks managers; both change the same assignments.</summary>
    [Fact]
    public async Task Assign_AccountAndServerPage_SameAssignments()
    {
        await using var manager = StatusWatcherTests.WithAdmin();
        manager.CreateClient().Dispose();
        var raid = await StatusWatcherTests.CreateAsync(manager, "Raid");
        var lobby = await StatusWatcherTests.CreateAsync(manager, "Lobby");
        var anna = await ManagerSignIn.CreateAsync(manager, "anna");
        await using var context = ServerPages.Context(manager);

        var account = context.Render<Assignments>(parameters => parameters.Add(component => component.UserId, anna));
        await TickAsync(account, raid.Id, true);
        Assert.Equal([raid.Id], await ServersOfAsync(manager, anna));

        var server = context.Render<Assignments>(parameters => parameters.Add(component => component.ServerId, lobby.Id));
        await TickAsync(server, anna, true);
        Assert.Equal(new HashSet<string> { raid.Id, lobby.Id }, await ServersOfAsync(manager, anna));

        var raidPage = context.Render<Assignments>(parameters => parameters.Add(component => component.ServerId, raid.Id));
        Assert.True(raidPage.Find($"label[data-choice='{anna}'] input").HasAttribute("checked"));
        await TickAsync(raidPage, anna, false);
        Assert.Equal([lobby.Id], await ServersOfAsync(manager, anna));
    }

    /// <summary>AC2: a manager's overview, server pages and audit export hold only the assigned servers; another one is 403.</summary>
    [Fact]
    public async Task Manager_OtherServer_Forbidden()
    {
        await using var manager = StatusWatcherTests.WithAdmin();
        manager.CreateClient().Dispose();
        var raid = await StatusWatcherTests.CreateAsync(manager, "Raid");
        var lobby = await StatusWatcherTests.CreateAsync(manager, "Lobby");
        var anna = await ManagerSignIn.CreateAsync(manager, "anna");
        await AssignAsync(manager, anna, raid.Id);
        await manager.Services.GetRequiredService<StatusWatcher>().RefreshAsync();
        using var client = await ManagerSignIn.LoginAsync(manager, "anna");

        var overview = await client.GetStringAsync(new Uri("/", UriKind.Relative));
        Assert.Contains("Raid", overview, StringComparison.Ordinal);
        Assert.DoesNotContain("Lobby", overview, StringComparison.Ordinal);
        using (var own = await client.GetAsync(new Uri($"/servers/{raid.Id}", UriKind.Relative)))
            Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        using (var other = await client.GetAsync(new Uri($"/servers/{lobby.Id}", UriKind.Relative)))
            Assert.Equal(HttpStatusCode.Forbidden, other.StatusCode);
        var export = await client.GetStringAsync(new Uri("/audit/export", UriKind.Relative));
        Assert.Contains(raid.Id, export, StringComparison.Ordinal);
        Assert.DoesNotContain(lobby.Id, export, StringComparison.Ordinal);
    }

    /// <summary>AC3: a manager operates an assigned server but never creates, deletes or assigns.</summary>
    [Fact]
    public async Task Manager_CreateDeleteAssign_Forbidden()
    {
        await using var manager = StatusWatcherTests.WithAdmin();
        manager.CreateClient().Dispose();
        var raid = await StatusWatcherTests.CreateAsync(manager, "Raid");
        var anna = await ManagerSignIn.CreateAsync(manager, "anna");
        await AssignAsync(manager, anna, raid.Id);
        await manager.Services.GetRequiredService<StatusWatcher>().RefreshAsync();
        using var client = await ManagerSignIn.LoginAsync(manager, "anna");

        foreach (var page in new[] { "/servers/new", "/servers/trash", $"/accounts/{anna}", "/accounts" })
        {
            using var response = await client.GetAsync(new Uri(page, UriKind.Relative));
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        await using var context = ServerPages.Context(manager, ManagerRoles.Manager, anna);
        var serverPage = context.Render<ServerPage>(parameters => parameters.Add(component => component.Id, raid.Id));
        Assert.Equal(3, serverPage.FindAll("span.server-actions button").Count);
        Assert.Empty(serverPage.FindAll("section.danger-zone"));
        Assert.Empty(serverPage.FindAll("section.assignments"));
        await serverPage.FindAll("span.server-actions button")[1].ClickAsync(new());
        Assert.Equal("exited", manager.Docker.Containers[raid.ContainerId!].State);
    }

    /// <summary>AC4: assigning and unassigning each write a line with the manager and the server.</summary>
    [Fact]
    public async Task Assign_EachChange_AuditLine()
    {
        await using var manager = StatusWatcherTests.WithAdmin();
        manager.CreateClient().Dispose();
        var raid = await StatusWatcherTests.CreateAsync(manager, "Raid");
        var anna = await ManagerSignIn.CreateAsync(manager, "anna");
        await AssignAsync(manager, anna, raid.Id);
        await AssignAsync(manager, anna, raid.Id); // already assigned: no second line
        await AssignAsync(manager, anna, raid.Id, assigned: false);

        await using var scope = manager.Services.CreateAsyncScope();
        var lines = await scope.ServiceProvider.GetRequiredService<AuditLog>().QueryAsync(new AuditFilter(ServerId: raid.Id), servers: null);
        Assert.Equal([(AuditActions.ServerAssigned, "anna"), (AuditActions.ServerUnassigned, "anna")],
            lines.Where(line => line.Action != AuditActions.ServerCreated).OrderBy(line => line.Id).Select(line => (line.Action, line.Target)));
    }
}
