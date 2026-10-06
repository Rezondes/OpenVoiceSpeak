using OVS.Server;
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
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server.State;

/// <summary>Every request type of the protocol reaches a handler: none is answered with UnknownRequest.</summary>
public sealed class DispatchTests
{
    static readonly Guid Unknown = new("90000000-0000-0000-0000-000000000001");
    const string NoUser = "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd";
    const string NoFile = "2000-01-01_00-00-00.ovsbackup";

    /// <summary>One well-formed instance per request type; a new request type needs an entry here.</summary>
    static readonly Dictionary<Type, Func<Request>> Factories = new()
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
        [typeof(Kick)] = () => new Kick(999, "weg"),
        [typeof(Ban)] = () => new Ban(999, "weg", 60, false),
        [typeof(Unban)] = () => new Unban(Unknown),
        [typeof(BanUser)] = () => new BanUser(NoUser, "weg", null, false),
        [typeof(DeleteUser)] = () => new DeleteUser(NoUser),
        [typeof(ListBans)] = () => new ListBans(),
        [typeof(SetServerMute)] = () => new SetServerMute(999, true),
        [typeof(SetStoredServerMute)] = () => new SetStoredServerMute(NoUser, true),
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

    static IEnumerable<Type> RequestTypes() =>
        typeof(Request).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(Request)) && !t.IsAbstract).OrderBy(t => t.Name, StringComparer.Ordinal);

    public static TheoryData<string> RequestTypeNames => new(RequestTypes().Select(t => t.Name));

    [Fact]
    public void Factories_EveryRequestType_HasExactlyOneEntry()
    {
        Assert.Equal(RequestTypes().Select(t => t.Name), Factories.Keys.Select(t => t.Name).Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(RequestTypeNames))]
    public void Handle_EveryRequestType_ReachesItsHandler(string typeName)
    {
        var type = RequestTypes().Single(t => t.Name == typeName);
        Assert.True(Factories.TryGetValue(type, out var create), $"No factory entry for {typeName}");
        var request = create() with { RequestId = "r" };
        Assert.Null(RequestShape.Problem(request)); // a malformed request would be answered before the dispatch
        var admin = ClientIdentity.Create();
        using var harness = new StateHarness(TestServer.Grant(admin, "Admin"));
        var session = harness.Admit("admin", admin.Fingerprint); // a fresh session: no request budget used yet
        StateHarness.Drain(session);

        harness.Handle(session, request);

        Assert.DoesNotContain(StateHarness.Drain(session), m => m is Error { Code: Codes.UnknownRequest });
    }
}
