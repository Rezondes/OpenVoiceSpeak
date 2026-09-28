using OVS.Shared.Identity;
using OVS.Shared.Protocol;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server;

/// <summary>A is admin, M is moderator, G is guest.</summary>
public sealed class ChannelCommandTests : IAsyncLifetime
{
    readonly ClientIdentity adminId = ClientIdentity.Create();
    readonly ClientIdentity modId = ClientIdentity.Create();
    TestServer server = null!;
    TestClient a = null!, m = null!, g = null!;
    Guid Lobby => a.Welcome.Snapshot.DefaultChannelId;

    public async Task InitializeAsync()
    {
        server = await TestServer.StartAsync(d =>
        {
            TestServer.Grant(adminId, "Admin")(d);
            TestServer.Grant(modId, "Moderator")(d);
        });
        a = await TestClient.ConnectAsync(server, "admin", adminId);
        m = await TestClient.ConnectAsync(server, "mod", modId);
        g = await TestClient.ConnectAsync(server, "gast");
    }

    public async Task DisposeAsync()
    {
        foreach (var c in new[] { a, m, g }) await c.DisposeAsync();
        await server.DisposeAsync();
    }

    async Task<ChannelInfo> CreateAsync(string name)
    {
        await a.SendAsync(new CreateChannel(name, ""));
        return (await a.WaitForAsync<ChannelAdded>(c => c.Channel.Name == name)).Channel;
    }

    [Fact]
    public async Task Join_BroadcastsUserUpdated()
    {
        var raid = await CreateAsync("Raid");
        await g.SendAsync(new JoinChannel(raid.Id));
        Assert.Equal(raid.Id, (await a.WaitForAsync<UserUpdated>(u => u.User.SessionId == g.Id)).User.ChannelId);
        Assert.Equal(raid.Id, (await g.WaitForAsync<UserUpdated>(u => u.User.SessionId == g.Id)).User.ChannelId);
    }

    [Fact]
    public async Task Create_AsAdmin_BroadcastsAndPersists()
    {
        await a.SendAsync(new CreateChannel("  Raid  ", "Beschreibung"));
        var added = await g.WaitForAsync<ChannelAdded>();
        Assert.Equal("Raid", added.Channel.Name);
        Assert.Contains("Raid", File.ReadAllText(Path.Combine(server.DataDir, "server-data.json")));
    }

    [Theory]
    [InlineData("", Codes.InvalidName)]
    [InlineData("   ", Codes.InvalidName)]
    [InlineData("12345678901234567890123456789012345678901234567890123456789012345", Codes.InvalidName)]
    [InlineData("lobby", Codes.NameTaken)]
    public async Task Create_InvalidOrDuplicateName_Error(string name, string code)
    {
        await a.SendAsync(new CreateChannel(name, "") { RequestId = "c" });
        Assert.Equal(code, (await a.ErrorAsync("c")).Code);
    }

    [Fact]
    public async Task Edit_AsAdmin_BroadcastsChannelUpdated()
    {
        var raid = await CreateAsync("Raid");
        await a.SendAsync(new EditChannel(raid.Id, "Raid 2", "neu", 5));
        var updated = await g.WaitForAsync<ChannelUpdated>();
        Assert.Equal(new ChannelInfo(raid.Id, "Raid 2", "neu", 5), updated.Channel);
    }

    /// <summary>Package 34: the mute flag is stored, logged and in the snapshot of new clients.</summary>
    [Fact]
    public async Task Edit_SetsMuted_PersistsLogsBroadcasts()
    {
        var raid = await CreateAsync("Raid");
        await a.SendAsync(new EditChannel(raid.Id, "Raid", "", raid.Order, IsMuted: true));
        Assert.True((await g.WaitForAsync<ChannelUpdated>()).Channel.IsMuted);

        await using var late = await TestClient.ConnectAsync(server, "spaet");
        Assert.True(late.Welcome.Snapshot.Channels.Single(c => c.Id == raid.Id).IsMuted);
        Assert.Contains("\"isMuted\": true", File.ReadAllText(Path.Combine(server.DataDir, "server-data.json")));
        var log = Directory.GetFiles(Path.Combine(server.DataDir, "logs", "channels", raid.Id.ToString())).Single();
        using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        Assert.Contains("stumm geschaltet", new StreamReader(stream).ReadToEnd());
    }

    // ---- Package 35: slot limit ----

    async Task<ChannelInfo> LimitedAsync(string name, int maxUsers)
    {
        var channel = await CreateAsync(name);
        await a.SendAsync(new EditChannel(channel.Id, name, "", channel.Order, MaxUsers: maxUsers));
        return (await g.WaitForAsync<ChannelUpdated>(u => u.Channel.Id == channel.Id && u.Channel.MaxUsers == maxUsers)).Channel;
    }

    async Task JoinAsync(TestClient client, Guid channel)
    {
        await client.SendAsync(new JoinChannel(channel));
        await a.WaitForAsync<UserUpdated>(u => u.User.SessionId == client.Id && u.User.ChannelId == channel);
    }

    [Fact]
    public async Task Join_Full_ChannelFull_AdminStillEnters()
    {
        var raid = await LimitedAsync("Raid", 1);
        await JoinAsync(m, raid.Id);
        await g.SendAsync(new JoinChannel(raid.Id) { RequestId = "voll" });
        Assert.Equal(Codes.ChannelFull, (await g.ErrorAsync("voll")).Code);
        await JoinAsync(a, raid.Id); // the admin has "Volle Channel betreten"
    }

    [Fact]
    public async Task Move_IntoFull_NeedsRightOfMover()
    {
        var raid = await LimitedAsync("Raid", 1);
        await JoinAsync(g, raid.Id);
        await using var g2 = await TestClient.ConnectAsync(server, "gast2");
        await m.SendAsync(new MoveUser(g2.Id, raid.Id) { RequestId = "mv" });
        Assert.Equal(Codes.ChannelFull, (await m.ErrorAsync("mv")).Code);
        await a.SendAsync(new MoveUser(g2.Id, raid.Id));
        await a.WaitForAsync<UserUpdated>(u => u.User.SessionId == g2.Id && u.User.ChannelId == raid.Id);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1000)]
    public async Task Edit_LimitOutOfRange_Invalid(int maxUsers)
    {
        var raid = await CreateAsync("Raid");
        await a.SendAsync(new EditChannel(raid.Id, "Raid", "", raid.Order, MaxUsers: maxUsers) { RequestId = "e" });
        Assert.Equal(Codes.InvalidValue, (await a.ErrorAsync("e")).Code);
    }

    [Fact]
    public async Task Edit_DefaultChannelLimit_Invalid()
    {
        await a.SendAsync(new EditChannel(Lobby, "Lobby", "", 0, MaxUsers: 5) { RequestId = "e" });
        Assert.Contains("Standard-Channel", (await a.ErrorAsync("e")).Detail);
    }

    [Fact]
    public async Task Edit_LimitBelowCount_NobodyRemoved_PersistedInSnapshot()
    {
        var raid = await CreateAsync("Raid");
        await JoinAsync(g, raid.Id);
        await JoinAsync(m, raid.Id);
        await a.SendAsync(new EditChannel(raid.Id, "Raid", "", raid.Order, MaxUsers: 1));
        await g.WaitForAsync<ChannelUpdated>(u => u.Channel.MaxUsers == 1);

        await using var late = await TestClient.ConnectAsync(server, "spaet");
        Assert.Equal(1, late.Welcome.Snapshot.Channels.Single(c => c.Id == raid.Id).MaxUsers);
        Assert.Equal(2, late.Welcome.Snapshot.Users.Count(u => u.ChannelId == raid.Id)); // both stay
        await late.SendAsync(new JoinChannel(raid.Id) { RequestId = "j" });
        Assert.Equal(Codes.ChannelFull, (await late.ErrorAsync("j")).Code);
    }

    [Fact]
    public async Task Edit_Muted_WithoutRight_Denied()
    {
        await g.SendAsync(new EditChannel(Lobby, "Lobby", "", 0, IsMuted: true) { RequestId = "e" });
        Assert.Equal(Codes.PermissionDenied, (await g.ErrorAsync("e")).Code);
    }

    [Fact]
    public async Task Edit_DescriptionTooLong_InvalidValue()
    {
        var raid = await CreateAsync("Raid");
        await a.SendAsync(new EditChannel(raid.Id, "Raid", new string('x', 501), 1) { RequestId = "e" });
        Assert.Equal(Codes.InvalidValue, (await a.ErrorAsync("e")).Code);
    }

    [Fact]
    public async Task Delete_MovesUsersToDefaultThenRemoves()
    {
        var raid = await CreateAsync("Raid");
        await g.SendAsync(new JoinChannel(raid.Id));
        await a.WaitForAsync<UserUpdated>(u => u.User.SessionId == g.Id && u.User.ChannelId == raid.Id);

        await a.SendAsync(new DeleteChannel(raid.Id));
        Assert.Equal(Lobby, (await m.WaitForAsync<UserUpdated>(u => u.User.SessionId == g.Id && u.User.ChannelId == Lobby)).User.ChannelId);
        Assert.Equal(raid.Id, (await m.WaitForAsync<ChannelRemoved>()).ChannelId);
    }

    [Fact]
    public async Task Delete_DefaultChannel_Error()
    {
        await a.SendAsync(new DeleteChannel(Lobby) { RequestId = "d" });
        Assert.Equal(Codes.CannotDeleteDefault, (await a.ErrorAsync("d")).Code);
    }

    [Fact]
    public async Task Move_ModeratorMovesGuest_Ok()
    {
        var raid = await CreateAsync("Raid");
        await m.SendAsync(new MoveUser(g.Id, raid.Id));
        Assert.Equal(raid.Id, (await g.WaitForAsync<UserUpdated>(u => u.User.SessionId == g.Id)).User.ChannelId);
    }

    [Fact]
    public async Task Move_ModeratorMovesAdmin_PermissionDenied()
    {
        var raid = await CreateAsync("Raid");
        await m.SendAsync(new MoveUser(a.Id, raid.Id) { RequestId = "mv" });
        Assert.Equal(Codes.PermissionDenied, (await m.ErrorAsync("mv")).Code);
    }

    [Fact]
    public async Task Move_UnknownSession_NotFound()
    {
        await m.SendAsync(new MoveUser(9999, Lobby) { RequestId = "mv" });
        Assert.Equal(Codes.NotFound, (await m.ErrorAsync("mv")).Code);
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
            _ => new MoveUser(a.Id, Lobby),
        };
        await g.SendAsync(request with { RequestId = "x" });

        Assert.Equal(Codes.PermissionDenied, (await g.ErrorAsync("x")).Code);
        await a.AssertNoMessageAsync<ChannelAdded>();
        await a.AssertNoMessageAsync<ChannelUpdated>();
        await a.AssertNoMessageAsync<ChannelRemoved>();
        await a.AssertNoMessageAsync<UserUpdated>();
        await a.AssertNoMessageAsync<Error>();
    }
}
