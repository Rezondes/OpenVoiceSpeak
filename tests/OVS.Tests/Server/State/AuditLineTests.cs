using System.Reflection;
using Microsoft.Extensions.Logging;
using OVS.Server.Logging;
using OVS.Server.State;
using OVS.Shared.Identity;
using OVS.Shared.Logging;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Chat;
using OVS.Shared.Protocol.Messages.Groups;
using OVS.Shared.Protocol.Messages.Links;
using OVS.Shared.Protocol.Messages.LogAccess;
using OVS.Shared.Protocol.Messages.Moderation;
using OVS.Shared.Protocol.Messages.ServerBackups;
using OVS.Shared.Protocol.Messages.ServerInfo;
using OVS.Shared.Protocol.Messages.Users;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server.State;

/// <summary>Package 165: every administrative request writes one audit line naming its actor.</summary>
public sealed class AuditLineTests
{
    /// <summary>The requests that change nothing (or only the own state), so they write no audit line.</summary>
    static readonly Type[] NoChange =
    [
        typeof(JoinChannel), typeof(SetSelfState), typeof(SendChat), typeof(ListUsers), typeof(ListBans), typeof(GetServerIcon), typeof(ListBackups),
        typeof(ListLogs), typeof(ReadLog), typeof(SearchLogs), typeof(PrepareLogDownload), typeof(DownloadLogChunk),
    ];

    /// <summary>What the audit line of each administrative request says (the action, after the actor).</summary>
    static readonly Dictionary<Type, string> Expected = new()
    {
        [typeof(RedeemAdminToken)] = "neu redeemed the admin token",
        [typeof(CreateGroup)] = "Group 'Team' created by chef",
        [typeof(UpdateGroup)] = "Group 'Team' changed by chef",
        [typeof(ReorderGroups)] = "Groups reordered by chef",
        [typeof(AssignGroup)] = "Group 'Team 2' given to gast1 by chef",
        [typeof(UnassignGroup)] = "Group 'Team 2' taken from gast1 by chef",
        [typeof(DeleteGroup)] = "Group 'Team 2' deleted by chef",
        [typeof(CreateChannel)] = "Channel 'Raid' created by chef",
        [typeof(EditChannel)] = "Channel 'Raid 2' changed by chef",
        [typeof(ReorderChannels)] = "Channels reordered by chef",
        [typeof(MoveUser)] = "gast1 moved from 'Lobby' to 'Raid 2' by chef",
        [typeof(LinkChannels)] = "Links changed by chef: 1 set, 0 removed",
        [typeof(UnlinkChannels)] = "Links changed by chef: 0 set, 1 removed",
        [typeof(SetChannelLinks)] = "Links changed by chef",
        [typeof(DeleteChannel)] = "Channel 'Raid 2' deleted by chef",
        [typeof(UpdateServerSettings)] = "Server settings changed by chef",
        [typeof(SetServerIcon)] = "Server logo changed by chef",
        [typeof(SetSessionServerMute)] = "gast1 server-muted by chef",
        [typeof(SetKnownUserServerMute)] = "gast1 server-unmuted by chef",
        [typeof(KickSession)] = "gast1 was kicked by chef",
        [typeof(BanSession)] = "gast2 was banned by chef",
        [typeof(Unban)] = "Ban of gast2 lifted by chef",
        [typeof(BanKnownUser)] = "gast1 was banned by chef",
        [typeof(DeleteKnownUser)] = "User data of gast1 deleted by chef",
        [typeof(CreateBackup)] = "created by chef",
        [typeof(DownloadBackup)] = "being downloaded by chef",
        [typeof(DeleteBackup)] = "deleted by chef",
        // restoring restarts the server and an upload needs a whole backup (BackupTests drive both): their lines are checked below
        [typeof(RestoreBackup)] = nameof(ServerLogLines.RestoreStarted),
        [typeof(UploadBackupChunk)] = nameof(ServerLogLines.BackupUploaded),
    };

    [Fact]
    public async Task AdministrativeRequest_Each_WritesAuditLine()
    {
        // a request type of the protocol without an entry (a new administrative one) fails here first
        Assert.Empty(ServerState.Routes.Keys.Except(NoChange).Except(Expected.Keys).Select(type => type.Name));

        foreach (var name in new[] { nameof(ServerLogLines.RestoreStarted), nameof(ServerLogLines.BackupUploaded) })
            Assert.StartsWith(LogFileLoggerProvider.AuditEventPrefix, typeof(ServerLogLines).GetMethod(name)!.GetCustomAttribute<LoggerMessageAttribute>()!.EventName, StringComparison.Ordinal);

        await using (var fresh = await TestServer.StartAsync())
        {
            await using var neu = await TestClient.ConnectAsync(fresh, "neu");
            await neu.SendAsync(new RedeemAdminToken(fresh.State.PendingAdminToken!));
            await AuditAsync(fresh, new AuditLine("", "neu", Expected[typeof(RedeemAdminToken)]));
        }

        using var adminId = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(TestServer.Grant(adminId, "Admin"));
        await using var admin = await TestClient.ConnectAsync(server, "chef", adminId);
        var lobby = admin.Welcome.Snapshot.DefaultChannelId;
#pragma warning disable CA2000 // disposed below: they are kicked and banned on the way
        var gast1 = await TestClient.ConnectAsync(server, "gast1");
        var gast2 = await TestClient.ConnectAsync(server, "gast2");
#pragma warning restore CA2000

        await admin.SendAsync(new CreateGroup("Team", Permission.Speak));
        var groups = (await admin.WaitForAsync<GroupsChanged>(changed => changed.Groups.Any(group => group.Name == "Team"))).Groups;
        var team = groups.Single(group => group.Name == "Team").Id;
        await admin.SendAsync(new UpdateGroup(team, "Team 2", Permission.Speak));
        await admin.SendAsync(new ReorderGroups([.. groups.Select(group => group.Id).Reverse()]));
        await admin.SendAsync(new AssignGroup(gast1.Identity.Fingerprint, team));
        await admin.SendAsync(new UnassignGroup(gast1.Identity.Fingerprint, team));
        await admin.SendAsync(new DeleteGroup(team));

        await admin.SendAsync(new CreateChannel("Raid", ""));
        var raid = (await admin.WaitForAsync<ChannelAdded>()).Channel.Id;
        await admin.SendAsync(new EditChannel(raid, "Raid 2", "", 1));
        await admin.SendAsync(new ReorderChannels([raid, lobby]));
        await admin.SendAsync(new MoveUser(gast1.Id, raid));
        await admin.SendAsync(new LinkChannels(lobby, raid));
        await admin.SendAsync(new UnlinkChannels(lobby, raid));
        await admin.SendAsync(new SetChannelLinks([new LinkInfo(lobby, raid)], []));
        await admin.SendAsync(new DeleteChannel(raid));

        await admin.SendAsync(new UpdateServerSettings("Neuer Name", "Hallo", null));
        await admin.SendAsync(new SetServerIcon(Convert.ToBase64String(TestImages.Encode(64, 64))));
        await admin.SendAsync(new SetSessionServerMute(gast1.Id, true));
        await admin.SendAsync(new SetKnownUserServerMute(gast1.Identity.Fingerprint, false));
        await admin.SendAsync(new KickSession(gast1.Id, "Spam"));
        await admin.SendAsync(new BanSession(gast2.Id, "Troll", 60, false));
        await admin.SendAsync(new ListBans { RequestId = "bans" });
        var ban = Assert.Single((await admin.WaitForAsync<BanList>(banList => banList.RequestId == "bans")).Bans);
        await admin.SendAsync(new Unban(ban.Id));
        await admin.SendAsync(new BanKnownUser(gast1.Identity.Fingerprint, "Spam", null, false));
        await admin.SendAsync(new DeleteKnownUser(gast1.Identity.Fingerprint));

        await admin.SendAsync(new CreateBackup { RequestId = "backup" });
        var backup = Assert.Single((await admin.WaitForAsync<BackupList>(backupList => backupList.RequestId == "backup")).Backups).FileName;
        await admin.SendAsync(new DownloadBackup(backup, 0));
        await admin.SendAsync(new DeleteBackup(backup));

        Type[] notDriven = [typeof(RedeemAdminToken), typeof(RestoreBackup), typeof(UploadBackupChunk)];
        await AuditAsync(server, [.. Expected.Where(pair => !notDriven.Contains(pair.Key)).Select(pair => new AuditLine("", "chef", pair.Value))]);
        await gast1.DisposeAsync();
        await gast2.DisposeAsync();
    }

    /// <summary>
    /// Waits until the server log holds an audit line of each wanted actor whose action holds the wanted text (lines are
    /// written under the server lock, the clients may see the change a moment earlier).
    /// </summary>
    static async Task AuditAsync(TestServer server, params AuditLine[] wanted)
    {
        IReadOnlyList<AuditLine> lines = [];
        bool Found(AuditLine line) => lines.Any(written => written.Actor == line.Actor && written.Action.Contains(line.Action, StringComparison.Ordinal));
        var folder = Path.Combine(server.DataDir, "logs", "server");
        for (var deadline = DateTime.UtcNow.AddSeconds(30); DateTime.UtcNow < deadline; await Task.Delay(50))
        {
            lines = [.. Directory.Exists(folder) ? Directory.GetFiles(folder).SelectMany(ReadShared).Select(AuditLine.Parse).OfType<AuditLine>() : []];
            if (wanted.All(Found)) return;
        }
        Assert.Fail("No audit line for: " + string.Join(" | ", wanted.Where(line => !Found(line)).Select(line => $"{line.Actor}: {line.Action}")));
    }

    static string[] ReadShared(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    }
}
