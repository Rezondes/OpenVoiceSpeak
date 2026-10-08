using OVS.Shared.Identity;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Chat;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.Groups;
using OVS.Shared.Protocol.Messages.Links;
using OVS.Shared.Protocol.Messages.LogAccess;
using OVS.Shared.Protocol.Messages.Moderation;
using OVS.Shared.Protocol.Messages.ServerBackups;
using OVS.Shared.Protocol.Messages.ServerInfo;
using OVS.Shared.Protocol.Messages.Users;

namespace OVS.Tests.TestSupport;

/// <summary>One well-formed instance of every request type, with ids and names no server knows (shared by the dispatch and the client log tests).</summary>
internal static class RequestSamples
{
    internal static readonly Guid Unknown = new("90000000-0000-0000-0000-000000000001");
    internal const string NoUser = "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd";
    internal const string NoFile = "2000-01-01_00-00-00.ovsbackup";

    /// <summary>One well-formed instance per request type; a new request type needs an entry here.</summary>
    internal static readonly Dictionary<Type, Func<Request>> Factories = new()
    {
        [typeof(JoinChannel)] = () => new JoinChannel(Unknown),
        [typeof(CreateChannel)] = () => new CreateChannel("Raid", ""),
        [typeof(EditChannel)] = () => new EditChannel(Unknown, "Raid", "", 0),
        [typeof(ReorderChannels)] = () => new ReorderChannels([Unknown]),
        [typeof(DeleteChannel)] = () => new DeleteChannel(Unknown),
        [typeof(MoveUser)] = () => new MoveUser(999, Unknown),
        [typeof(SetSelfState)] = () => new SetSelfState(true, false),
        [typeof(CreateGroup)] = () => new CreateGroup("Team", Permission.Speak),
        [typeof(ReorderGroups)] = () => new ReorderGroups([Unknown]),
        [typeof(UpdateGroup)] = () => new UpdateGroup(Unknown, "Team", Permission.Speak),
        [typeof(DeleteGroup)] = () => new DeleteGroup(Unknown),
        [typeof(AssignGroup)] = () => new AssignGroup(NoUser, WellKnownGroups.Guest),
        [typeof(UnassignGroup)] = () => new UnassignGroup(NoUser, WellKnownGroups.Guest),
        [typeof(ListUsers)] = () => new ListUsers(),
        [typeof(RedeemAdminToken)] = () => new RedeemAdminToken("kein-token"),
        [typeof(UpdateServerSettings)] = () => new UpdateServerSettings("Testserver", "", null),
        [typeof(SetServerIcon)] = () => new SetServerIcon(null),
        [typeof(GetServerIcon)] = () => new GetServerIcon(),
        [typeof(KickSession)] = () => new KickSession(999, "weg"),
        [typeof(BanSession)] = () => new BanSession(999, "weg", 60, false),
        [typeof(Unban)] = () => new Unban(Unknown),
        [typeof(BanKnownUser)] = () => new BanKnownUser(NoUser, "weg", null, false),
        [typeof(DeleteKnownUser)] = () => new DeleteKnownUser(NoUser),
        [typeof(ListBans)] = () => new ListBans(),
        [typeof(SetSessionServerMute)] = () => new SetSessionServerMute(999, true),
        [typeof(SetKnownUserServerMute)] = () => new SetKnownUserServerMute(NoUser, true),
        [typeof(LinkChannels)] = () => new LinkChannels(Unknown, Guid.NewGuid()),
        [typeof(SetChannelLinks)] = () => new SetChannelLinks([], []),
        [typeof(UnlinkChannels)] = () => new UnlinkChannels(Unknown, Guid.NewGuid()),
        [typeof(SendChat)] = () => new SendChat(ChatTarget.Server, null, "hallo"),
        [typeof(ListBackups)] = () => new ListBackups(),
        [typeof(CreateBackup)] = () => new CreateBackup(),
        [typeof(DeleteBackup)] = () => new DeleteBackup(NoFile),
        [typeof(RestoreBackup)] = () => new RestoreBackup(NoFile),
        [typeof(DownloadBackup)] = () => new DownloadBackup(NoFile, 0),
        [typeof(UploadBackupChunk)] = () => new UploadBackupChunk(Guid.NewGuid().ToString("N"), 0, "AAAA", false),
        [typeof(ListLogs)] = () => new ListLogs(),
        [typeof(ReadLog)] = () => new ReadLog("server/none.log"),
        [typeof(SearchLogs)] = () => new SearchLogs("hallo"),
        [typeof(PrepareLogDownload)] = () => new PrepareLogDownload(["server/none.log"]),
        [typeof(DownloadLogChunk)] = () => new DownloadLogChunk(Guid.NewGuid().ToString("N"), 0),
    };
}
