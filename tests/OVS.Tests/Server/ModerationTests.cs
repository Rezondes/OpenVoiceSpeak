using OVS.Server.Permissions;
using OVS.Shared.Identity;
using OVS.Shared.Permissions;
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
        foreach (var c in new[] { a, m, g }.Distinct()) await c.DisposeAsync();
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

    [Theory]
    [InlineData("kick", "Spam\n2026-01-01 00:00:00 admin wurde gebannt")]
    [InlineData("ban", "Spam\r\nfalsch")]
    [InlineData("kick", "Spam\u2028falsch")]
    [InlineData("ban", "\u001b[31mrot")]
    public async Task KickBan_ReasonWithLineBreakOrControl_InvalidValue(string action, string reason)
    {
        Request r = action == "kick" ? new Kick(g.Id, reason) : new Ban(g.Id, reason, null, false);
        await m.SendAsync(r with { RequestId = "r" });
        Assert.Equal(Codes.InvalidValue, (await m.ErrorAsync("r")).Code);

        await g.SendAsync(new ListBans { RequestId = "still-here" }); // the guest is still connected
        Assert.Equal(Codes.PermissionDenied, (await g.ErrorAsync("still-here")).Code);
        await m.SendAsync(new ListBans { RequestId = "l" });
        Assert.Empty((await m.WaitForAsync<BanList>(b => b.RequestId == "l")).Bans);
    }

    [Fact]
    public async Task Ban_ReasonTooLong_InvalidValue_MaxLengthAccepted()
    {
        await m.SendAsync(new Ban(g.Id, new string('x', ProtocolInfo.MaxReasonLength + 1), null, false) { RequestId = "r" });
        Assert.Equal(Codes.InvalidValue, (await m.ErrorAsync("r")).Code);

        await m.SendAsync(new Ban(g.Id, new string('x', ProtocolInfo.MaxReasonLength), null, false));
        Assert.Equal(Codes.Banned, (await g.WaitForAsync<Disconnected>()).Reason);
    }

    [Fact]
    public async Task Kick_EmptyReason_Allowed()
    {
        await m.SendAsync(new Kick(g.Id, ""));
        Assert.Equal(new Disconnected(Codes.Kicked, ""), await g.WaitForAsync<Disconnected>());
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

    // ---- Package 72: ban and delete by fingerprint, also offline ----

    /// <summary>Disconnects the guest and waits until the others saw it leave.</summary>
    async Task GuestOfflineAsync()
    {
        var id = g.Id;
        await g.DisposeAsync();
        await m.WaitForAsync<UserLeft>(l => l.SessionId == id);
        g = m; // DisposeAsync skips duplicates
    }

    async Task<List<BanInfo>> BansAsync(TestClient c)
    {
        await c.SendAsync(new ListBans { RequestId = "bans" });
        return (await c.WaitForAsync<BanList>(b => b.RequestId == "bans")).Bans.ToList();
    }

    async Task<List<KnownUserInfo>> UsersAsync(TestClient c)
    {
        await c.SendAsync(new ListUsers { RequestId = "users" });
        return (await c.WaitForAsync<UserList>(l => l.RequestId == "users")).Users.ToList();
    }

    [Fact]
    public async Task BanUser_Offline_BlocksNextLogin()
    {
        await GuestOfflineAsync();
        await m.SendAsync(new BanUser(guestId.Fingerprint, "weg", null, false));
        var ban = Assert.Single(await BansAsync(m));
        Assert.Equal((guestId.Fingerprint, "gast", "weg", "mod", (string?)null), (ban.Fingerprint, ban.Nickname, ban.Reason, ban.CreatedBy, ban.Ip));

        var rejected = Assert.IsType<Rejected>(await ReconnectGuestAsync());
        Assert.Equal(Codes.Banned, rejected.Code);
        Assert.Contains(server.Log, l => l.Contains("gast wurde von mod gebannt dauerhaft (offline): weg"));
    }

    [Fact]
    public async Task BanUser_OfflineWithIp_UsesLastIp()
    {
        await GuestOfflineAsync();
        await m.SendAsync(new BanUser(guestId.Fingerprint, "", 60, IncludeIp: true));
        Assert.Equal("127.0.0.1", Assert.Single(await BansAsync(m)).Ip);
        Assert.Equal(Codes.Banned, Assert.IsType<Rejected>(await ReconnectGuestAsync(ClientIdentity.Create())).Code); // same IP, other identity
    }

    [Fact]
    public async Task BanUser_StrongerOrSelf_Denied()
    {
        await m.SendAsync(new BanUser(adminId.Fingerprint, "x", null, false) { RequestId = "online" });
        Assert.Equal(Codes.PermissionDenied, (await m.ErrorAsync("online")).Code);
        await m.SendAsync(new BanUser(modId.Fingerprint, "x", null, false) { RequestId = "self" });
        Assert.Equal(Codes.PermissionDenied, (await m.ErrorAsync("self")).Code);
        await m.SendAsync(new BanUser("unbekannt", "x", null, false) { RequestId = "unknown" });
        Assert.Equal(Codes.NotFound, (await m.ErrorAsync("unknown")).Code);
        await g.SendAsync(new BanUser(modId.Fingerprint, "x", null, false) { RequestId = "guest" });
        Assert.Equal(Codes.PermissionDenied, (await g.ErrorAsync("guest")).Code);

        // offline, the rights come from the stored groups
        var adminSession = a.Id;
        await a.DisposeAsync();
        await m.WaitForAsync<UserLeft>(l => l.SessionId == adminSession);
        a = m;
        await m.SendAsync(new BanUser(adminId.Fingerprint, "x", null, false) { RequestId = "offline" });
        Assert.Equal(Codes.PermissionDenied, (await m.ErrorAsync("offline")).Code);
        Assert.Empty(await BansAsync(m));
    }

    [Fact]
    public async Task BanUser_Online_Disconnected()
    {
        await m.SendAsync(new BanUser(guestId.Fingerprint, "raus", 60, false));
        Assert.Equal(new Disconnected(Codes.Banned, "raus"), await g.WaitForAsync<Disconnected>());
        Assert.True(await g.WaitClosedAsync());
        Assert.NotNull(Assert.Single(await BansAsync(m)).ExpiresAt);
    }

    [Fact]
    public async Task DeleteUser_RemovesRecordAndBans_RejoinsAsNew()
    {
        var modGroup = a.Welcome.Snapshot.Groups.Single(x => x.Name == "Moderator").Id;
        await a.SendAsync(new AssignGroup(guestId.Fingerprint, modGroup));
        await a.WaitForAsync<UserUpdated>(u => u.User.SessionId == g.Id);
        await GuestOfflineAsync();
        await a.SendAsync(new BanUser(guestId.Fingerprint, "kurz", 10, false));
        Assert.Single(await BansAsync(a));
        time.Advance(TimeSpan.FromMinutes(11)); // expired, still stored until the next save
        await a.SendAsync(new BanUser(guestId.Fingerprint, "lang", null, true));
        Assert.Single(await BansAsync(a));

        await a.SendAsync(new DeleteUser(guestId.Fingerprint));
        Assert.DoesNotContain(await UsersAsync(a), u => u.Fingerprint == guestId.Fingerprint);
        Assert.Empty(await BansAsync(a));
        Assert.DoesNotContain(guestId.Fingerprint, File.ReadAllText(Path.Combine(server.DataDir, "server-data.json")));
        Assert.Contains(server.Log, l => l.Contains("Nutzerdaten von gast gelöscht von admin"));

        time.Advance(TimeSpan.FromMinutes(5));
        g = await TestClient.ConnectAsync(server, "gast", guestId);
        Assert.Equal(PermissionRules.GuestPermissions, g.Welcome.Snapshot.Users.Single(u => u.SessionId == g.Id).Permissions);
        var again = (await UsersAsync(a)).Single(u => u.Fingerprint == guestId.Fingerprint);
        Assert.Equal((time.GetUtcNow(), 1), (again.FirstSeen, again.LoginCount));
        Assert.Equal([PermissionRules.GuestGroupId], again.GroupIds);
    }

    [Fact]
    public async Task DeleteUser_Online_KickedWithMessage()
    {
        var gId = g.Id;
        await a.SendAsync(new DeleteUser(guestId.Fingerprint));
        Assert.Equal(Codes.UserDeleted, (await g.WaitForAsync<Disconnected>()).Reason);
        Assert.True(await g.WaitClosedAsync());
        Assert.Equal(gId, (await m.WaitForAsync<UserLeft>()).SessionId);
        Assert.DoesNotContain(await UsersAsync(a), u => u.Fingerprint == guestId.Fingerprint); // not saved again on disconnect
    }

    [Fact]
    public async Task DeleteUser_SelfLastAdminStronger_Denied()
    {
        await m.SendAsync(new DeleteUser(guestId.Fingerprint) { RequestId = "noRight" }); // Moderator has no UserDelete
        Assert.Equal(Codes.PermissionDenied, (await m.ErrorAsync("noRight")).Code);
        await a.SendAsync(new DeleteUser(adminId.Fingerprint) { RequestId = "lastAdmin" });
        Assert.Equal(Codes.LastAdmin, (await a.ErrorAsync("lastAdmin")).Code);

        // a deleter without the Admin group: not the stronger moderator, not the last admin, not itself
        var deleter = ClientIdentity.Create();
        await a.SendAsync(new CreateGroup("Loescher", PermissionRules.GuestPermissions | Permission.UserDelete));
        var group = (await a.WaitForAsync<GroupsChanged>()).Groups.Single(x => x.Name == "Loescher").Id;
        await using var d = await TestClient.ConnectAsync(server, "loescher", deleter);
        await a.SendAsync(new AssignGroup(deleter.Fingerprint, group));
        await d.WaitForAsync<UserUpdated>(u => u.User.SessionId == d.Id && u.User.Permissions.HasFlag(Permission.UserDelete));
        await d.SendAsync(new DeleteUser(modId.Fingerprint) { RequestId = "stronger" });
        Assert.Equal(Codes.PermissionDenied, (await d.ErrorAsync("stronger")).Code);
        await d.SendAsync(new DeleteUser(deleter.Fingerprint) { RequestId = "self" });
        Assert.Equal(Codes.PermissionDenied, (await d.ErrorAsync("self")).Code);
        await d.SendAsync(new DeleteUser(guestId.Fingerprint));
        Assert.Equal(Codes.UserDeleted, (await g.WaitForAsync<Disconnected>()).Reason);
    }
}
