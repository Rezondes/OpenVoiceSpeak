using System.Net;
using OVS.Server.Data;
using OVS.Server.Data.Records;
using OVS.Server.Permissions;
using OVS.Shared.Identity;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.Groups;
using OVS.Shared.Protocol.Messages.Moderation;
using OVS.Shared.Protocol.Messages.ServerInfo;
using OVS.Shared.Protocol.Messages.Users;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server.State;

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
        server = await TestServer.StartAsync(serverData =>
        {
            TestServer.Grant(adminId, "Admin")(serverData);
            TestServer.Grant(modId, "Moderator")(serverData);
        }, time: time);
        a = await TestClient.ConnectAsync(server, "admin", adminId);
        m = await TestClient.ConnectAsync(server, "mod", modId);
        g = await TestClient.ConnectAsync(server, "gast", guestId);
    }

    public async Task DisposeAsync()
    {
        foreach (var client in new[] { a, m, g }.Distinct()) await client.DisposeAsync();
        await server.DisposeAsync();
    }

    async Task<Message> ReconnectGuestAsync(ClientIdentity? identity = null)
    {
        await using var client = await TestClient.OpenAsync(server.Port, identity ?? guestId);
        return await client.HandshakeAsync("gast2");
    }

    [Fact]
    public async Task KickSession_TargetDisconnectedOthersUserLeft()
    {
        var gId = g.Id;
        await m.SendAsync(new KickSession(gId, "Spam"));
        Assert.Equal(new Disconnected(ReasonCodes.Kicked, "Spam"), await g.WaitForAsync<Disconnected>());
        Assert.True(await g.WaitClosedAsync());
        Assert.Equal(gId, (await a.WaitForAsync<UserLeft>()).SessionId);
    }

    [Fact]
    public async Task BanSession_PersistsAndDisconnects_ReconnectRejected()
    {
        await m.SendAsync(new BanSession(g.Id, "Beleidigung", 60, false));
        Assert.Equal(ReasonCodes.Banned, (await g.WaitForAsync<Disconnected>()).Reason);

        var rejected = Assert.IsType<Rejected>(await ReconnectGuestAsync());
        Assert.Equal(ReasonCodes.Banned, rejected.Code);
        Assert.Contains("Beleidigung", rejected.Detail);
        Assert.Contains("bis", rejected.Detail);
    }

    [Fact]
    public async Task IpBan_OtherIdentitySameIp_Rejected()
    {
        await a.SendAsync(new BanSession(g.Id, "raus", null, IncludeIp: true));
        await g.WaitForAsync<Disconnected>();
        var rejected = Assert.IsType<Rejected>(await ReconnectGuestAsync(ClientIdentity.Create()));
        Assert.Equal(ReasonCodes.Banned, rejected.Code);
        Assert.Contains("dauerhaft", rejected.Detail);
    }

    /// <summary>Package 88: an IPv6 ban is stored with the exact address and matches the whole /64.</summary>
    [Fact]
    public async Task IpBan_Ipv6_MatchesWholeSlash64()
    {
        var (target, _) = server.State.Admit("fpv6-000000000001", "v6", IPAddress.Parse("2001:db8:7:8::1"), null);
        Assert.NotNull(target);
        await a.SendAsync(new BanSession(target.Id, "raus", null, IncludeIp: true));
        await a.WaitForAsync<UserLeft>(userLeft => userLeft.SessionId == target.Id);

        var rejected = server.State.Admit("fpv6-000000000002", "v6b", IPAddress.Parse("2001:db8:7:8:ffff::2"), null);
        Assert.Equal(ReasonCodes.Banned, rejected.Rejection?.Code);
        Assert.Null(server.State.Admit("fpv6-000000000003", "v6c", IPAddress.Parse("2001:db8:7:9::1"), null).Rejection);
    }

    [Fact]
    public async Task ExpiredBan_HandshakeSucceeds()
    {
        await m.SendAsync(new BanSession(g.Id, "kurz", 10, false));
        await g.WaitForAsync<Disconnected>();
        time.Advance(TimeSpan.FromMinutes(11));
        Assert.IsType<Welcome>(await ReconnectGuestAsync());
    }

    [Fact]
    public async Task Unban_AllowsReconnect()
    {
        await m.SendAsync(new BanSession(g.Id, "x", null, false));
        await g.WaitForAsync<Disconnected>();

        await m.SendAsync(new ListBans { RequestId = "l" });
        var ban = Assert.Single((await m.WaitForAsync<BanList>(banList => banList.RequestId == "l")).Bans);
        Assert.Equal(guestId.Fingerprint, ban.Fingerprint);
        Assert.Equal("mod", ban.CreatedBy);

        await m.SendAsync(new Unban(ban.Id) { RequestId = "u" });
        Assert.Equal("mod", Assert.Single((await m.WaitForAsync<BanList>(banList => banList.RequestId == "u")).Bans).LiftedBy); // Package 80: kept as history
        Assert.IsType<Welcome>(await ReconnectGuestAsync());
    }

    [Fact]
    public async Task ListBans_AsGuest_PermissionDenied()
    {
        await g.SendAsync(new ListBans { RequestId = "l" });
        Assert.Equal(ReasonCodes.PermissionDenied, (await g.ErrorAsync("l")).Code);
    }

    [Fact]
    public async Task ServerMute_BroadcastsUserUpdated()
    {
        await m.SendAsync(new SetSessionServerMute(g.Id, true));
        Assert.True((await a.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == g.Id)).User.ServerMuted);
    }

    [Theory]
    [InlineData("kick")]
    [InlineData("ban")]
    [InlineData("mute")]
    public async Task ModeratorVsAdmin_PermissionDenied(string action)
    {
        Request request = action switch
        {
            "kick" => new KickSession(a.Id, "x"),
            "ban" => new BanSession(a.Id, "x", null, false),
            _ => new SetSessionServerMute(a.Id, true),
        };
        await m.SendAsync(request with { RequestId = "r" });
        // Package 84: a ban on the last admin is refused as such before the rank is looked at
        Assert.Equal(action == "ban" ? ReasonCodes.LastAdmin : ReasonCodes.PermissionDenied, (await m.ErrorAsync("r")).Code);
    }

    [Theory]
    [InlineData("kick", "Spam\n2026-01-01 00:00:00 admin wurde gebannt")]
    [InlineData("ban", "Spam\r\nfalsch")]
    [InlineData("kick", "Spam\u2028falsch")]
    [InlineData("ban", "\u001b[31mrot")]
    public async Task KickBan_ReasonWithLineBreakOrControl_InvalidValue(string action, string reason)
    {
        Request request = action == "kick" ? new KickSession(g.Id, reason) : new BanSession(g.Id, reason, null, false);
        await m.SendAsync(request with { RequestId = "r" });
        Assert.Equal(ReasonCodes.InvalidValue, (await m.ErrorAsync("r")).Code);

        await g.SendAsync(new ListBans { RequestId = "still-here" }); // the guest is still connected
        Assert.Equal(ReasonCodes.PermissionDenied, (await g.ErrorAsync("still-here")).Code);
        await m.SendAsync(new ListBans { RequestId = "l" });
        Assert.Empty((await m.WaitForAsync<BanList>(banList => banList.RequestId == "l")).Bans);
    }

    [Fact]
    public async Task BanSession_ReasonAtAndOverMaxLength_OnlyOverRefused()
    {
        await m.SendAsync(new BanSession(g.Id, new string('x', ProtocolLimits.MaxReasonLength + 1), null, false) { RequestId = "r" });
        Assert.Equal(ReasonCodes.InvalidValue, (await m.ErrorAsync("r")).Code);

        await m.SendAsync(new BanSession(g.Id, new string('x', ProtocolLimits.MaxReasonLength), null, false));
        Assert.Equal(ReasonCodes.Banned, (await g.WaitForAsync<Disconnected>()).Reason);
    }

    [Fact]
    public async Task KickSession_EmptyReason_Allowed()
    {
        await m.SendAsync(new KickSession(g.Id, ""));
        Assert.Equal(new Disconnected(ReasonCodes.Kicked, ""), await g.WaitForAsync<Disconnected>());
    }

    [Fact]
    public async Task Unban_AsGuest_PermissionDenied()
    {
        await g.SendAsync(new Unban(Guid.NewGuid()) { RequestId = "u" });
        Assert.Equal(ReasonCodes.PermissionDenied, (await g.ErrorAsync("u")).Code);
    }

    [Fact]
    public async Task ServerMute_Persists_AcrossReconnect()
    {
        // Package 85: stored on the user's record, applied on the next login
        await m.SendAsync(new SetSessionServerMute(g.Id, true));
        await a.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == g.Id && userUpdated.User.ServerMuted);
        Assert.True((await UsersAsync(a)).Single(knownUser => knownUser.Fingerprint == guestId.Fingerprint).ServerMuted);
        Assert.True(StoredUser(guestId.Fingerprint).ServerMuted);
        await g.DisposeAsync();

        g = await TestClient.ConnectAsync(server, "gast", guestId);
        Assert.True(g.Welcome.Snapshot.Users.Single(user => user.SessionId == g.Id).ServerMuted);
        Assert.True((await a.WaitForAsync<UserJoined>(userJoined => userJoined.User.SessionId == g.Id)).User.ServerMuted);

        // lifting is stored the same way
        await m.SendAsync(new SetSessionServerMute(g.Id, false));
        await a.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == g.Id && !userUpdated.User.ServerMuted);
        Assert.False(StoredUser(guestId.Fingerprint).ServerMuted);
        await g.DisposeAsync();
        g = await TestClient.ConnectAsync(server, "gast", guestId);
        Assert.False(g.Welcome.Snapshot.Users.Single(user => user.SessionId == g.Id).ServerMuted);
    }

    [Fact]
    public async Task ServerMute_NoVoiceBeforeWelcome()
    {
        await m.SendAsync(new SetSessionServerMute(g.Id, true));
        await a.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == g.Id && userUpdated.User.ServerMuted);
        await g.DisposeAsync();
        g = await TestClient.ConnectAsync(server, "gast", guestId);
        using var va = new TestVoice(a, server.VoiceEndPoint);
        using var vg = new TestVoice(g, server.VoiceEndPoint);
        await va.HelloAsync();

        // voice right after the Hello of the new session is not relayed: the mute was there before the Welcome
        await vg.HelloAsync();
        await vg.SendAsync(OVS.Shared.Voice.PacketType.Voice, new byte[80], OVS.Shared.Voice.VoiceHeader.TargetChannel);
        Assert.Null(await va.ReceiveVoiceAsync(500));

        await m.SendAsync(new SetSessionServerMute(g.Id, false));
        await a.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == g.Id && !userUpdated.User.ServerMuted);
        // resent until heard: a busy runner can drop a datagram even on loopback, and this part is about the mute being lifted
        (OVS.Shared.Voice.VoiceHeader Header, byte[] Plain)? heard = null;
        for (int index = 0; index < 5 && heard is null; index++)
        {
            await vg.SendAsync(OVS.Shared.Voice.PacketType.Voice, new byte[80], OVS.Shared.Voice.VoiceHeader.TargetChannel);
            heard = await va.ReceiveVoiceAsync(5000);
        }
        Assert.NotNull(heard);
    }

    [Fact]
    public async Task SetKnownUserServerMute_LiftedOffline_RankChecked()
    {
        await m.SendAsync(new SetSessionServerMute(g.Id, true));
        await a.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == g.Id && userUpdated.User.ServerMuted);
        await GuestOfflineAsync();

        await m.SendAsync(new SetKnownUserServerMute(adminId.Fingerprint, true) { RequestId = "stronger" });
        Assert.Equal(ReasonCodes.PermissionDenied, (await m.ErrorAsync("stronger")).Code);
        await m.SendAsync(new SetKnownUserServerMute(modId.Fingerprint, false) { RequestId = "self" });
        Assert.Equal(ReasonCodes.PermissionDenied, (await m.ErrorAsync("self")).Code);
        await m.SendAsync(new SetKnownUserServerMute("unbekannt", false) { RequestId = "unknown" });
        Assert.Equal(ReasonCodes.NotFound, (await m.ErrorAsync("unknown")).Code);

        await m.SendAsync(new SetKnownUserServerMute(guestId.Fingerprint, false));
        // the list comes from another session: first make sure m's request is done (m's answers come in order)
        await m.SendAsync(new SetKnownUserServerMute("unbekannt", false) { RequestId = "done" });
        await m.ErrorAsync("done");
        Assert.False((await UsersAsync(a)).Single(knownUser => knownUser.Fingerprint == guestId.Fingerprint).ServerMuted);
        Assert.False(StoredUser(guestId.Fingerprint).ServerMuted);
        Assert.Contains(server.Log, line => line.Contains("gast serverseitig wieder freigegeben von mod (offline)"));
        await using var back = await TestClient.ConnectAsync(server, "gast", guestId);
        Assert.False(back.Welcome.Snapshot.Users.Single(user => user.SessionId == back.Id).ServerMuted);

        // online it acts on the session at once
        await m.SendAsync(new SetKnownUserServerMute(guestId.Fingerprint, true));
        Assert.True((await a.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == back.Id)).User.ServerMuted);

        // without UserMute
        await back.SendAsync(new SetKnownUserServerMute(modId.Fingerprint, true) { RequestId = "noRight" });
        Assert.Equal(ReasonCodes.PermissionDenied, (await back.ErrorAsync("noRight")).Code);
    }

    [Fact]
    public async Task DeleteKnownUser_MutedUser_RemovesMute()
    {
        await m.SendAsync(new SetSessionServerMute(g.Id, true));
        await a.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == g.Id && userUpdated.User.ServerMuted);
        Assert.True((await UsersAsync(a)).Single(knownUser => knownUser.Fingerprint == guestId.Fingerprint).ServerMuted);
        await a.SendAsync(new DeleteKnownUser(guestId.Fingerprint));
        await g.WaitForAsync<Disconnected>();

        g = await TestClient.ConnectAsync(server, "gast", guestId);
        Assert.False(g.Welcome.Snapshot.Users.Single(user => user.SessionId == g.Id).ServerMuted);
        Assert.False((await UsersAsync(a)).Single(knownUser => knownUser.Fingerprint == guestId.Fingerprint).ServerMuted);
    }

    [Fact]
    public async Task Bans_SurviveRestart()
    {
        await m.SendAsync(new BanSession(g.Id, "x", null, false));
        await g.WaitForAsync<Disconnected>();
        await a.DisposeAsync();
        await m.DisposeAsync();

        server = await server.RestartAsync(time);
        a = await TestClient.ConnectAsync(server, "admin", adminId);
        m = await TestClient.ConnectAsync(server, "mod", modId);
        Assert.Equal(ReasonCodes.Banned, Assert.IsType<Rejected>(await ReconnectGuestAsync()).Code);
    }

    // ---- Package 72: ban and delete by fingerprint, also offline ----

    /// <summary>Disconnects the guest and waits until the others saw it leave.</summary>
    async Task GuestOfflineAsync()
    {
        var id = g.Id;
        await g.DisposeAsync();
        await m.WaitForAsync<UserLeft>(userLeft => userLeft.SessionId == id);
        g = m; // DisposeAsync skips duplicates
    }

    async Task<List<BanInfo>> BansAsync(TestClient client)
    {
        await client.SendAsync(new ListBans { RequestId = "bans" });
        return (await client.WaitForAsync<BanList>(banList => banList.RequestId == "bans")).Bans.ToList();
    }

    async Task<List<KnownUserInfo>> UsersAsync(TestClient client)
    {
        await client.SendAsync(new ListUsers { RequestId = "users" });
        return (await client.WaitForAsync<UserList>(userList => userList.RequestId == "users")).Users.ToList();
    }

    [Fact]
    public async Task BanKnownUser_Offline_BlocksNextLogin()
    {
        await GuestOfflineAsync();
        await m.SendAsync(new BanKnownUser(guestId.Fingerprint, "weg", null, false));
        var ban = Assert.Single(await BansAsync(m));
        Assert.Equal((guestId.Fingerprint, "gast", "weg", "mod", (string?)null), (ban.Fingerprint, ban.Nickname, ban.Reason, ban.CreatedBy, ban.Ip));

        var rejected = Assert.IsType<Rejected>(await ReconnectGuestAsync());
        Assert.Equal(ReasonCodes.Banned, rejected.Code);
        Assert.Contains(server.Log, line => line.Contains("gast wurde von mod gebannt dauerhaft (offline): weg"));
    }

    [Fact]
    public async Task BanKnownUser_OfflineWithIp_UsesLastIp()
    {
        await GuestOfflineAsync();
        await m.SendAsync(new BanKnownUser(guestId.Fingerprint, "", 60, IncludeIp: true));
        Assert.Equal("127.0.0.1", Assert.Single(await BansAsync(m)).Ip);
        Assert.Equal(ReasonCodes.Banned, Assert.IsType<Rejected>(await ReconnectGuestAsync(ClientIdentity.Create())).Code); // same IP, other identity
    }

    [Fact]
    public async Task BanKnownUser_StrongerOrSelf_Denied()
    {
        // Package 84: the stronger user has every right without being in the Admin group (the last admin is refused as such)
        var chef = await ConnectChefAsync();
        await m.SendAsync(new BanKnownUser(chef.Identity.Fingerprint, "x", null, false) { RequestId = "online" });
        Assert.Equal(ReasonCodes.PermissionDenied, (await m.ErrorAsync("online")).Code);
        await m.SendAsync(new BanKnownUser(modId.Fingerprint, "x", null, false) { RequestId = "self" });
        Assert.Equal(ReasonCodes.PermissionDenied, (await m.ErrorAsync("self")).Code);
        await m.SendAsync(new BanKnownUser("unbekannt", "x", null, false) { RequestId = "unknown" });
        Assert.Equal(ReasonCodes.NotFound, (await m.ErrorAsync("unknown")).Code);
        await g.SendAsync(new BanKnownUser(modId.Fingerprint, "x", null, false) { RequestId = "guest" });
        Assert.Equal(ReasonCodes.PermissionDenied, (await g.ErrorAsync("guest")).Code);

        // offline, the rights come from the stored groups
        var chefSession = chef.Id;
        await chef.DisposeAsync();
        await m.WaitForAsync<UserLeft>(userLeft => userLeft.SessionId == chefSession);
        await m.SendAsync(new BanKnownUser(chef.Identity.Fingerprint, "x", null, false) { RequestId = "offline" });
        Assert.Equal(ReasonCodes.PermissionDenied, (await m.ErrorAsync("offline")).Code);
        Assert.Empty(await BansAsync(m));
    }

    [Fact]
    public async Task BanKnownUser_Online_Disconnected()
    {
        await m.SendAsync(new BanKnownUser(guestId.Fingerprint, "raus", 60, false));
        Assert.Equal(new Disconnected(ReasonCodes.Banned, "raus"), await g.WaitForAsync<Disconnected>());
        Assert.True(await g.WaitClosedAsync());
        Assert.NotNull(Assert.Single(await BansAsync(m)).ExpiresAt);
    }

    [Fact]
    public async Task DeleteKnownUser_RemovesRecordAndBans_RejoinsAsNew()
    {
        var modGroup = a.Welcome.Snapshot.Groups.Single(groupInfo => groupInfo.Name == "Moderator").Id;
        await a.SendAsync(new AssignGroup(guestId.Fingerprint, modGroup));
        await a.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == g.Id);
        await GuestOfflineAsync();
        await a.SendAsync(new BanKnownUser(guestId.Fingerprint, "kurz", 10, false));
        Assert.Single(await BansAsync(a));
        time.Advance(TimeSpan.FromMinutes(11)); // expired, kept as history (Package 80)
        await a.SendAsync(new BanKnownUser(guestId.Fingerprint, "lang", null, true));
        Assert.Equal(2, (await BansAsync(a)).Count);

        await a.SendAsync(new DeleteKnownUser(guestId.Fingerprint));
        Assert.DoesNotContain(await UsersAsync(a), knownUser => knownUser.Fingerprint == guestId.Fingerprint);
        Assert.Empty(await BansAsync(a));
        Assert.DoesNotContain(guestId.Fingerprint, File.ReadAllText(Path.Combine(server.DataDir, "server-data.json")));
        Assert.Contains(server.Log, line => line.Contains("Nutzerdaten von gast gelöscht von admin"));

        time.Advance(TimeSpan.FromMinutes(5));
        g = await TestClient.ConnectAsync(server, "gast", guestId);
        Assert.Equal(PermissionRules.GuestPermissions, g.Welcome.Snapshot.Users.Single(user => user.SessionId == g.Id).Permissions);
        var again = (await UsersAsync(a)).Single(knownUser => knownUser.Fingerprint == guestId.Fingerprint);
        Assert.Equal((time.GetUtcNow(), 1), (again.FirstSeen, again.LoginCount));
        Assert.Equal([PermissionRules.GuestGroupId], again.GroupIds);
    }

    [Fact]
    public async Task DeleteKnownUser_Online_KickedWithMessage()
    {
        var gId = g.Id;
        await a.SendAsync(new DeleteKnownUser(guestId.Fingerprint));
        Assert.Equal(ReasonCodes.UserDeleted, (await g.WaitForAsync<Disconnected>()).Reason);
        Assert.True(await g.WaitClosedAsync());
        Assert.Equal(gId, (await m.WaitForAsync<UserLeft>()).SessionId);
        Assert.DoesNotContain(await UsersAsync(a), knownUser => knownUser.Fingerprint == guestId.Fingerprint); // not saved again on disconnect
    }

    [Fact]
    public async Task DeleteKnownUser_SelfLastAdminStronger_Denied()
    {
        await m.SendAsync(new DeleteKnownUser(guestId.Fingerprint) { RequestId = "noRight" }); // Moderator has no UserDelete
        Assert.Equal(ReasonCodes.PermissionDenied, (await m.ErrorAsync("noRight")).Code);
        await a.SendAsync(new DeleteKnownUser(adminId.Fingerprint) { RequestId = "lastAdmin" });
        Assert.Equal(ReasonCodes.LastAdmin, (await a.ErrorAsync("lastAdmin")).Code);

        // a deleter without the Admin group: not the stronger moderator, not the last admin, not itself
        var deleter = ClientIdentity.Create();
        await a.SendAsync(new CreateGroup("Loescher", PermissionRules.GuestPermissions | Permission.UserDelete));
        var group = (await a.WaitForAsync<GroupsChanged>()).Groups.Single(groupInfo => groupInfo.Name == "Loescher").Id;
        await using var client = await TestClient.ConnectAsync(server, "loescher", deleter);
        await a.SendAsync(new AssignGroup(deleter.Fingerprint, group));
        await client.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == client.Id && userUpdated.User.Permissions.HasFlag(Permission.UserDelete));
        await client.SendAsync(new DeleteKnownUser(modId.Fingerprint) { RequestId = "stronger" });
        Assert.Equal(ReasonCodes.PermissionDenied, (await client.ErrorAsync("stronger")).Code);
        await client.SendAsync(new DeleteKnownUser(deleter.Fingerprint) { RequestId = "self" });
        Assert.Equal(ReasonCodes.PermissionDenied, (await client.ErrorAsync("self")).Code);
        await client.SendAsync(new DeleteKnownUser(guestId.Fingerprint));
        Assert.Equal(ReasonCodes.UserDeleted, (await g.WaitForAsync<Disconnected>()).Reason);
    }

    // ---- Package 80: ban details and history (A97) ----

    UserRecord StoredUser(string fingerprint) =>
        new DataStore(Path.Combine(server.DataDir, DataStore.FileName)).LoadOrCreate(() => throw new InvalidOperationException()).Users
            .Single(user => user.Fingerprint == fingerprint);

    List<BanRecord> StoredBans() =>
        new DataStore(Path.Combine(server.DataDir, DataStore.FileName)).LoadOrCreate(() => throw new InvalidOperationException()).Bans;

    [Fact]
    public async Task BanSession_Created_StoresCreatedAtCreatorDuration()
    {
        time.Advance(TimeSpan.FromMinutes(3));
        var now = time.GetUtcNow();
        await m.SendAsync(new BanSession(g.Id, "laut", 60, IncludeIp: true));
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
        await m.SendAsync(new BanSession(g.Id, "weg", null, false));
        await g.WaitForAsync<Disconnected>();
        var lifted = Assert.Single(await BansAsync(m));
        time.Advance(TimeSpan.FromMinutes(1));
        await m.SendAsync(new Unban(lifted.Id) { RequestId = "u" });
        await m.WaitForAsync<BanList>(banList => banList.RequestId == "u");
        await m.SendAsync(new Unban(lifted.Id) { RequestId = "again" }); // lifting twice is not possible
        Assert.Equal(ReasonCodes.NotFound, (await m.ErrorAsync("again")).Code);
        Assert.IsType<Welcome>(await ReconnectGuestAsync());

        await m.SendAsync(new BanKnownUser(guestId.Fingerprint, "kurz", 10, false));
        Assert.Equal(2, (await BansAsync(m)).Count);
        Assert.Equal(ReasonCodes.Banned, Assert.IsType<Rejected>(await ReconnectGuestAsync()).Code);
        time.Advance(TimeSpan.FromMinutes(11));
        Assert.IsType<Welcome>(await ReconnectGuestAsync());

        var bans = await BansAsync(m);
        var ban = bans.Single(banInfo => banInfo.Id == lifted.Id);
        Assert.Equal(("mod", time.GetUtcNow().AddMinutes(-11)), (ban.LiftedBy, ban.LiftedAt));
        var expired = bans.Single(banInfo => banInfo.Id != lifted.Id);
        Assert.Equal(("kurz", null), (expired.Reason, expired.LiftedAt));
        Assert.True(expired.ExpiresAt < time.GetUtcNow());
        Assert.Equal(2, StoredBans().Count);
        Assert.Empty((await UsersAsync(a)).Single(knownUser => knownUser.Fingerprint == guestId.Fingerprint).Bans!); // the user card only gets active bans
    }

    [Fact]
    public async Task History_RemovedAfterLogRetention_AndOnUserDelete()
    {
        await a.SendAsync(new UpdateServerSettings("Testserver", "", null, new ServerLimits(50, 1, true, false, new TimeOnly(4, 0))));
        await a.WaitForAsync<ServerSettingsChanged>();
        await m.SendAsync(new BanSession(g.Id, "alt", 10, false));
        await g.WaitForAsync<Disconnected>();
        g = m;
        time.Advance(TimeSpan.FromMinutes(11));
        await m.SendAsync(new BanKnownUser(guestId.Fingerprint, "dauer", null, false));
        Assert.Equal(2, (await BansAsync(m)).Count);

        time.Advance(TimeSpan.FromHours(23)); // the expired ban ended 23 h ago: still kept
        await a.SendAsync(new CreateChannel("Speichern1", ""));
        await a.WaitForAsync<ChannelAdded>();
        Assert.Equal(2, StoredBans().Count);

        time.Advance(TimeSpan.FromHours(2)); // now more than one day: gone, the active one stays however old
        await a.SendAsync(new CreateChannel("Speichern2", ""));
        await a.WaitForAsync<ChannelAdded>(channelAdded => channelAdded.Channel.Name == "Speichern2");
        Assert.Equal("dauer", Assert.Single(StoredBans()).Reason);
        var active = Assert.Single(await BansAsync(m));

        await m.SendAsync(new Unban(active.Id) { RequestId = "u" });
        await m.WaitForAsync<BanList>(banList => banList.RequestId == "u");
        await a.SendAsync(new DeleteKnownUser(guestId.Fingerprint));
        Assert.DoesNotContain(await UsersAsync(a), knownUser => knownUser.Fingerprint == guestId.Fingerprint);
        Assert.Empty(await BansAsync(a));
        Assert.Empty(StoredBans());
    }

    [Fact]
    public async Task BlockedAttempts_Counted_SavedAtMostPerMinute()
    {
        await m.SendAsync(new BanSession(g.Id, "raus", null, false));
        await g.WaitForAsync<Disconnected>();
        for (int index = 0; index < 3; index++)
        {
            if (index > 0) time.Advance(TimeSpan.FromSeconds(5));
            Assert.Equal(ReasonCodes.Banned, Assert.IsType<Rejected>(await ReconnectGuestAsync()).Code);
        }
        Assert.Equal(3, Assert.Single(await BansAsync(m)).BlockedAttempts);
        Assert.Equal(1, Assert.Single(StoredBans()).BlockedAttempts); // only the first of these was written

        time.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(ReasonCodes.Banned, Assert.IsType<Rejected>(await ReconnectGuestAsync()).Code);
        var ban = Assert.Single(await BansAsync(m));
        Assert.Equal((4, time.GetUtcNow(), "127.0.0.1"), (ban.BlockedAttempts, ban.LastAttempt, ban.LastAttemptIp));
        var stored = Assert.Single(StoredBans());
        Assert.Equal((4, time.GetUtcNow(), "127.0.0.1"), (stored.BlockedAttempts, stored.LastAttempt, stored.LastAttemptIp));
    }

    // ---- Package 84: one rank rule for every action on another user (A102) ----

    Guid GroupId(string name) => a.Welcome.Snapshot.Groups.Single(groupInfo => groupInfo.Name == name).Id;

    /// <summary>A new client the admin puts into the group.</summary>
    async Task<TestClient> ConnectInGroupAsync(string nickname, Guid groupId)
    {
        var client = await TestClient.ConnectAsync(server, nickname, ClientIdentity.Create());
        await a.SendAsync(new AssignGroup(client.Identity.Fingerprint, groupId));
        await a.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == client.Id && userUpdated.User.GroupIds.Contains(groupId));
        return client;
    }

    /// <summary>A group with every right that is not the Admin group: equal to an admin, never the last admin.</summary>
    async Task<TestClient> ConnectChefAsync()
    {
        await a.SendAsync(new CreateGroup("Chef", Permission.All));
        var chef = (await a.WaitForAsync<GroupsChanged>()).Groups.Single(groupInfo => groupInfo.Name == "Chef").Id;
        return await ConnectInGroupAsync("chef", chef);
    }

    [Fact]
    public async Task EqualRanks_CannotActOnEachOther()
    {
        await using var m2 = await ConnectInGroupAsync("mod2", GroupId("Moderator"));
        var lobby = a.Welcome.Snapshot.DefaultChannelId;
        Request[] requests =
        [
            new KickSession(m2.Id, "x"), new BanSession(m2.Id, "x", null, false), new BanKnownUser(m2.Identity.Fingerprint, "x", null, false),
            new SetSessionServerMute(m2.Id, true), new MoveUser(m2.Id, lobby),
        ];
        for (int index = 0; index < requests.Length; index++)
        {
            await m.SendAsync(requests[index] with { RequestId = $"m{index}" });
            Assert.Equal(ReasonCodes.PermissionDenied, (await m.ErrorAsync($"m{index}")).Code);
        }

        // two admins neither
        await using var a2 = await ConnectInGroupAsync("admin2", PermissionRules.AdminGroupId);
        Request[] onAdmin =
        [
            new KickSession(a.Id, "x"), new SetSessionServerMute(a.Id, true), new MoveUser(a.Id, lobby), new DeleteKnownUser(adminId.Fingerprint),
            new UnassignGroup(adminId.Fingerprint, PermissionRules.AdminGroupId), new AssignGroup(adminId.Fingerprint, GroupId("Moderator")),
            new BanKnownUser(adminId.Fingerprint, "x", null, false),
        ];
        for (int index = 0; index < onAdmin.Length; index++)
        {
            await a2.SendAsync(onAdmin[index] with { RequestId = $"a{index}" });
            Assert.Equal(ReasonCodes.PermissionDenied, (await a2.ErrorAsync($"a{index}")).Code);
        }
        Assert.Contains(PermissionRules.AdminGroupId, (await UsersAsync(a)).Single(knownUser => knownUser.Fingerprint == adminId.Fingerprint).GroupIds);
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
            await a.SendAsync(new SetSessionServerMute(m.Id, true));
            await a.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == m.Id && userUpdated.User.ServerMuted);
        }
        // the moderator for its own rights, a user with every right (not the Admin group) for group changes and delete
        var actor = action is "assign" or "unassign" or "delete" ? await ConnectChefAsync() : m;
        var fingerprint = actor.Identity.Fingerprint;
        var groupsBefore = (await UsersAsync(a)).Single(knownUser => knownUser.Fingerprint == fingerprint).GroupIds;
        Request request = action switch
        {
            "kick" => new KickSession(actor.Id, "x"),
            "ban" => new BanSession(actor.Id, "x", null, false),
            "banUser" => new BanKnownUser(fingerprint, "x", null, false),
            "unmute" => new SetSessionServerMute(actor.Id, false),
            "move" => new MoveUser(actor.Id, lobby),
            "assign" => new AssignGroup(fingerprint, GroupId("Moderator")),
            "unassign" => new UnassignGroup(fingerprint, groupsBefore[^1]),
            _ => new DeleteKnownUser(fingerprint),
        };
        await actor.SendAsync(request with { RequestId = "self" });
        Assert.Equal(ReasonCodes.PermissionDenied, (await actor.ErrorAsync("self")).Code);
        await a.AssertNoMessageAsync<UserUpdated>(); // still muted, not moved, no new rights
        Assert.Equal(groupsBefore, (await UsersAsync(a)).Single(knownUser => knownUser.Fingerprint == fingerprint).GroupIds);
        Assert.Empty(await BansAsync(a));
        if (actor != m) await actor.DisposeAsync();
    }

    [Fact]
    public async Task LastAdmin_CannotBeBanned()
    {
        await a.SendAsync(new BanSession(a.Id, "x", null, false) { RequestId = "self" });
        Assert.Equal(ReasonCodes.LastAdmin, (await a.ErrorAsync("self")).Code);
        await a.SendAsync(new BanKnownUser(adminId.Fingerprint, "x", null, false) { RequestId = "selfUser" });
        Assert.Equal(ReasonCodes.LastAdmin, (await a.ErrorAsync("selfUser")).Code);
        await m.SendAsync(new BanSession(a.Id, "x", null, false) { RequestId = "online" });
        Assert.Equal(ReasonCodes.LastAdmin, (await m.ErrorAsync("online")).Code);

        var adminSession = a.Id;
        await a.DisposeAsync();
        await m.WaitForAsync<UserLeft>(userLeft => userLeft.SessionId == adminSession);
        a = m;
        await m.SendAsync(new BanKnownUser(adminId.Fingerprint, "x", null, true) { RequestId = "offline" });
        Assert.Equal(ReasonCodes.LastAdmin, (await m.ErrorAsync("offline")).Code);
        Assert.Empty(await BansAsync(m));
    }

    [Fact]
    public async Task Unban_StrongerBannedUser_Denied_NoBanListWithoutBansView()
    {
        await m.SendAsync(new BanSession(g.Id, "x", null, false));
        await g.WaitForAsync<Disconnected>();
        g = m;
        var ban = Assert.Single(await BansAsync(m));

        // the banned guest became a moderator meanwhile: equal to m, who may no longer lift the ban
        await a.SendAsync(new AssignGroup(guestId.Fingerprint, GroupId("Moderator")));
        await UsersAsync(a); // the assignment is done
        await m.SendAsync(new Unban(ban.Id) { RequestId = "equal" });
        Assert.Equal(ReasonCodes.PermissionDenied, (await m.ErrorAsync("equal")).Code);
        Assert.Null(Assert.Single(await BansAsync(m)).LiftedAt);
        await a.SendAsync(new UnassignGroup(guestId.Fingerprint, GroupId("Moderator")));

        // with UserBan but without BansView: lifted, but the ban list is not sent
        await a.SendAsync(new CreateGroup("Entbanner", PermissionRules.GuestPermissions | Permission.UserBan));
        var group = (await a.WaitForAsync<GroupsChanged>(groupsChanged => groupsChanged.Groups.Any(groupInfo => groupInfo.Name == "Entbanner"))).Groups.Single(groupInfo => groupInfo.Name == "Entbanner").Id;
        await using var client = await ConnectInGroupAsync("entbanner", group);
        await client.SendAsync(new Unban(ban.Id) { RequestId = "lift" });
        // a request that surely fails: its answer comes after anything the unban sent back, however slow the server
        await client.SendAsync(new MoveUser(client.Id, Guid.NewGuid()) { RequestId = "probe" });
        while (await client.NextAsync() is { } message and not Error { RequestId: "probe" })
            Assert.False(message is BanList or Error, $"unexpected {message}");
        Assert.Equal("entbanner", Assert.Single(await BansAsync(m)).LiftedBy);
        Assert.IsType<Welcome>(await ReconnectGuestAsync());
    }
}
