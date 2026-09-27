using OVS.Shared.Identity;
using OVS.Shared.Protocol;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server;

public sealed class ModerationTests : IAsyncLifetime
{
    readonly ClientIdentity adminId = ClientIdentity.Create();
    readonly ClientIdentity modId = ClientIdentity.Create();
    readonly ClientIdentity guestId = ClientIdentity.Create();
    readonly ManualTimeProvider time = new();
    TestServer server = null!;
    TestClient a = null!, m = null!, g = null!;

    public async Task InitializeAsync()
    {
        server = await TestServer.StartAsync(d =>
        {
            TestServer.Grant(adminId, "Admin")(d);
            TestServer.Grant(modId, "Moderator")(d);
        }, time: time);
        a = await TestClient.ConnectAsync(server, "admin", adminId);
        m = await TestClient.ConnectAsync(server, "mod", modId);
        g = await TestClient.ConnectAsync(server, "gast", guestId);
    }

    public async Task DisposeAsync()
    {
        foreach (var c in new[] { a, m, g }) await c.DisposeAsync();
        await server.DisposeAsync();
    }

    async Task<Message> ReconnectGuestAsync(ClientIdentity? identity = null)
    {
        await using var c = await TestClient.OpenAsync(server.Port, identity ?? guestId);
        return await c.HandshakeAsync("gast2");
    }

    [Fact]
    public async Task Kick_TargetDisconnectedOthersUserLeft()
    {
        var gId = g.Id;
        await m.SendAsync(new Kick(gId, "Spam"));
        Assert.Equal(new Disconnected(Codes.Kicked, "Spam"), await g.WaitForAsync<Disconnected>());
        Assert.True(await g.WaitClosedAsync());
        Assert.Equal(gId, (await a.WaitForAsync<UserLeft>()).SessionId);
    }

    [Fact]
    public async Task Ban_PersistsAndDisconnects_ReconnectRejected()
    {
        await m.SendAsync(new Ban(g.Id, "Beleidigung", 60, false));
        Assert.Equal(Codes.Banned, (await g.WaitForAsync<Disconnected>()).Reason);

        var rejected = Assert.IsType<Rejected>(await ReconnectGuestAsync());
        Assert.Equal(Codes.Banned, rejected.Code);
        Assert.Contains("Beleidigung", rejected.Detail);
        Assert.Contains("bis", rejected.Detail);
    }

    [Fact]
    public async Task IpBan_OtherIdentitySameIp_Rejected()
    {
        await a.SendAsync(new Ban(g.Id, "raus", null, IncludeIp: true));
        await g.WaitForAsync<Disconnected>();
        var rejected = Assert.IsType<Rejected>(await ReconnectGuestAsync(ClientIdentity.Create()));
        Assert.Equal(Codes.Banned, rejected.Code);
        Assert.Contains("dauerhaft", rejected.Detail);
    }

    [Fact]
    public async Task ExpiredBan_HandshakeSucceeds()
    {
        await m.SendAsync(new Ban(g.Id, "kurz", 10, false));
        await g.WaitForAsync<Disconnected>();
        time.Advance(TimeSpan.FromMinutes(11));
        Assert.IsType<Welcome>(await ReconnectGuestAsync());
    }

    [Fact]
    public async Task Unban_AllowsReconnect()
    {
        await m.SendAsync(new Ban(g.Id, "x", null, false));
        await g.WaitForAsync<Disconnected>();

        await m.SendAsync(new ListBans { RequestId = "l" });
        var ban = Assert.Single((await m.WaitForAsync<BanList>(b => b.RequestId == "l")).Bans);
        Assert.Equal(guestId.Fingerprint, ban.Fingerprint);
        Assert.Equal("mod", ban.CreatedBy);

        await m.SendAsync(new Unban(ban.Id) { RequestId = "u" });
        Assert.Empty((await m.WaitForAsync<BanList>(b => b.RequestId == "u")).Bans);
        Assert.IsType<Welcome>(await ReconnectGuestAsync());
    }

    [Fact]
    public async Task ListBans_AsGuest_PermissionDenied()
    {
        await g.SendAsync(new ListBans { RequestId = "l" });
        Assert.Equal(Codes.PermissionDenied, (await g.ErrorAsync("l")).Code);
    }

    [Fact]
    public async Task ServerMute_BroadcastsUserUpdated()
    {
        await m.SendAsync(new SetServerMute(g.Id, true));
        Assert.True((await a.WaitForAsync<UserUpdated>(u => u.User.SessionId == g.Id)).User.ServerMuted);
    }

    [Theory]
    [InlineData("kick")]
    [InlineData("ban")]
    [InlineData("mute")]
    public async Task ModeratorVsAdmin_PermissionDenied(string action)
    {
        Request r = action switch
        {
            "kick" => new Kick(a.Id, "x"),
            "ban" => new Ban(a.Id, "x", null, false),
            _ => new SetServerMute(a.Id, true),
        };
        await m.SendAsync(r with { RequestId = "r" });
        Assert.Equal(Codes.PermissionDenied, (await m.ErrorAsync("r")).Code);
    }

    [Fact]
    public async Task ExpiredBans_RemovedOnNextSave()
    {
        await m.SendAsync(new Ban(g.Id, "kurzweg", 10, false));
        await g.WaitForAsync<Disconnected>();
        time.Advance(TimeSpan.FromMinutes(11));

        await a.SendAsync(new CreateChannel("Irgendwas", "")); // any change that saves
        await a.WaitForAsync<ChannelAdded>();
        Assert.DoesNotContain("kurzweg", File.ReadAllText(Path.Combine(server.DataDir, "server-data.json")));
    }

    [Fact]
    public async Task Unban_AsGuest_PermissionDenied()
    {
        await g.SendAsync(new Unban(Guid.NewGuid()) { RequestId = "u" });
        Assert.Equal(Codes.PermissionDenied, (await g.ErrorAsync("u")).Code);
    }

    [Fact]
    public async Task ServerMute_NotPersisted_GoneAfterReconnect()
    {
        await m.SendAsync(new SetServerMute(g.Id, true));
        await a.WaitForAsync<UserUpdated>(u => u.User.SessionId == g.Id && u.User.ServerMuted);
        await g.DisposeAsync();

        g = await TestClient.ConnectAsync(server, "gast", guestId);
        Assert.False(g.Welcome.Snapshot.Users.Single(u => u.SessionId == g.Id).ServerMuted);
    }

    [Fact]
    public async Task Bans_SurviveRestart()
    {
        await m.SendAsync(new Ban(g.Id, "x", null, false));
        await g.WaitForAsync<Disconnected>();
        await a.DisposeAsync();
        await m.DisposeAsync();

        server = await server.RestartAsync(time);
        a = await TestClient.ConnectAsync(server, "admin", adminId);
        m = await TestClient.ConnectAsync(server, "mod", modId);
        Assert.Equal(Codes.Banned, Assert.IsType<Rejected>(await ReconnectGuestAsync()).Code);
    }
}
