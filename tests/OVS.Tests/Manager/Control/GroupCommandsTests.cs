using Bunit;
using Microsoft.Extensions.DependencyInjection;
using OVS.Manager.Components.Pages.Servers;
using OVS.Manager.Control;
using OVS.Shared.Permissions;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Manager.Control;

/// <summary>Package M13 (AC2, AC4): groups and their holders through the manager's service session.</summary>
public sealed class GroupCommandsTests
{
    /// <summary>AC2: the known users carry their logins; the server admin gives a user Admin (the last admin stays) and gives and takes another group.</summary>
    [Fact]
    public async Task KnownUser_AssignAdmin_Sent()
    {
        await using var manager = ServerLinks.Manager();
        manager.CreateClient().Dispose();
        await using var linked = await ServerLinks.StartAsync(manager);
        await using var scope = manager.Services.CreateAsyncScope();
        var groups = scope.ServiceProvider.GetRequiredService<GroupCommands>();
        var moderation = scope.ServiceProvider.GetRequiredService<ModerationCommands>();
        await using (var client = await TestClient.ConnectAsync(linked.Server, "anna"))
            await AsyncWait.UntilAsync(() => moderation.Online(linked.Id).Count == 1, ServerLinks.Within);

        var anna = Assert.Single((await moderation.KnownUsersAsync(linked.Id)).Users, user => user.LastNickname == "anna");
        Assert.NotNull(anna.LastLogin);
        var admin = groups.Groups(linked.Id).Single(group => group.Id == WellKnownGroups.Admin);

        async Task<IReadOnlyList<Guid>> GroupsOfAnnaAsync() => (await moderation.KnownUsersAsync(linked.Id)).Users.Single(user => user.LastNickname == "anna").GroupIds;
        Assert.Null(await groups.AssignAsync(linked.Id, anna, admin, "chef"));
        Assert.Contains(WellKnownGroups.Admin, await GroupsOfAnnaAsync());
        Assert.StartsWith("Der Server hat abgelehnt:", await groups.UnassignAsync(linked.Id, anna, admin, "chef"), StringComparison.Ordinal); // the last admin stays

        Assert.Null(await groups.CreateAsync(linked.Id, "Mods", Permission.Speak, "chef"));
        var mods = groups.Groups(linked.Id).Single(group => group.Name == "Mods");
        Assert.Null(await groups.AssignAsync(linked.Id, anna, mods, "chef"));
        Assert.Contains(mods.Id, await GroupsOfAnnaAsync());
        Assert.Null(await groups.UnassignAsync(linked.Id, anna, mods, "chef"));
        Assert.DoesNotContain(mods.Id, await GroupsOfAnnaAsync());
    }

    /// <summary>AC4: Admin and Server-Admin cannot be edited, and Guest, Admin and Server-Admin cannot be deleted.</summary>
    [Fact]
    public async Task FixedGroups_Page_NotEditable()
    {
        await using var manager = ServerLinks.Manager();
        manager.CreateClient().Dispose();
        await using var linked = await ServerLinks.StartAsync(manager);
        await using var scope = manager.Services.CreateAsyncScope();
        var groups = scope.ServiceProvider.GetRequiredService<GroupCommands>();
        var moderation = scope.ServiceProvider.GetRequiredService<ModerationCommands>();
        await using var context = ServerPages.Context(manager);
        var page = context.Render<ServerGroups>(parameters => parameters.Add(component => component.Id, linked.Id));

        var admin = page.Find("tr[data-group='Admin']");
        Assert.Empty(admin.QuerySelectorAll("[data-edit], [data-delete]"));
        var guest = page.Find($"tr[data-group='{groups.Groups(linked.Id).Single(group => group.Id == WellKnownGroups.Guest).Name}']");
        Assert.NotNull(guest.QuerySelector("[data-edit]"));
        Assert.Null(guest.QuerySelector("[data-delete]"));
    }

    /// <summary>AC4: a group is created, moved up and deleted on the server.</summary>
    [Fact]
    public async Task Groups_CreateMoveDelete_ChangedOnServer()
    {
        await using var manager = ServerLinks.Manager();
        manager.CreateClient().Dispose();
        await using var linked = await ServerLinks.StartAsync(manager);
        await using var scope = manager.Services.CreateAsyncScope();
        var groups = scope.ServiceProvider.GetRequiredService<GroupCommands>();
        var moderation = scope.ServiceProvider.GetRequiredService<ModerationCommands>();

        Assert.Null(await groups.CreateAsync(linked.Id, "Mods", Permission.Speak | Permission.UserKick, "chef"));
        Assert.Equal("Mods", groups.Groups(linked.Id)[^1].Name);
        var mods = groups.Groups(linked.Id).Single(group => group.Name == "Mods");
        Assert.Null(await groups.MoveAsync(linked.Id, mods, -1, "chef"));
        Assert.Equal("Mods", groups.Groups(linked.Id)[^2].Name);
        Assert.Null(await groups.DeleteAsync(linked.Id, mods, "chef"));
        Assert.DoesNotContain(groups.Groups(linked.Id), group => group.Name == "Mods");
    }
}
