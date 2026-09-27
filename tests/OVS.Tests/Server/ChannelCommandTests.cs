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
