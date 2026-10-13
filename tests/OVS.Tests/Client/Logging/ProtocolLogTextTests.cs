using System.Runtime.CompilerServices;
using OVS.Client.Logging;
using OVS.Client.Net;
using OVS.Shared.Permissions;
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
using OVS.Shared.Protocol.Messages.VoiceRelay;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Logging;

public sealed class ProtocolLogTextTests
{
    static readonly Guid Lobby = Guid.NewGuid(), Raid = Guid.NewGuid(), Gap = Guid.NewGuid(), Mods = Guid.NewGuid();

    static UserInfo User(uint id, Guid channel, string nick, bool muted = false, bool deafened = false, bool serverMuted = false,
        Permission permissions = Permission.Speak, Guid[]? groups = null) =>
        new(id, "fp" + id, nick, channel, muted, deafened, serverMuted, permissions, groups ?? []);

    static StateMirror Mirror() => Snapshots.Mirror(Snapshots.Snapshot(
        "S", "", Lobby,
        [new ChannelInfo(Lobby, "Lobby", "", 0), new ChannelInfo(Raid, "Raid", "", 1), new ChannelInfo(Gap, "", "", 2, Kind: ChannelKind.Separator)],
        [new GroupInfo(Mods, "Moderator", Permission.Speak)],
        [User(1, Lobby, "ich"), User(2, Lobby, "bert")]));

    public static TheoryData<Message, string> Deltas => new()
    {
        { new ChannelAdded(new ChannelInfo(Guid.NewGuid(), "Neu", "", 2)), "Channel 'Neu' created" },
        { new ChannelAdded(new ChannelInfo(Guid.NewGuid(), "", "", 2, Kind: ChannelKind.Separator)), "Separator created" },
        { new ChannelUpdated(new ChannelInfo(Guid.NewGuid(), "", "", 3, Kind: ChannelKind.Separator)), "Separator moved" },
        { new ChannelUpdated(new ChannelInfo(Raid, "Raid 2", "", 1)), "Channel 'Raid' renamed to 'Raid 2'" },
        { new ChannelUpdated(new ChannelInfo(Raid, "Raid", "", 1, IsMuted: true)), "Channel 'Raid' is muted now" },
        { new ChannelUpdated(new ChannelInfo(Raid, "Raid", "neu", 5)), "Channel 'Raid' changed" },
        { new ChannelUpdated(new ChannelInfo(Guid.NewGuid(), "Fremd", "", 5)), "Channel 'Fremd' changed" },
        { new ChannelRemoved(Raid), "Channel 'Raid' deleted" },
        { new ChannelRemoved(Gap), "Separator deleted" },
        { new UserJoined(User(3, Raid, "carla")), "carla connected, channel 'Raid'" },
        { new UserUpdated(User(2, Raid, "bert")), "bert moves from 'Lobby' to 'Raid'" },
        { new UserUpdated(User(2, Lobby, "bert", muted: true)), "bert muted" },
        { new UserUpdated(User(2, Lobby, "bert", deafened: true)), "bert deafened" },
        { new UserUpdated(User(2, Lobby, "bert", serverMuted: true)), "bert muted by the server" },
        { new UserUpdated(User(2, Lobby, "bert", groups: [Mods])), "bert permissions now Speak" },
        { new UserUpdated(User(2, Lobby, "bert", permissions: Permission.None, groups: [Mods])), "bert groups changed" },
        { new UserUpdated(User(2, Lobby, "bert")), "bert updated" },
        { new UserUpdated(User(9, Lobby, "zoe")), "zoe updated" },
        { new UserLeft(2), "bert disconnected" },
        { new ChannelsLinked(Lobby, Raid), "Channels 'Lobby' and 'Raid' linked" },
        { new ChannelsLinked(Lobby, Gap), "Channels 'Lobby' and (separator) linked" },
        { new ChannelsUnlinked(Lobby, Raid), "Link between 'Lobby' and 'Raid' removed" },
        { new GroupsChanged([new GroupInfo(Mods, "Mods", Permission.Speak)]), "Groups changed: Mods" },
        { new ServerSettingsChanged(new ServerSettingsInfo("Neu", "", true)), "Server settings changed: name 'Neu', with password" },
        { new UserList(null, []), "User list received (0 entries)" },
        { new BanList(null, []), "Ban list received (0 entries)" },
        { new ChatMessage(ChatTarget.Server, 2, "bert", null, null, "Hallo", default), "Chat (General) bert: Hallo" },
        { new ChatMessage(ChatTarget.Channel, 2, "bert", Lobby, null, "hier", default), "Chat in 'Lobby' bert: hier" },
        { new ChatMessage(ChatTarget.Private, 2, "bert", null, 1, "psst", default), "Private bert to ich: psst" },
        { new ServerIcon(null, null, null), "Server has no logo" },
        { new ServerIcon(null, "h", new string('A', 136536)), "Server logo received (100 KB)" },
    };

    [Theory]
    [MemberData(nameof(Deltas))]
    public void Describe_ServerMessage_InWordsWithNamesFromTheMirror(Message message, string expected) =>
        Assert.Equal(expected, ProtocolLogText.Describe(message, Mirror()));

    [Fact]
    public void Describe_ErrorMessage_LoggedOnlyWithDetail()
    {
        var mirror = Mirror();
        Assert.Equal("Error InvalidValue (r1): too long", ProtocolLogText.Describe(new Error("r1", "InvalidValue", "too long"), mirror));
        Assert.Null(ProtocolLogText.Describe(new Error("r2", "InvalidValue"), mirror));
    }

    [Fact]
    public void Describe_Request_UsesNames_NeverSecrets()
    {
        var mirror = Mirror();
        Assert.Equal("Request r1: Join channel 'Raid'", ProtocolLogText.Describe(new JoinChannel(Raid) { RequestId = "r1" }, mirror));
        Assert.Equal("Request r2: Move bert to 'Raid'", ProtocolLogText.Describe(new MoveUser(2, Raid) { RequestId = "r2" }, mirror));
        Assert.Equal("Request r3: Assign group 'Moderator' to bert", ProtocolLogText.Describe(new AssignGroup("fp2", Mods) { RequestId = "r3" }, mirror));
        Assert.Equal("Request r4: Redeem admin token", ProtocolLogText.Describe(new RedeemAdminToken("geheim-token") { RequestId = "r4" }, mirror));
        Assert.Equal("Request r5: Change server settings: name 'S', set password",
            ProtocolLogText.Describe(new UpdateServerSettings("S", "", "geheim") { RequestId = "r5" }, mirror));
        Assert.Equal("Request r6: Set server logo", ProtocolLogText.Describe(new SetServerIcon("AAAA") { RequestId = "r6" }, mirror));
        Assert.Equal("Request r7: Remove server logo", ProtocolLogText.Describe(new SetServerIcon(null) { RequestId = "r7" }, mirror));
        Assert.Equal("Request r8: Private message to bert", ProtocolLogText.Describe(new SendChat(ChatTarget.Private, 2, "psst") { RequestId = "r8" }, mirror));
        Assert.Equal("Request r9: Chat in the channel", ProtocolLogText.Describe(new SendChat(ChatTarget.Channel, null, "x") { RequestId = "r9" }, mirror));
    }

    public static TheoryData<Request, string> MoreRequests => new()
    {
        { new DeleteChannel(Raid) { RequestId = "r" }, "Request r: Delete channel 'Raid'" },
        { new DeleteChannel(Gap) { RequestId = "r" }, "Request r: Delete separator" },
        { new UpdateServerSettings("S", "", null) { RequestId = "r" }, "Request r: Change server settings: name 'S'" },
        { new UpdateServerSettings("S", "", "") { RequestId = "r" }, "Request r: Change server settings: name 'S', remove password" },
        { new BanSession(2, "spam", null, true) { RequestId = "r" }, "Request r: Ban bert (permanently, with IP): spam" },
        { new ReorderGroups([Mods, Gap]) { RequestId = "r" }, $"Request r: Reorder groups: 'Moderator', '{Gap}'" },
    };

    [Theory]
    [MemberData(nameof(MoreRequests))]
    public void Describe_OwnRequest_InWordsWithNamesFromTheMirror(Request request, string expected) =>
        Assert.Equal(expected, ProtocolLogText.Describe(request, Mirror()));

    [Fact]
    public void Describe_KnownUserRequest_NamesPersonAndBanTerms()
    {
        var mirror = Mirror();
        Assert.Equal("Request r1: Ban bert (60 minutes, with IP): spam",
            ProtocolLogText.Describe(new BanKnownUser("fp2", "spam", 60, true) { RequestId = "r1" }, mirror));
        Assert.Equal("Request r2: Ban abcdef012345 (permanently): ",
            ProtocolLogText.Describe(new BanKnownUser("abcdef0123456789", "", null, false) { RequestId = "r2" }, mirror));
        Assert.Equal("Request r3: Delete the user data of bert", ProtocolLogText.Describe(new DeleteKnownUser("fp2") { RequestId = "r3" }, mirror));
    }

    [Fact]
    public void Describe_ServerMuteRequest_NamesSessionOrShortFingerprint()
    {
        var mirror = Mirror();
        Assert.Equal("Request r1: Server-mute bert", ProtocolLogText.Describe(new SetSessionServerMute(2, true) { RequestId = "r1" }, mirror));
        Assert.Equal("Request r2: Server-unmute user abcdef012345",
            ProtocolLogText.Describe(new SetKnownUserServerMute("abcdef0123456789", false) { RequestId = "r2" }, mirror));
    }

    /// <summary>Requests logged by their type name only; each one with its reason.</summary>
    static readonly HashSet<Type> TypeNameOnlyRequests =
    [
        typeof(ListBackups), typeof(CreateBackup), typeof(DeleteBackup), typeof(RestoreBackup), typeof(DownloadBackup), // backup administration: the admin page shows its own state
        typeof(UploadBackupChunk), // one line per chunk of a transfer, the type is enough
        typeof(ListLogs), typeof(ReadLog), typeof(SearchLogs), typeof(PrepareLogDownload), typeof(DownloadLogChunk), // log administration and its transfer chunks
    ];

    /// <summary>Server messages logged by their type name only (or never received by the client); each one with its reason.</summary>
    static readonly HashSet<Type> TypeNameOnlyMessages =
    [
        typeof(ClientHello), typeof(Challenge), typeof(ClientProof), typeof(Welcome), typeof(Rejected), // the handshake: the connection logs it in its own words
        typeof(Disconnected), // the connection logs the reason in its own words
        typeof(Ping), typeof(Pong), // keep-alive
        typeof(VoiceUp), typeof(VoiceDown), // voice frames, never logged per frame
        typeof(BackupList), typeof(BackupChunk), typeof(BackupUploaded), typeof(UploadBackupAck), // backup administration and its transfer chunks
        typeof(LogList), typeof(LogPage), typeof(LogSearchResult), typeof(LogDownloadReady), typeof(LogChunk), // log administration and its transfer chunks
    ];

    static IEnumerable<Type> Concrete<T>() => typeof(Message).Assembly.GetTypes().Where(candidate => !candidate.IsAbstract && typeof(T).IsAssignableFrom(candidate));

    public static TheoryData<string> RequestTypeNames => new(Concrete<Request>().Select(candidate => candidate.Name).Order(StringComparer.Ordinal));

    [Theory]
    [MemberData(nameof(RequestTypeNames))]
    public void Describe_EveryRequestType_OwnTextUnlessTypeNameOnly(string typeName)
    {
        var type = Concrete<Request>().Single(candidate => candidate.Name == typeName);
        var text = ProtocolLogText.Describe(RequestSamples.Factories[type]() with { RequestId = "r" }, Mirror());
        if (TypeNameOnlyRequests.Contains(type)) Assert.Equal($"Request r: {typeName}", text);
        else Assert.NotEqual($"Request r: {typeName}", text);
    }

    [Fact]
    public void Describe_EveryServerMessageType_DescribedOrTypeNameOnly()
    {
        var all = Concrete<Message>().Where(candidate => !typeof(Request).IsAssignableFrom(candidate)).ToHashSet();
        var described = Deltas.Select(row => row[0].GetType()).Append(typeof(Error)).ToHashSet(); // Error: Describe_ErrorMessage_LoggedOnlyWithDetail
        Assert.Empty(described.Intersect(TypeNameOnlyMessages));
        Assert.Equal(all.Select(candidate => candidate.Name).Order(StringComparer.Ordinal), described.Union(TypeNameOnlyMessages).Select(candidate => candidate.Name).Order(StringComparer.Ordinal));
        foreach (var type in TypeNameOnlyMessages) // the fallback reads nothing of the message, so an empty instance is enough
            Assert.Equal(type.Name, ProtocolLogText.Describe((Message)RuntimeHelpers.GetUninitializedObject(type), Mirror()));
    }
}
