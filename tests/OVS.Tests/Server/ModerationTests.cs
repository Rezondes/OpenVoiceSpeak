using System.Net;
using OVS.Server.Data;
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

    /// <summary>Package 88: an IPv6 ban is stored with the exact address and matches the whole /64.</summary>
    [Fact]
    public async Task IpBan_Ipv6_MatchesWholeSlash64()
    {
        var (target, _) = server.State.Admit("fpv6-000000000001", "v6", IPAddress.Parse("2001:db8:7:8::1"), null);
        Assert.NotNull(target);
        await a.SendAsync(new Ban(target.Id, "raus", null, IncludeIp: true));
        await a.WaitForAsync<UserLeft>(u => u.SessionId == target.Id);

        var rejected = server.State.Admit("fpv6-000000000002", "v6b", IPAddress.Parse("2001:db8:7:8:ffff::2"), null);
        Assert.Equal(Codes.Banned, rejected.Rejection?.Code);
        Assert.Null(server.State.Admit("fpv6-000000000003", "v6c", IPAddress.Parse("2001:db8:7:9::1"), null).Rejection);
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
        Assert.Equal("mod", Assert.Single((await m.WaitForAsync<BanList>(b => b.RequestId == "u")).Bans).LiftedBy); // Package 80: kept as history
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
        // Package 84: a ban on the last admin is refused as such before the rank is looked at
        Assert.Equal(action == "ban" ? Codes.LastAdmin : Codes.PermissionDenied, (await m.ErrorAsync("r")).Code);
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
    public async Task Unban_AsGuest_PermissionDenied()
    {
        await g.SendAsync(new Unban(Guid.NewGuid()) { RequestId = "u" });
        Assert.Equal(Codes.PermissionDenied, (await g.ErrorAsync("u")).Code);
    }

    [Fact]
    public async Task ServerMute_Persists_AcrossReconnect()
    {
        // Package 85: stored on the user's record, applied on the next login
        await m.SendAsync(new SetServerMute(g.Id, true));
        await a.WaitForAsync<UserUpdated>(u => u.User.SessionId == g.Id && u.User.ServerMuted);
        Assert.True((await UsersAsync(a)).Single(u => u.Fingerprint == guestId.Fingerprint).ServerMuted);
        Assert.True(StoredUser(guestId.Fingerprint).ServerMuted);
        await g.DisposeAsync();

        g = await TestClient.ConnectAsync(server, "gast", guestId);
        Assert.True(g.Welcome.Snapshot.Users.Single(u => u.SessionId == g.Id).ServerMuted);
        Assert.True((await a.WaitForAsync<UserJoined>(j => j.User.SessionId == g.Id)).User.ServerMuted);

        // lifting is stored the same way
        await m.SendAsync(new SetServerMute(g.Id, false));
        await a.WaitForAsync<UserUpdated>(u => u.User.SessionId == g.Id && !u.User.ServerMuted);
        Assert.False(StoredUser(guestId.Fingerprint).ServerMuted);
        await g.DisposeAsync();
        g = await TestClient.ConnectAsync(server, "gast", guestId);
        Assert.False(g.Welcome.Snapshot.Users.Single(u => u.SessionId == g.Id).ServerMuted);
    }

    [Fact]
    public async Task ServerMute_NoVoiceBeforeWelcome()
    {
        await m.SendAsync(new SetServerMute(g.Id, true));
        await a.WaitForAsync<UserUpdated>(u => u.User.SessionId == g.Id && u.User.ServerMuted);
        await g.DisposeAsync();
        g = await TestClient.ConnectAsync(server, "gast", guestId);
        using var va = new TestVoice(a, server.VoiceEndPoint);
        using var vg = new TestVoice(g, server.VoiceEndPoint);
        await va.HelloAsync();

        // voice right after the Hello of the new session is not relayed: the mute was there before the Welcome
        await vg.HelloAsync();
        await vg.SendAsync(OVS.Shared.Voice.PacketType.Voice, new byte[80], OVS.Shared.Voice.VoiceHeader.TargetChannel);
        Assert.Null(await va.ReceiveVoiceAsync(500));

        await m.SendAsync(new SetServerMute(g.Id, false));
        await a.WaitForAsync<UserUpdated>(u => u.User.SessionId == g.Id && !u.User.ServerMuted);
        await vg.SendAsync(OVS.Shared.Voice.PacketType.Voice, new byte[80], OVS.Shared.Voice.VoiceHeader.TargetChannel);
        Assert.NotNull(await va.ReceiveVoiceAsync());
    }

    [Fact]
    public async Task StoredServerMute_LiftedOffline_RankChecked()
    {
        await m.SendAsync(new SetServerMute(g.Id, true));
        await a.WaitForAsync<UserUpdated>(u => u.User.SessionId == g.Id && u.User.ServerMuted);
        await GuestOfflineAsync();

        await m.SendAsync(new SetStoredServerMute(adminId.Fingerprint, true) { RequestId = "stronger" });
        Assert.Equal(Codes.PermissionDenied, (await m.ErrorAsync("stronger")).Code);
        await m.SendAsync(new SetStoredServerMute(modId.Fingerprint, false) { RequestId = "self" });
        Assert.Equal(Codes.PermissionDenied, (await m.ErrorAsync("self")).Code);
        await m.SendAsync(new SetStoredServerMute("unbekannt", false) { RequestId = "unknown" });
        Assert.Equal(Codes.NotFound, (await m.ErrorAsync("unknown")).Code);

        await m.SendAsync(new SetStoredServerMute(guestId.Fingerprint, false));
        Assert.False((await UsersAsync(a)).Single(u => u.Fingerprint == guestId.Fingerprint).ServerMuted);
        Assert.False(StoredUser(guestId.Fingerprint).ServerMuted);
        Assert.Contains(server.Log, l => l.Contains("gast serverseitig wieder freigegeben von mod (offline)"));
        await using var back = await TestClient.ConnectAsync(server, "gast", guestId);
        Assert.False(back.Welcome.Snapshot.Users.Single(u => u.SessionId == back.Id).ServerMuted);

        // online it acts on the session at once
        await m.SendAsync(new SetStoredServerMute(guestId.Fingerprint, true));
        Assert.True((await a.WaitForAsync<UserUpdated>(u => u.User.SessionId == back.Id)).User.ServerMuted);

        // without UserMute
        await back.SendAsync(new SetStoredServerMute(modId.Fingerprint, true) { RequestId = "noRight" });
        Assert.Equal(Codes.PermissionDenied, (await back.ErrorAsync("noRight")).Code);
    }

    [Fact]
    public async Task DeleteUser_RemovesMute()
    {
        await m.SendAsync(new SetServerMute(g.Id, true));
        await a.WaitForAsync<UserUpdated>(u => u.User.SessionId == g.Id && u.User.ServerMuted);
        Assert.True((await UsersAsync(a)).Single(u => u.Fingerprint == guestId.Fingerprint).ServerMuted);
        await a.SendAsync(new DeleteUser(guestId.Fingerprint));
        await g.WaitForAsync<Disconnected>();

        g = await TestClient.ConnectAsync(server, "gast", guestId);
        Assert.False(g.Welcome.Snapshot.Users.Single(u => u.SessionId == g.Id).ServerMuted);
        Assert.False((await UsersAsync(a)).Single(u => u.Fingerprint == guestId.Fingerprint).ServerMuted);
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
        // Package 84: the stronger user has every right without being in the Admin group (the last admin is refused as such)
        var chef = await ConnectChefAsync();
        await m.SendAsync(new BanUser(chef.Identity.Fingerprint, "x", null, false) { RequestId = "online" });
        Assert.Equal(Codes.PermissionDenied, (await m.ErrorAsync("online")).Code);
        await m.SendAsync(new BanUser(modId.Fingerprint, "x", null, false) { RequestId = "self" });
        Assert.Equal(Codes.PermissionDenied, (await m.ErrorAsync("self")).Code);
        await m.SendAsync(new BanUser("unbekannt", "x", null, false) { RequestId = "unknown" });
        Assert.Equal(Codes.NotFound, (await m.ErrorAsync("unknown")).Code);
        await g.SendAsync(new BanUser(modId.Fingerprint, "x", null, false) { RequestId = "guest" });
        Assert.Equal(Codes.PermissionDenied, (await g.ErrorAsync("guest")).Code);

        // offline, the rights come from the stored groups
        var chefSession = chef.Id;
        await chef.DisposeAsync();
        await m.WaitForAsync<UserLeft>(l => l.SessionId == chefSession);
        await m.SendAsync(new BanUser(chef.Identity.Fingerprint, "x", null, false) { RequestId = "offline" });
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
        time.Advance(TimeSpan.FromMinutes(11)); // expired, kept as history (Package 80)
        await a.SendAsync(new BanUser(guestId.Fingerprint, "lang", null, true));
        Assert.Equal(2, (await BansAsync(a)).Count);

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

    // ---- Package 80: ban details and history (A97) ----

    UserRecord StoredUser(string fingerprint) =>
        new DataStore(Path.Combine(server.DataDir, DataStore.FileName)).LoadOrCreate(() => throw new InvalidOperationException()).Users
            .Single(u => u.Fingerprint == fingerprint);

    List<BanRecord> StoredBans() =>
        new DataStore(Path.Combine(server.DataDir, DataStore.FileName)).LoadOrCreate(() => throw new InvalidOperationException()).Bans;

    [Fact]
    public async Task Ban_StoresCreatedAtCreatorDuration()
    {
        time.Advance(TimeSpan.FromMinutes(3));
        var now = time.GetUtcNow();
        await m.SendAsync(new Ban(g.Id, "laut", 60, IncludeIp: true));
        await g.WaitForAsync<Disconnected>();

        var ban = Assert.Single(await BansAsync(m));
        Assert.Equal((now, modId.Fingerprint, 60, "127.0.0.1", now.AddMinutes(60)),
            (ban.CreatedAt, ban.CreatedByFingerprint, ban.DurationMinutes, ban.Ip, ban.ExpiresAt));
        Assert.Equal((null, null, 0), (ban.LiftedAt, ban.LiftedBy, ban.BlockedAttempts));
        var stored = Assert.Single(StoredBans());
        Assert.Equal((now, modId.Fingerprint, 60), (stored.CreatedAt, stored.CreatedByFingerprint, stored.DurationMinutes));
    }

    [Fact]
    public async Task Unban_KeepsHistory_ExpiredToo_AdmitIgnoresInactive()
    {
        await m.SendAsync(new Ban(g.Id, "weg", null, false));
        await g.WaitForAsync<Disconnected>();
        var lifted = Assert.Single(await BansAsync(m));
        time.Advance(TimeSpan.FromMinutes(1));
        await m.SendAsync(new Unban(lifted.Id) { RequestId = "u" });
        await m.WaitForAsync<BanList>(b => b.RequestId == "u");
        await m.SendAsync(new Unban(lifted.Id) { RequestId = "again" }); // lifting twice is not possible
        Assert.Equal(Codes.NotFound, (await m.ErrorAsync("again")).Code);
        Assert.IsType<Welcome>(await ReconnectGuestAsync());

        await m.SendAsync(new BanUser(guestId.Fingerprint, "kurz", 10, false));
        Assert.Equal(2, (await BansAsync(m)).Count);
        Assert.Equal(Codes.Banned, Assert.IsType<Rejected>(await ReconnectGuestAsync()).Code);
        time.Advance(TimeSpan.FromMinutes(11));
        Assert.IsType<Welcome>(await ReconnectGuestAsync());

        var bans = await BansAsync(m);
        var l = bans.Single(b => b.Id == lifted.Id);
        Assert.Equal(("mod", time.GetUtcNow().AddMinutes(-11)), (l.LiftedBy, l.LiftedAt));
        var expired = bans.Single(b => b.Id != lifted.Id);
        Assert.Equal(("kurz", null), (expired.Reason, expired.LiftedAt));
        Assert.True(expired.ExpiresAt < time.GetUtcNow());
        Assert.Equal(2, StoredBans().Count);
        Assert.Empty((await UsersAsync(a)).Single(u => u.Fingerprint == guestId.Fingerprint).Bans!); // the user card only gets active bans
    }

    [Fact]
    public async Task History_RemovedAfterLogRetention_AndOnUserDelete()
    {
        await a.SendAsync(new UpdateServerSettings("Testserver", "", null, new ServerLimits(50, 1, true, false, new TimeOnly(4, 0))));
        await a.WaitForAsync<ServerSettingsChanged>();
        await m.SendAsync(new Ban(g.Id, "alt", 10, false));
        await g.WaitForAsync<Disconnected>();
        g = m;
        time.Advance(TimeSpan.FromMinutes(11));
        await m.SendAsync(new BanUser(guestId.Fingerprint, "dauer", null, false));
        Assert.Equal(2, (await BansAsync(m)).Count);

        time.Advance(TimeSpan.FromHours(23)); // the expired ban ended 23 h ago: still kept
        await a.SendAsync(new CreateChannel("Speichern1", ""));
        await a.WaitForAsync<ChannelAdded>();
        Assert.Equal(2, StoredBans().Count);

        time.Advance(TimeSpan.FromHours(2)); // now more than one day: gone, the active one stays however old
        await a.SendAsync(new CreateChannel("Speichern2", ""));
        await a.WaitForAsync<ChannelAdded>(c => c.Channel.Name == "Speichern2");
        Assert.Equal("dauer", Assert.Single(StoredBans()).Reason);
        var active = Assert.Single(await BansAsync(m));

        await m.SendAsync(new Unban(active.Id) { RequestId = "u" });
        await m.WaitForAsync<BanList>(b => b.RequestId == "u");
        await a.SendAsync(new DeleteUser(guestId.Fingerprint));
        Assert.DoesNotContain(await UsersAsync(a), u => u.Fingerprint == guestId.Fingerprint);
        Assert.Empty(await BansAsync(a));
        Assert.Empty(StoredBans());
    }

    [Fact]
    public async Task BlockedAttempts_Counted_SavedAtMostPerMinute()
    {
        await m.SendAsync(new Ban(g.Id, "raus", null, false));
        await g.WaitForAsync<Disconnected>();
        for (int i = 0; i < 3; i++)
        {
            if (i > 0) time.Advance(TimeSpan.FromSeconds(5));
            Assert.Equal(Codes.Banned, Assert.IsType<Rejected>(await ReconnectGuestAsync()).Code);
        }
        Assert.Equal(3, Assert.Single(await BansAsync(m)).BlockedAttempts);
        Assert.Equal(1, Assert.Single(StoredBans()).BlockedAttempts); // only the first of these was written

        time.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(Codes.Banned, Assert.IsType<Rejected>(await ReconnectGuestAsync()).Code);
        var ban = Assert.Single(await BansAsync(m));
        Assert.Equal((4, time.GetUtcNow(), "127.0.0.1"), (ban.BlockedAttempts, ban.LastAttempt, ban.LastAttemptIp));
        var stored = Assert.Single(StoredBans());
        Assert.Equal((4, time.GetUtcNow(), "127.0.0.1"), (stored.BlockedAttempts, stored.LastAttempt, stored.LastAttemptIp));
    }

    // ---- Package 84: one rank rule for every action on another user (A102) ----

    Guid GroupId(string name) => a.Welcome.Snapshot.Groups.Single(x => x.Name == name).Id;

    /// <summary>A new client the admin puts into the group.</summary>
    async Task<TestClient> ConnectInGroupAsync(string nickname, Guid groupId)
    {
        var c = await TestClient.ConnectAsync(server, nickname, ClientIdentity.Create());
        await a.SendAsync(new AssignGroup(c.Identity.Fingerprint, groupId));
        await a.WaitForAsync<UserUpdated>(u => u.User.SessionId == c.Id && u.User.GroupIds.Contains(groupId));
        return c;
    }

    /// <summary>A group with every right that is not the Admin group: equal to an admin, never the last admin.</summary>
    async Task<TestClient> ConnectChefAsync()
    {
        await a.SendAsync(new CreateGroup("Chef", Permission.All));
        var chef = (await a.WaitForAsync<GroupsChanged>()).Groups.Single(x => x.Name == "Chef").Id;
        return await ConnectInGroupAsync("chef", chef);
    }

    [Fact]
    public async Task EqualRanks_CannotActOnEachOther()
    {
        await using var m2 = await ConnectInGroupAsync("mod2", GroupId("Moderator"));
        var lobby = a.Welcome.Snapshot.DefaultChannelId;
        Request[] requests =
        [
            new Kick(m2.Id, "x"), new Ban(m2.Id, "x", null, false), new BanUser(m2.Identity.Fingerprint, "x", null, false),
            new SetServerMute(m2.Id, true), new MoveUser(m2.Id, lobby),
        ];
        for (int i = 0; i < requests.Length; i++)
        {
            await m.SendAsync(requests[i] with { RequestId = $"m{i}" });
            Assert.Equal(Codes.PermissionDenied, (await m.ErrorAsync($"m{i}")).Code);
        }

        // two admins neither
        await using var a2 = await ConnectInGroupAsync("admin2", PermissionRules.AdminGroupId);
        Request[] onAdmin =
        [
            new Kick(a.Id, "x"), new SetServerMute(a.Id, true), new MoveUser(a.Id, lobby), new DeleteUser(adminId.Fingerprint),
            new UnassignGroup(adminId.Fingerprint, PermissionRules.AdminGroupId), new AssignGroup(adminId.Fingerprint, GroupId("Moderator")),
            new BanUser(adminId.Fingerprint, "x", null, false),
        ];
        for (int i = 0; i < onAdmin.Length; i++)
        {
            await a2.SendAsync(onAdmin[i] with { RequestId = $"a{i}" });
            Assert.Equal(Codes.PermissionDenied, (await a2.ErrorAsync($"a{i}")).Code);
        }
        Assert.Contains(PermissionRules.AdminGroupId, (await UsersAsync(a)).Single(u => u.Fingerprint == adminId.Fingerprint).GroupIds);
        Assert.Empty(await BansAsync(a));
    }

    [Theory]
    [InlineData("kick")]
    [InlineData("ban")]
    [InlineData("banUser")]
    [InlineData("unmute")]
    [InlineData("move")]
    [InlineData("assign")]
    [InlineData("unassign")]
    [InlineData("delete")]
    public async Task SelfTargeting_Denied(string action)
    {
        var lobby = a.Welcome.Snapshot.DefaultChannelId;
        if (action == "unmute")
        {
            await a.SendAsync(new SetServerMute(m.Id, true));
            await a.WaitForAsync<UserUpdated>(u => u.User.SessionId == m.Id && u.User.ServerMuted);
        }
        // the moderator for its own rights, a user with every right (not the Admin group) for group changes and delete
        var actor = action is "assign" or "unassign" or "delete" ? await ConnectChefAsync() : m;
        var fingerprint = actor.Identity.Fingerprint;
        var groupsBefore = (await UsersAsync(a)).Single(u => u.Fingerprint == fingerprint).GroupIds;
        Request r = action switch
        {
            "kick" => new Kick(actor.Id, "x"),
            "ban" => new Ban(actor.Id, "x", null, false),
            "banUser" => new BanUser(fingerprint, "x", null, false),
            "unmute" => new SetServerMute(actor.Id, false),
            "move" => new MoveUser(actor.Id, lobby),
            "assign" => new AssignGroup(fingerprint, GroupId("Moderator")),
            "unassign" => new UnassignGroup(fingerprint, groupsBefore[^1]),
            _ => new DeleteUser(fingerprint),
        };
        await actor.SendAsync(r with { RequestId = "self" });
        Assert.Equal(Codes.PermissionDenied, (await actor.ErrorAsync("self")).Code);
        await a.AssertNoMessageAsync<UserUpdated>(); // still muted, not moved, no new rights
        Assert.Equal(groupsBefore, (await UsersAsync(a)).Single(u => u.Fingerprint == fingerprint).GroupIds);
        Assert.Empty(await BansAsync(a));
        if (actor != m) await actor.DisposeAsync();
    }

    [Fact]
    public async Task LastAdmin_CannotBeBanned()
    {
        await a.SendAsync(new Ban(a.Id, "x", null, false) { RequestId = "self" });
        Assert.Equal(Codes.LastAdmin, (await a.ErrorAsync("self")).Code);
        await a.SendAsync(new BanUser(adminId.Fingerprint, "x", null, false) { RequestId = "selfUser" });
        Assert.Equal(Codes.LastAdmin, (await a.ErrorAsync("selfUser")).Code);
        await m.SendAsync(new Ban(a.Id, "x", null, false) { RequestId = "online" });
        Assert.Equal(Codes.LastAdmin, (await m.ErrorAsync("online")).Code);

        var adminSession = a.Id;
        await a.DisposeAsync();
        await m.WaitForAsync<UserLeft>(l => l.SessionId == adminSession);
        a = m;
        await m.SendAsync(new BanUser(adminId.Fingerprint, "x", null, true) { RequestId = "offline" });
        Assert.Equal(Codes.LastAdmin, (await m.ErrorAsync("offline")).Code);
        Assert.Empty(await BansAsync(m));
    }

    [Fact]
    public async Task Unban_StrongerBannedUser_Denied_NoBanListWithoutBansView()
    {
        await m.SendAsync(new Ban(g.Id, "x", null, false));
        await g.WaitForAsync<Disconnected>();
        g = m;
        var ban = Assert.Single(await BansAsync(m));

        // the banned guest became a moderator meanwhile: equal to m, who may no longer lift the ban
        await a.SendAsync(new AssignGroup(guestId.Fingerprint, GroupId("Moderator")));
        await UsersAsync(a); // the assignment is done
        await m.SendAsync(new Unban(ban.Id) { RequestId = "equal" });
        Assert.Equal(Codes.PermissionDenied, (await m.ErrorAsync("equal")).Code);
        Assert.Null(Assert.Single(await BansAsync(m)).LiftedAt);
        await a.SendAsync(new UnassignGroup(guestId.Fingerprint, GroupId("Moderator")));

        // with UserBan but without BansView: lifted, but the ban list is not sent
        await a.SendAsync(new CreateGroup("Entbanner", PermissionRules.GuestPermissions | Permission.UserBan));
        var group = (await a.WaitForAsync<GroupsChanged>(c => c.Groups.Any(x => x.Name == "Entbanner"))).Groups.Single(x => x.Name == "Entbanner").Id;
        await using var u = await ConnectInGroupAsync("entbanner", group);
        await u.SendAsync(new Unban(ban.Id) { RequestId = "lift" });
        await u.AssertNoMessageAsync<BanList>();
        await u.AssertNoMessageAsync<Error>();
        Assert.Equal("entbanner", Assert.Single(await BansAsync(m)).LiftedBy);
        Assert.IsType<Welcome>(await ReconnectGuestAsync());
    }
}
