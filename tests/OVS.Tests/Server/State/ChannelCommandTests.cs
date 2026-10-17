using OVS.Shared.Identity;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.Users;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server.State;

/// <summary>A is admin, M is moderator, G is guest.</summary>
public sealed class ChannelCommandTests : IAsyncLifetime
{
    readonly ClientIdentity adminId = ClientIdentity.Create();
    readonly ClientIdentity modId = ClientIdentity.Create();
    TestServer server = null!;
    TestClient admin = null!, moderator = null!, guest = null!;
    Guid Lobby => admin.Welcome.Snapshot.DefaultChannelId;

    public async Task InitializeAsync()
    {
        server = await TestServer.StartAsync(serverData =>
        {
            TestServer.Grant(adminId, "Admin")(serverData);
            TestServer.Grant(modId, "Moderator")(serverData);
        });
        admin = await TestClient.ConnectAsync(server, "admin", adminId);
        moderator = await TestClient.ConnectAsync(server, "mod", modId);
        guest = await TestClient.ConnectAsync(server, "gast");
    }

    public async Task DisposeAsync()
    {
        foreach (var client in new[] { admin, moderator, guest }) await client.DisposeAsync();
        await server.DisposeAsync();
    }

    async Task<ChannelInfo> CreateAsync(string name)
    {
        await admin.SendAsync(new CreateChannel(name, ""));
        return (await admin.WaitForAsync<ChannelAdded>(channelAdded => channelAdded.Channel.Name == name)).Channel;
    }

    [Fact]
    public async Task Join_OtherChannel_BroadcastsUserUpdated()
    {
        var raid = await CreateAsync("Raid");
        await guest.SendAsync(new JoinChannel(raid.Id));
        Assert.Equal(raid.Id, (await admin.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == guest.Id)).User.ChannelId);
        Assert.Equal(raid.Id, (await guest.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == guest.Id)).User.ChannelId);
    }

    /// <summary>Package 54: a new channel gets its options right away, like an edited one.</summary>
    [Fact]
    public async Task Create_WithMutedAndSlots_ChannelHasThem()
    {
        await admin.SendAsync(new CreateChannel("Raid", "", IsMuted: true, MaxUsers: 5));
        var added = (await guest.WaitForAsync<ChannelAdded>()).Channel;
        Assert.Equal((true, 5), (added.IsMuted, added.MaxUsers));
        await using var late = await TestClient.ConnectAsync(server, "spät");
        var known = late.Welcome.Snapshot.Channels.Single(channel => channel.Id == added.Id);
        Assert.Equal((true, 5), (known.IsMuted, known.MaxUsers));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1000)]
    public async Task Create_InvalidSlots_Rejected(int maxUsers)
    {
        await admin.SendAsync(new CreateChannel("Raid", "", MaxUsers: maxUsers) { RequestId = "c" });
        Assert.Equal(ReasonCodes.InvalidValue, (await admin.ErrorAsync("c")).Code);
        await using var late = await TestClient.ConnectAsync(server, "spät");
        Assert.DoesNotContain(late.Welcome.Snapshot.Channels, channel => channel.Name == "Raid");
    }

    [Fact]
    public async Task Create_AsAdmin_BroadcastsAndPersists()
    {
        await admin.SendAsync(new CreateChannel("  Raid  ", "Beschreibung"));
        var added = await guest.WaitForAsync<ChannelAdded>();
        Assert.Equal("Raid", added.Channel.Name);
        Assert.Contains("Raid", await File.ReadAllTextAsync(Path.Combine(server.DataDir, "server-data.json")), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", ReasonCodes.InvalidName)]
    [InlineData("   ", ReasonCodes.InvalidName)]
    [InlineData("12345678901234567890123456789012345678901234567890123456789012345", ReasonCodes.InvalidName)]
    public async Task Create_InvalidName_Error(string name, string code)
    {
        await admin.SendAsync(new CreateChannel(name, "") { RequestId = "c" });
        Assert.Equal(code, (await admin.ErrorAsync("c")).Code);
    }

    /// <summary>Package 110 (A119): channel names may repeat; only the creator's copy names its request.</summary>
    [Fact]
    public async Task Create_SameNameAsExisting_Succeeds()
    {
        await admin.SendAsync(new CreateChannel("lobby", "") { RequestId = "dup" });
        var own = await admin.WaitForAsync<ChannelAdded>();
        var seen = await guest.WaitForAsync<ChannelAdded>();
        Assert.Equal(("lobby", "dup"), (own.Channel.Name, own.RequestId));
        Assert.Null(seen.RequestId);
        Assert.Equal(own.Channel, seen.Channel);
        Assert.NotEqual(Lobby, own.Channel.Id);
    }

    /// <summary>Package 111 (A120): a separator has no name and no options, everyone learns its kind.</summary>
    [Fact]
    public async Task CreateSeparator_EmptyNameNoOptions_BroadcastsKind()
    {
        await admin.SendAsync(new CreateChannel("", "", Kind: ChannelKind.Separator) { RequestId = "s" });
        var own = await admin.WaitForAsync<ChannelAdded>();
        var seen = await guest.WaitForAsync<ChannelAdded>();
        Assert.Equal((ChannelKind.Separator, "", "s"), (own.Channel.Kind, own.Channel.Name, own.RequestId));
        Assert.Equal(own.Channel, seen.Channel);

        await admin.SendAsync(new CreateChannel("Name", "", Kind: ChannelKind.Separator) { RequestId = "n" });
        Assert.Equal(ReasonCodes.InvalidValue, (await admin.ErrorAsync("n")).Code);
        await admin.SendAsync(new CreateChannel("", "", MaxUsers: 3, Kind: ChannelKind.Separator) { RequestId = "o" });
        Assert.Equal(ReasonCodes.InvalidValue, (await admin.ErrorAsync("o")).Code);
        await guest.SendAsync(new CreateChannel("", "", Kind: ChannelKind.Separator) { RequestId = "p" });
        Assert.Equal(ReasonCodes.PermissionDenied, (await guest.ErrorAsync("p")).Code);
    }

    /// <summary>Package 132 (A141): the default (Package 183: 128 kbps) without a value, 16 to 510 (Package 184) kept on create and edit, anything else refused; separators have none.</summary>
    [Fact]
    public async Task Bitrate_CreateAndEdit_DefaultRangeOrRefused()
    {
        Assert.Equal(ProtocolDefaults.Bitrate, (await CreateAsync("Standard")).Bitrate);
        await admin.SendAsync(new CreateChannel("Leise", "", Bitrate: 16));
        Assert.Equal(16, (await guest.WaitForAsync<ChannelAdded>(channelAdded => channelAdded.Channel.Name == "Leise")).Channel.Bitrate);
        await admin.SendAsync(new CreateChannel("Musik", "", Bitrate: 128));
        var music = (await guest.WaitForAsync<ChannelAdded>(channelAdded => channelAdded.Channel.Name == "Musik")).Channel;
        Assert.Equal(128, music.Bitrate);

        await admin.SendAsync(new EditChannel(music.Id, "Musik", "", music.Order, Bitrate: 96));
        Assert.Equal(96, (await guest.WaitForAsync<ChannelUpdated>(channelUpdated => channelUpdated.Channel.Id == music.Id)).Channel.Bitrate);
        await using (var late = await TestClient.ConnectAsync(server, "spät"))
            Assert.Equal(96, late.Welcome.Snapshot.Channels.Single(channel => channel.Id == music.Id).Bitrate);
        var log = Directory.GetFiles(Path.Combine(server.DataDir, "logs", "channels", music.Id.ToString())).Single();
        using (var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            using var reader = new StreamReader(stream);
            Assert.Contains("quality 96 kbps", await reader.ReadToEndAsync(), StringComparison.Ordinal);
        }

        await admin.SendAsync(new CreateChannel("Zu leise", "", Bitrate: 15) { RequestId = "c" });
        Assert.Equal(ReasonCodes.InvalidValue, (await admin.ErrorAsync("c")).Code);
        await admin.SendAsync(new EditChannel(music.Id, "Musik", "", music.Order, Bitrate: 511) { RequestId = "e" });
        Assert.Equal(ReasonCodes.InvalidValue, (await admin.ErrorAsync("e")).Code);
        await admin.SendAsync(new EditChannel(Lobby, "Lobby", "", 0, Bitrate: 0) { RequestId = "z" });
        Assert.Equal(ReasonCodes.InvalidValue, (await admin.ErrorAsync("z")).Code);

        await admin.SendAsync(new CreateChannel("", "", Kind: ChannelKind.Separator));
        Assert.Equal(0, (await guest.WaitForAsync<ChannelAdded>(channelAdded => channelAdded.Channel.Kind == ChannelKind.Separator)).Channel.Bitrate);
        await admin.SendAsync(new CreateChannel("", "", Bitrate: 96, Kind: ChannelKind.Separator) { RequestId = "s" });
        Assert.Equal(ReasonCodes.InvalidValue, (await admin.ErrorAsync("s")).Code);
    }

    [Fact]
    public async Task Separator_JoinMoveEdit_Refused()
    {
        await admin.SendAsync(new CreateChannel("", "", Kind: ChannelKind.Separator));
        var separator = (await admin.WaitForAsync<ChannelAdded>(channelAdded => channelAdded.Channel.Kind == ChannelKind.Separator)).Channel;

        await admin.SendAsync(new JoinChannel(separator.Id) { RequestId = "j" });
        Assert.Equal(ReasonCodes.NotJoinable, (await admin.ErrorAsync("j")).Code); // also for an admin
        await admin.SendAsync(new MoveUser(guest.Id, separator.Id) { RequestId = "m" });
        Assert.Equal(ReasonCodes.NotJoinable, (await admin.ErrorAsync("m")).Code);
        await admin.SendAsync(new EditChannel(separator.Id, "Name", "", separator.Order) { RequestId = "e" });
        Assert.Equal(ReasonCodes.InvalidValue, (await admin.ErrorAsync("e")).Code);

        // it moves and goes like any channel
        await admin.SendAsync(new ReorderChannels([separator.Id, Lobby]));
        Assert.Equal(0, (await guest.WaitForAsync<ChannelUpdated>(channelUpdated => channelUpdated.Channel.Id == separator.Id)).Channel.Order);
        await admin.SendAsync(new DeleteChannel(separator.Id));
        Assert.Equal(separator.Id, (await guest.WaitForAsync<ChannelRemoved>()).ChannelId);
    }

    [Fact]
    public async Task Edit_ToNameOfOther_Succeeds()
    {
        var raid = await CreateAsync("Raid");
        await admin.SendAsync(new EditChannel(raid.Id, "Lobby", "", raid.Order));
        Assert.Equal("Lobby", (await guest.WaitForAsync<ChannelUpdated>()).Channel.Name);
    }

    [Fact]
    public async Task Edit_AsAdmin_BroadcastsChannelUpdated()
    {
        var raid = await CreateAsync("Raid");
        await admin.SendAsync(new EditChannel(raid.Id, "Raid 2", "neu", 5));
        var updated = await guest.WaitForAsync<ChannelUpdated>();
        Assert.Equal(new ChannelInfo(raid.Id, "Raid 2", "neu", raid.Order), updated.Channel); // Package 83: the order stays
    }

    /// <summary>Package 34: the mute flag is stored, logged and in the snapshot of new clients.</summary>
    [Fact]
    public async Task Edit_SetsMuted_PersistsLogsBroadcasts()
    {
        var raid = await CreateAsync("Raid");
        await admin.SendAsync(new EditChannel(raid.Id, "Raid", "", raid.Order, IsMuted: true));
        Assert.True((await guest.WaitForAsync<ChannelUpdated>()).Channel.IsMuted);

        await using var late = await TestClient.ConnectAsync(server, "spaet");
        Assert.True(late.Welcome.Snapshot.Channels.Single(channel => channel.Id == raid.Id).IsMuted);
        Assert.Contains("\"isMuted\": true", await File.ReadAllTextAsync(Path.Combine(server.DataDir, "server-data.json")), StringComparison.Ordinal);
        var log = Directory.GetFiles(Path.Combine(server.DataDir, "logs", "channels", raid.Id.ToString())).Single();
        using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        Assert.Contains("muted", await reader.ReadToEndAsync(), StringComparison.Ordinal);
    }

    // ---- Package 35: slot limit ----

    async Task<ChannelInfo> LimitedAsync(string name, int maxUsers)
    {
        var channel = await CreateAsync(name);
        await admin.SendAsync(new EditChannel(channel.Id, name, "", channel.Order, MaxUsers: maxUsers));
        return (await guest.WaitForAsync<ChannelUpdated>(channelUpdated => channelUpdated.Channel.Id == channel.Id && channelUpdated.Channel.MaxUsers == maxUsers)).Channel;
    }

    async Task JoinAsync(TestClient client, Guid channel)
    {
        await client.SendAsync(new JoinChannel(channel));
        await admin.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == client.Id && userUpdated.User.ChannelId == channel);
    }

    [Fact]
    public async Task Join_FullChannel_RefusedButAdminEnters()
    {
        var raid = await LimitedAsync("Raid", 1);
        await JoinAsync(moderator, raid.Id);
        await guest.SendAsync(new JoinChannel(raid.Id) { RequestId = "voll" });
        Assert.Equal(ReasonCodes.ChannelFull, (await guest.ErrorAsync("voll")).Code);
        await JoinAsync(admin, raid.Id); // the admin has "Volle Channel betreten"
    }

    [Fact]
    public async Task Move_IntoFull_NeedsRightOfMover()
    {
        var raid = await LimitedAsync("Raid", 1);
        await JoinAsync(guest, raid.Id);
        await using var g2 = await TestClient.ConnectAsync(server, "gast2");
        await moderator.SendAsync(new MoveUser(g2.Id, raid.Id) { RequestId = "mv" });
        Assert.Equal(ReasonCodes.ChannelFull, (await moderator.ErrorAsync("mv")).Code);
        await admin.SendAsync(new MoveUser(g2.Id, raid.Id));
        await admin.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == g2.Id && userUpdated.User.ChannelId == raid.Id);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1000)]
    public async Task Edit_LimitOutOfRange_Invalid(int maxUsers)
    {
        var raid = await CreateAsync("Raid");
        await admin.SendAsync(new EditChannel(raid.Id, "Raid", "", raid.Order, MaxUsers: maxUsers) { RequestId = "e" });
        Assert.Equal(ReasonCodes.InvalidValue, (await admin.ErrorAsync("e")).Code);
    }

    [Fact]
    public async Task Edit_DefaultChannelLimit_Invalid()
    {
        await admin.SendAsync(new EditChannel(Lobby, "Lobby", "", 0, MaxUsers: 5) { RequestId = "e" });
        Assert.Contains("default channel", (await admin.ErrorAsync("e")).Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Edit_LimitBelowCount_NobodyRemovedAndPersisted()
    {
        var raid = await CreateAsync("Raid");
        await JoinAsync(guest, raid.Id);
        await JoinAsync(moderator, raid.Id);
        await admin.SendAsync(new EditChannel(raid.Id, "Raid", "", raid.Order, MaxUsers: 1));
        await guest.WaitForAsync<ChannelUpdated>(channelUpdated => channelUpdated.Channel.MaxUsers == 1);

        await using var late = await TestClient.ConnectAsync(server, "spaet");
        Assert.Equal(1, late.Welcome.Snapshot.Channels.Single(channel => channel.Id == raid.Id).MaxUsers);
        Assert.Equal(2, late.Welcome.Snapshot.Users.Count(user => user.ChannelId == raid.Id)); // both stay
        await late.SendAsync(new JoinChannel(raid.Id) { RequestId = "j" });
        Assert.Equal(ReasonCodes.ChannelFull, (await late.ErrorAsync("j")).Code);
    }

    // ---- Package 36: reorder ----

    [Fact]
    public async Task Reorder_SetsOrder_PersistsBroadcasts()
    {
        var raid = await CreateAsync("Raid");
        var afk = await CreateAsync("AFK");
        await admin.SendAsync(new ReorderChannels([afk.Id, Lobby, raid.Id]));
        var afkNow = await guest.WaitForAsync<ChannelUpdated>(channelUpdated => channelUpdated.Channel.Id == afk.Id);
        Assert.Equal(0, afkNow.Channel.Order);

        await using var late = await TestClient.ConnectAsync(server, "spaet");
        Assert.Equal([afk.Id, Lobby, raid.Id], late.Welcome.Snapshot.Channels.OrderBy(channel => channel.Order).Select(channel => channel.Id));
        Assert.Contains(server.Log, line => line.Contains("Channels reordered by admin", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reorder_IncompleteDuplicateUnknown_Invalid()
    {
        var raid = await CreateAsync("Raid");
        foreach (var (ids, id) in new (Guid[], string)[] { ([raid.Id], "fehlt"), ([raid.Id, raid.Id], "doppelt"), ([raid.Id, Guid.NewGuid()], "fremd") })
        {
            await admin.SendAsync(new ReorderChannels(ids) { RequestId = id });
            Assert.Equal(ReasonCodes.InvalidValue, (await admin.ErrorAsync(id)).Code);
        }
    }

    [Fact]
    public async Task Reorder_WithoutRight_Denied()
    {
        await guest.SendAsync(new ReorderChannels([Lobby]) { RequestId = "r" });
        Assert.Equal(ReasonCodes.PermissionDenied, (await guest.ErrorAsync("r")).Code);
    }

    [Fact]
    public async Task EditMuted_WithoutRight_Denied()
    {
        await guest.SendAsync(new EditChannel(Lobby, "Lobby", "", 0, IsMuted: true) { RequestId = "e" });
        Assert.Equal(ReasonCodes.PermissionDenied, (await guest.ErrorAsync("e")).Code);
    }

    [Fact]
    public async Task Edit_DescriptionTooLong_InvalidValue()
    {
        var raid = await CreateAsync("Raid");
        await admin.SendAsync(new EditChannel(raid.Id, "Raid", new string('x', 501), 1) { RequestId = "e" });
        Assert.Equal(ReasonCodes.InvalidValue, (await admin.ErrorAsync("e")).Code);
    }

    [Fact]
    public async Task Delete_OccupiedChannel_UsersMovedToDefaultFirst()
    {
        var raid = await CreateAsync("Raid");
        await guest.SendAsync(new JoinChannel(raid.Id));
        await admin.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == guest.Id && userUpdated.User.ChannelId == raid.Id);

        await admin.SendAsync(new DeleteChannel(raid.Id));
        Assert.Equal(Lobby, (await moderator.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == guest.Id && userUpdated.User.ChannelId == Lobby)).User.ChannelId);
        Assert.Equal(raid.Id, (await moderator.WaitForAsync<ChannelRemoved>()).ChannelId);
    }

    [Fact]
    public async Task Delete_DefaultChannel_Error()
    {
        await admin.SendAsync(new DeleteChannel(Lobby) { RequestId = "d" });
        Assert.Equal(ReasonCodes.CannotDeleteDefault, (await admin.ErrorAsync("d")).Code);
    }

    [Fact]
    public async Task Move_ModeratorMovesGuest_Ok()
    {
        var raid = await CreateAsync("Raid");
        await moderator.SendAsync(new MoveUser(guest.Id, raid.Id));
        Assert.Equal(raid.Id, (await guest.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == guest.Id)).User.ChannelId);
    }

    [Fact]
    public async Task Move_ModeratorMovesAdmin_PermissionDenied()
    {
        var raid = await CreateAsync("Raid");
        await moderator.SendAsync(new MoveUser(admin.Id, raid.Id) { RequestId = "mv" });
        Assert.Equal(ReasonCodes.PermissionDenied, (await moderator.ErrorAsync("mv")).Code);
    }

    [Fact]
    public async Task Move_UnknownSession_NotFound()
    {
        await moderator.SendAsync(new MoveUser(9999, Lobby) { RequestId = "mv" });
        Assert.Equal(ReasonCodes.NotFound, (await moderator.ErrorAsync("mv")).Code);
    }

    public static TheoryData<string> GuestRequests => ["create", "edit", "delete", "move"];

    [Theory]
    [MemberData(nameof(GuestRequests))]
    public async Task GuestCommands_PermissionDenied_NoBroadcast(string kind)
    {
        Request request = kind switch
        {
            "create" => new CreateChannel("Nope", ""),
            "edit" => new EditChannel(Lobby, "Nope", "", 0),
            "delete" => new DeleteChannel(Lobby),
            _ => new MoveUser(admin.Id, Lobby),
        };
        await guest.SendAsync(request with { RequestId = "x" });

        Assert.Equal(ReasonCodes.PermissionDenied, (await guest.ErrorAsync("x")).Code);
        await admin.AssertNoMessageAsync<ChannelAdded>();
        await admin.AssertNoMessageAsync<ChannelUpdated>();
        await admin.AssertNoMessageAsync<ChannelRemoved>();
        await admin.AssertNoMessageAsync<UserUpdated>();
        await admin.AssertNoMessageAsync<Error>();
    }
}
