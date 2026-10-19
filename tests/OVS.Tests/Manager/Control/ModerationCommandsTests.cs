using Microsoft.Extensions.DependencyInjection;
using OVS.Manager.Audit;
using OVS.Manager.Control;
using OVS.Server.Data.Records;
using OVS.Shared.Protocol.Messages.Users;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Manager.Control;

/// <summary>Package M13 (AC1, AC3, AC5): moderation of a server's users through the manager's service session.</summary>
public sealed class ModerationCommandsTests
{
    static readonly Guid Lounge = Guid.Parse("10000000-0000-0000-0000-000000000001");

    static Task<LinkedServer> StartAsync(ManagerFactory manager) =>
        ServerLinks.StartAsync(manager, data => data.Channels.Add(new ChannelRecord { Id = Lounge, Name = "Lounge", Order = 1 }));

    static UserInfo Online(ModerationCommands commands, string id, string nickname) => commands.Online(id).Single(user => user.Nickname == nickname);

    static Task OnlineCountAsync(ModerationCommands commands, string id, int count) =>
        AsyncWait.UntilAsync(() => commands.Online(id).Count == count, ServerLinks.Within);

    /// <summary>AC1: the online list follows the server, and mute, move, kick and ban act on its users.</summary>
    [Fact]
    public async Task Online_MuteMoveKickBan_Followed()
    {
        await using var manager = ServerLinks.Manager();
        manager.CreateClient().Dispose();
        await using var linked = await StartAsync(manager);
        await using var scope = manager.Services.CreateAsyncScope();
        var commands = scope.ServiceProvider.GetRequiredService<ModerationCommands>();
        await using var anna = await TestClient.ConnectAsync(linked.Server, "anna");
        await using var bert = await TestClient.ConnectAsync(linked.Server, "bert");
        await OnlineCountAsync(commands, linked.Id, 2);

        Assert.Null(await commands.MuteAsync(linked.Id, Online(commands, linked.Id, "anna"), true, 10, "chef"));
        Assert.True(Online(commands, linked.Id, "anna").ServerMuted);
        Assert.Null(await commands.MoveAsync(linked.Id, Online(commands, linked.Id, "anna"), Lounge, "chef"));
        Assert.Equal(Lounge, Online(commands, linked.Id, "anna").ChannelId);
        Assert.Null(await commands.KickAsync(linked.Id, Online(commands, linked.Id, "bert"), "Pause", "chef"));
        Assert.Null(await commands.BanAsync(linked.Id, Online(commands, linked.Id, "anna"), "Spam", 60, false, "chef"));
        Assert.Empty(commands.Online(linked.Id));
    }

    /// <summary>AC3: the ban list holds reason, end and who banned, and an unban lifts it.</summary>
    [Fact]
    public async Task Bans_Unban_ListedAndLifted()
    {
        await using var manager = ServerLinks.Manager();
        manager.CreateClient().Dispose();
        await using var linked = await StartAsync(manager);
        await using var scope = manager.Services.CreateAsyncScope();
        var commands = scope.ServiceProvider.GetRequiredService<ModerationCommands>();
        await using (var anna = await TestClient.ConnectAsync(linked.Server, "anna"))
        {
            await OnlineCountAsync(commands, linked.Id, 1);
            Assert.Null(await commands.BanAsync(linked.Id, Online(commands, linked.Id, "anna"), "Spam", null, false, "chef"));
        }

        var ban = Assert.Single((await commands.BansAsync(linked.Id)).Bans);
        Assert.Equal(("anna", "Spam", "Serververwaltung", (DateTimeOffset?)null), (ban.Nickname, ban.Reason, ban.CreatedBy, ban.ExpiresAt));

        var after = await commands.UnbanAsync(linked.Id, ban, "chef");
        Assert.Null(after.Error);
        Assert.NotNull(Assert.Single(after.Bans).LiftedAt);
    }

    /// <summary>AC5: each action writes a line in the manager besides the server's own.</summary>
    [Fact]
    public async Task Actions_EachOne_AuditLine()
    {
        await using var manager = ServerLinks.Manager();
        manager.CreateClient().Dispose();
        await using var linked = await StartAsync(manager);
        await using var scope = manager.Services.CreateAsyncScope();
        var commands = scope.ServiceProvider.GetRequiredService<ModerationCommands>();
        await using var anna = await TestClient.ConnectAsync(linked.Server, "anna");
        await OnlineCountAsync(commands, linked.Id, 1);

        await commands.MuteAsync(linked.Id, Online(commands, linked.Id, "anna"), true, null, "chef");
        await commands.MuteAsync(linked.Id, Online(commands, linked.Id, "anna"), false, null, "chef");
        await commands.KickAsync(linked.Id, Online(commands, linked.Id, "anna"), "Pause", "chef");

        var lines = await scope.ServiceProvider.GetRequiredService<AuditLog>().QueryAsync(new AuditFilter(ServerId: linked.Id), servers: null);
        Assert.Equal([(AuditActions.UserMuted, "anna", "permanent"), (AuditActions.UserUnmuted, "anna", null), (AuditActions.UserKicked, "anna", "Pause")],
            lines.OrderBy(line => line.Id).Select(line => (line.Action, line.Target, line.Detail)));
    }
}
