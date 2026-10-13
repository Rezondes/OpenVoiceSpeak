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
    TestClient admin = null!, moderator = null!, guest = null!;

    public async Task InitializeAsync()
    {
        server = await TestServer.StartAsync(serverData =>
        {
            TestServer.Grant(adminId, "Admin")(serverData);
            TestServer.Grant(modId, "Moderator")(serverData);
        }, time: time);
        admin = await TestClient.ConnectAsync(server, "admin", adminId);
        moderator = await TestClient.ConnectAsync(server, "mod", modId);
        guest = await TestClient.ConnectAsync(server, "gast", guestId);
    }

    public async Task DisposeAsync()
    {
        foreach (var client in new[] { admin, moderator, guest }.Distinct()) await client.DisposeAsync();
        await server.DisposeAsync();
    }

    async Task<Message> ReconnectGuestAsync(ClientIdentity? identity = null)
    {
        await using var client = await TestClient.OpenAsync(server.Port, identity ?? guestId);
        return await client.HandshakeAsync("gast2");
    }

    [Fact]
    public async Task KickSession_Target_DisconnectedAndOthersSeeUserLeft()
    {
        var gId = guest.Id;
        await moderator.SendAsync(new KickSession(gId, "Spam"));
        Assert.Equal(new Disconnected(ReasonCodes.Kicked, "Spam"), await guest.WaitForAsync<Disconnected>());
        Assert.True(await guest.WaitClosedAsync());
        Assert.Equal(gId, (await admin.WaitForAsync<UserLeft>()).SessionId);
    }

    [Fact]
    public async Task BanSession_PersistsAndDisconnects_ReconnectRejected()
    {
        await moderator.SendAsync(new BanSession(guest.Id, "Beleidigung", 60, false));
        Assert.Equal(ReasonCodes.Banned, (await guest.WaitForAsync<Disconnected>()).Reason);

        var rejected = Assert.IsType<Rejected>(await ReconnectGuestAsync());
        Assert.Equal(ReasonCodes.Banned, rejected.Code);
        Assert.Contains("Beleidigung", rejected.Detail);
        Assert.Contains("bis", rejected.Detail);
    }

    [Fact]
    public async Task IpBan_OtherIdentitySameIp_Rejected()
    {
        await admin.SendAsync(new BanSession(guest.Id, "raus", null, IncludeIp: true));
        await guest.WaitForAsync<Disconnected>();
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
        await admin.SendAsync(new BanSession(target.Id, "raus", null, IncludeIp: true));
        await admin.WaitForAsync<UserLeft>(userLeft => userLeft.SessionId == target.Id);

        var rejected = server.State.Admit("fpv6-000000000002", "v6b", IPAddress.Parse("2001:db8:7:8:ffff::2"), null);
        Assert.Equal(ReasonCodes.Banned, rejected.Rejection?.Code);
        Assert.Null(server.State.Admit("fpv6-000000000003", "v6c", IPAddress.Parse("2001:db8:7:9::1"), null).Rejection);
    }

    [Fact]
    public async Task Handshake_BanExpired_Succeeds()
    {
        await moderator.SendAsync(new BanSession(guest.Id, "kurz", 10, false));
        await guest.WaitForAsync<Disconnected>();
        time.Advance(TimeSpan.FromMinutes(11));
        Assert.IsType<Welcome>(await ReconnectGuestAsync());
    }

    [Fact]
    public async Task Unban_BannedUser_CanReconnect()
    {
        await moderator.SendAsync(new BanSession(guest.Id, "x", null, false));
        await guest.WaitForAsync<Disconnected>();

        await moderator.SendAsync(new ListBans { RequestId = "l" });
        var ban = Assert.Single((await moderator.WaitForAsync<BanList>(banList => banList.RequestId == "l")).Bans);
        Assert.Equal(guestId.Fingerprint, ban.Fingerprint);
        Assert.Equal("mod", ban.CreatedBy);

        await moderator.SendAsync(new Unban(ban.Id) { RequestId = "u" });
        Assert.Equal("mod", Assert.Single((await moderator.WaitForAsync<BanList>(banList => banList.RequestId == "u")).Bans).LiftedBy); // Package 80: kept as history
        Assert.IsType<Welcome>(await ReconnectGuestAsync());
    }

    [Fact]
    public async Task ListBans_AsGuest_PermissionDenied()
    {
        await guest.SendAsync(new ListBans { RequestId = "l" });
        Assert.Equal(ReasonCodes.PermissionDenied, (await guest.ErrorAsync("l")).Code);
    }

    [Fact]
    public async Task ServerMute_Set_BroadcastsUserUpdated()
    {
        await moderator.SendAsync(new SetSessionServerMute(guest.Id, true));
        Assert.True((await admin.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == guest.Id)).User.ServerMuted);
    }

    [Theory]
    [InlineData("kick")]
    [InlineData("ban")]
    [InlineData("mute")]
    public async Task Moderate_ModeratorOnAdmin_PermissionDenied(string action)
    {
        Request request = action switch
        {
            "kick" => new KickSession(admin.Id, "x"),
            "ban" => new BanSession(admin.Id, "x", null, false),
            _ => new SetSessionServerMute(admin.Id, true),
        };
        await moderator.SendAsync(request with { RequestId = "r" });
        // Package 84: a ban on the last admin is refused as such before the rank is looked at
        Assert.Equal(action == "ban" ? ReasonCodes.LastAdmin : ReasonCodes.PermissionDenied, (await moderator.ErrorAsync("r")).Code);
    }

    [Theory]
    [InlineData("kick", "Spam\n2026-01-01 00:00:00 admin wurde gebannt")]
    [InlineData("ban", "Spam\r\nfalsch")]
    [InlineData("kick", "Spam\u2028falsch")]
    [InlineData("ban", "\u001b[31mrot")]
    public async Task KickBan_ReasonWithLineBreakOrControl_InvalidValue(string action, string reason)
    {
        Request request = action == "kick" ? new KickSession(guest.Id, reason) : new BanSession(guest.Id, reason, null, false);
        await moderator.SendAsync(request with { RequestId = "r" });
        Assert.Equal(ReasonCodes.InvalidValue, (await moderator.ErrorAsync("r")).Code);

        await guest.SendAsync(new ListBans { RequestId = "still-here" }); // the guest is still connected
        Assert.Equal(ReasonCodes.PermissionDenied, (await guest.ErrorAsync("still-here")).Code);
        await moderator.SendAsync(new ListBans { RequestId = "l" });
        Assert.Empty((await moderator.WaitForAsync<BanList>(banList => banList.RequestId == "l")).Bans);
    }

    [Fact]
    public async Task BanSession_ReasonAtAndOverMaxLength_OnlyOverRefused()
    {
        await moderator.SendAsync(new BanSession(guest.Id, new string('x', ProtocolLimits.MaxReasonLength + 1), null, false) { RequestId = "r" });
        Assert.Equal(ReasonCodes.InvalidValue, (await moderator.ErrorAsync("r")).Code);

        await moderator.SendAsync(new BanSession(guest.Id, new string('x', ProtocolLimits.MaxReasonLength), null, false));
        Assert.Equal(ReasonCodes.Banned, (await guest.WaitForAsync<Disconnected>()).Reason);
    }

    [Fact]
    public async Task KickSession_EmptyReason_Allowed()
    {
        await moderator.SendAsync(new KickSession(guest.Id, ""));
        Assert.Equal(new Disconnected(ReasonCodes.Kicked, ""), await guest.WaitForAsync<Disconnected>());
    }

    [Fact]
    public async Task Unban_AsGuest_PermissionDenied()
    {
        await guest.SendAsync(new Unban(Guid.NewGuid()) { RequestId = "u" });
        Assert.Equal(ReasonCodes.PermissionDenied, (await guest.ErrorAsync("u")).Code);
    }

    [Fact]
    public async Task ServerMute_Persists_AcrossReconnect()
    {
        // Package 85: stored on the user's record, applied on the next login
        await moderator.SendAsync(new SetSessionServerMute(guest.Id, true));
        await admin.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == guest.Id && userUpdated.User.ServerMuted);
        Assert.True((await UsersAsync(admin)).Single(knownUser => knownUser.Fingerprint == guestId.Fingerprint).ServerMuted);
        Assert.True(StoredUser(guestId.Fingerprint).ServerMuted);
        await guest.DisposeAsync();

        guest = await TestClient.ConnectAsync(server, "gast", guestId);
        Assert.True(guest.Welcome.Snapshot.Users.Single(user => user.SessionId == guest.Id).ServerMuted);
        Assert.True((await admin.WaitForAsync<UserJoined>(userJoined => userJoined.User.SessionId == guest.Id)).User.ServerMuted);

        // lifting is stored the same way
        await moderator.SendAsync(new SetSessionServerMute(guest.Id, false));
        await admin.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == guest.Id && !userUpdated.User.ServerMuted);
        Assert.False(StoredUser(guestId.Fingerprint).ServerMuted);
        await guest.DisposeAsync();
        guest = await TestClient.ConnectAsync(server, "gast", guestId);
        Assert.False(guest.Welcome.Snapshot.Users.Single(user => user.SessionId == guest.Id).ServerMuted);
    }

    [Fact]
    public async Task ServerMute_BeforeWelcome_NoVoice()
    {
        await moderator.SendAsync(new SetSessionServerMute(guest.Id, true));
        await admin.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == guest.Id && userUpdated.User.ServerMuted);
        await guest.DisposeAsync();
        guest = await TestClient.ConnectAsync(server, "gast", guestId);
        using var va = new TestVoice(admin, server.VoiceEndPoint);
        using var vg = new TestVoice(guest, server.VoiceEndPoint);
        await va.HelloAsync();

        // voice right after the Hello of the new session is not relayed: the mute was there before the Welcome
        await vg.HelloAsync();
        await vg.SendAsync(OVS.Shared.Voice.PacketType.Voice, new byte[80], OVS.Shared.Voice.VoiceHeader.TargetChannel);
        Assert.Null(await va.ReceiveVoiceAsync(500));

        await moderator.SendAsync(new SetSessionServerMute(guest.Id, false));
        await admin.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == guest.Id && !userUpdated.User.ServerMuted);
        // resent until heard: a busy runner can drop a datagram even on loopback, and this part is about the mute being lifted
        OpenedPacket? heard = null;
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
        await moderator.SendAsync(new SetSessionServerMute(guest.Id, true));
        await admin.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == guest.Id && userUpdated.User.ServerMuted);
        await GuestOfflineAsync();

        await moderator.SendAsync(new SetKnownUserServerMute(adminId.Fingerprint, true) { RequestId = "stronger" });
        Assert.Equal(ReasonCodes.PermissionDenied, (await moderator.ErrorAsync("stronger")).Code);
        await moderator.SendAsync(new SetKnownUserServerMute(modId.Fingerprint, false) { RequestId = "self" });
        Assert.Equal(ReasonCodes.PermissionDenied, (await moderator.ErrorAsync("self")).Code);
        await moderator.SendAsync(new SetKnownUserServerMute("unbekannt", false) { RequestId = "unknown" });
        Assert.Equal(ReasonCodes.NotFound, (await moderator.ErrorAsync("unknown")).Code);

        await moderator.SendAsync(new SetKnownUserServerMute(guestId.Fingerprint, false));
        // the list comes from another session: first make sure m's request is done (m's answers come in order)
        await moderator.SendAsync(new SetKnownUserServerMute("unbekannt", false) { RequestId = "done" });
        await moderator.ErrorAsync("done");
        Assert.False((await UsersAsync(admin)).Single(knownUser => knownUser.Fingerprint == guestId.Fingerprint).ServerMuted);
        Assert.False(StoredUser(guestId.Fingerprint).ServerMuted);
        Assert.Contains(server.Log, line => line.Contains("gast server-unmuted by mod (offline)"));
        await using var back = await TestClient.ConnectAsync(server, "gast", guestId);
        Assert.False(back.Welcome.Snapshot.Users.Single(user => user.SessionId == back.Id).ServerMuted);

        // online it acts on the session at once
        await moderator.SendAsync(new SetKnownUserServerMute(guestId.Fingerprint, true));
        Assert.True((await admin.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == back.Id)).User.ServerMuted);

        // without UserMute
        await back.SendAsync(new SetKnownUserServerMute(modId.Fingerprint, true) { RequestId = "noRight" });
        Assert.Equal(ReasonCodes.PermissionDenied, (await back.ErrorAsync("noRight")).Code);
    }

    [Fact]
    public async Task DeleteKnownUser_MutedUser_RemovesMute()
    {
        await moderator.SendAsync(new SetSessionServerMute(guest.Id, true));
        await admin.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == guest.Id && userUpdated.User.ServerMuted);
        Assert.True((await UsersAsync(admin)).Single(knownUser => knownUser.Fingerprint == guestId.Fingerprint).ServerMuted);
        await admin.SendAsync(new DeleteKnownUser(guestId.Fingerprint));
        await guest.WaitForAsync<Disconnected>();

        guest = await TestClient.ConnectAsync(server, "gast", guestId);
        Assert.False(guest.Welcome.Snapshot.Users.Single(user => user.SessionId == guest.Id).ServerMuted);
        Assert.False((await UsersAsync(admin)).Single(knownUser => knownUser.Fingerprint == guestId.Fingerprint).ServerMuted);
    }

    [Fact]
    public async Task Bans_Restart_Survive()
    {
        await moderator.SendAsync(new BanSession(guest.Id, "x", null, false));
        await guest.WaitForAsync<Disconnected>();
        await admin.DisposeAsync();
        await moderator.DisposeAsync();

        server = await server.RestartAsync(time);
        admin = await TestClient.ConnectAsync(server, "admin", adminId);
        moderator = await TestClient.ConnectAsync(server, "mod", modId);
        Assert.Equal(ReasonCodes.Banned, Assert.IsType<Rejected>(await ReconnectGuestAsync()).Code);
    }

    // ---- Package 72: ban and delete by fingerprint, also offline ----

    /// <summary>Disconnects the guest and waits until the others saw it leave.</summary>
    async Task GuestOfflineAsync()
    {
        var id = guest.Id;
        await guest.DisposeAsync();
        await moderator.WaitForAsync<UserLeft>(userLeft => userLeft.SessionId == id);
        guest = moderator; // DisposeAsync skips duplicates
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
        await moderator.SendAsync(new BanKnownUser(guestId.Fingerprint, "weg", null, false));
        var ban = Assert.Single(await BansAsync(moderator));
        Assert.Equal((guestId.Fingerprint, "gast", "weg", "mod", (string?)null), (ban.Fingerprint, ban.Nickname, ban.Reason, ban.CreatedBy, ban.Ip));

        var rejected = Assert.IsType<Rejected>(await ReconnectGuestAsync());
        Assert.Equal(ReasonCodes.Banned, rejected.Code);
        Assert.Contains(server.Log, line => line.Contains("gast was banned by mod permanently (offline): weg"));
    }

    [Fact]
    public async Task BanKnownUser_OfflineWithIp_UsesLastIp()
    {
        await GuestOfflineAsync();
        await moderator.SendAsync(new BanKnownUser(guestId.Fingerprint, "", 60, IncludeIp: true));
        Assert.Equal("127.0.0.1", Assert.Single(await BansAsync(moderator)).Ip);
        Assert.Equal(ReasonCodes.Banned, Assert.IsType<Rejected>(await ReconnectGuestAsync(ClientIdentity.Create())).Code); // same IP, other identity
    }

    [Fact]
    public async Task BanKnownUser_StrongerOrSelf_Denied()
    {
        // Package 84: the stronger user has every right without being in the Admin group (the last admin is refused as such)
        var chef = await ConnectChefAsync();
        await moderator.SendAsync(new BanKnownUser(chef.Identity.Fingerprint, "x", null, false) { RequestId = "online" });
        Assert.Equal(ReasonCodes.PermissionDenied, (await moderator.ErrorAsync("online")).Code);
        await moderator.SendAsync(new BanKnownUser(modId.Fingerprint, "x", null, false) { RequestId = "self" });
        Assert.Equal(ReasonCodes.PermissionDenied, (await moderator.ErrorAsync("self")).Code);
        await moderator.SendAsync(new BanKnownUser("unbekannt", "x", null, false) { RequestId = "unknown" });
        Assert.Equal(ReasonCodes.NotFound, (await moderator.ErrorAsync("unknown")).Code);
        await guest.SendAsync(new BanKnownUser(modId.Fingerprint, "x", null, false) { RequestId = "guest" });
        Assert.Equal(ReasonCodes.PermissionDenied, (await guest.ErrorAsync("guest")).Code);

        // offline, the rights come from the stored groups
        var chefSession = chef.Id;
        await chef.DisposeAsync();
        await moderator.WaitForAsync<UserLeft>(userLeft => userLeft.SessionId == chefSession);
        await moderator.SendAsync(new BanKnownUser(chef.Identity.Fingerprint, "x", null, false) { RequestId = "offline" });
        Assert.Equal(ReasonCodes.PermissionDenied, (await moderator.ErrorAsync("offline")).Code);
        Assert.Empty(await BansAsync(moderator));
    }

    [Fact]
    public async Task BanKnownUser_Online_Disconnected()
    {
        await moderator.SendAsync(new BanKnownUser(guestId.Fingerprint, "raus", 60, false));
        Assert.Equal(new Disconnected(ReasonCodes.Banned, "raus"), await guest.WaitForAsync<Disconnected>());
        Assert.True(await guest.WaitClosedAsync());
        Assert.NotNull(Assert.Single(await BansAsync(moderator)).ExpiresAt);
    }

    [Fact]
    public async Task DeleteKnownUser_RemovesRecordAndBans_RejoinsAsNew()
    {
        var modGroup = admin.Welcome.Snapshot.Groups.Single(groupInfo => groupInfo.Name == "Moderator").Id;
        await admin.SendAsync(new AssignGroup(guestId.Fingerprint, modGroup));
        await admin.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == guest.Id);
        await GuestOfflineAsync();
        await admin.SendAsync(new BanKnownUser(guestId.Fingerprint, "kurz", 10, false));
        Assert.Single(await BansAsync(admin));
        time.Advance(TimeSpan.FromMinutes(11)); // expired, kept as history (Package 80)
        await admin.SendAsync(new BanKnownUser(guestId.Fingerprint, "lang", null, true));
        Assert.Equal(2, (await BansAsync(admin)).Count);

        await admin.SendAsync(new DeleteKnownUser(guestId.Fingerprint));
        Assert.DoesNotContain(await UsersAsync(admin), knownUser => knownUser.Fingerprint == guestId.Fingerprint);
        Assert.Empty(await BansAsync(admin));
        Assert.DoesNotContain(guestId.Fingerprint, await File.ReadAllTextAsync(Path.Combine(server.DataDir, "server-data.json")));
        Assert.Contains(server.Log, line => line.Contains("User data of gast deleted by admin"));

        time.Advance(TimeSpan.FromMinutes(5));
        guest = await TestClient.ConnectAsync(server, "gast", guestId);
        Assert.Equal(PermissionRules.GuestPermissions, guest.Welcome.Snapshot.Users.Single(user => user.SessionId == guest.Id).Permissions);
        var again = (await UsersAsync(admin)).Single(knownUser => knownUser.Fingerprint == guestId.Fingerprint);
        Assert.Equal((time.GetUtcNow(), 1), (again.FirstSeen, again.LoginCount));
        Assert.Equal([PermissionRules.GuestGroupId], again.GroupIds);
    }

    [Fact]
    public async Task DeleteKnownUser_Online_KickedWithMessage()
    {
        var gId = guest.Id;
        await admin.SendAsync(new DeleteKnownUser(guestId.Fingerprint));
        Assert.Equal(ReasonCodes.UserDeleted, (await guest.WaitForAsync<Disconnected>()).Reason);
        Assert.True(await guest.WaitClosedAsync());
        Assert.Equal(gId, (await moderator.WaitForAsync<UserLeft>()).SessionId);
        Assert.DoesNotContain(await UsersAsync(admin), knownUser => knownUser.Fingerprint == guestId.Fingerprint); // not saved again on disconnect
    }

    [Fact]
    public async Task DeleteKnownUser_SelfLastAdminStronger_Denied()
    {
        await moderator.SendAsync(new DeleteKnownUser(guestId.Fingerprint) { RequestId = "noRight" }); // Moderator has no UserDelete
        Assert.Equal(ReasonCodes.PermissionDenied, (await moderator.ErrorAsync("noRight")).Code);
        await admin.SendAsync(new DeleteKnownUser(adminId.Fingerprint) { RequestId = "lastAdmin" });
        Assert.Equal(ReasonCodes.LastAdmin, (await admin.ErrorAsync("lastAdmin")).Code);

        // a deleter without the Admin group: not the stronger moderator, not the last admin, not itself
        var deleter = ClientIdentity.Create();
        await admin.SendAsync(new CreateGroup("Loescher", PermissionRules.GuestPermissions | Permission.UserDelete));
        var group = (await admin.WaitForAsync<GroupsChanged>()).Groups.Single(groupInfo => groupInfo.Name == "Loescher").Id;
        await using var client = await TestClient.ConnectAsync(server, "loescher", deleter);
        await admin.SendAsync(new AssignGroup(deleter.Fingerprint, group));
        await client.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == client.Id && userUpdated.User.Permissions.HasFlag(Permission.UserDelete));
        await client.SendAsync(new DeleteKnownUser(modId.Fingerprint) { RequestId = "stronger" });
        Assert.Equal(ReasonCodes.PermissionDenied, (await client.ErrorAsync("stronger")).Code);
        await client.SendAsync(new DeleteKnownUser(deleter.Fingerprint) { RequestId = "self" });
        Assert.Equal(ReasonCodes.PermissionDenied, (await client.ErrorAsync("self")).Code);
        await client.SendAsync(new DeleteKnownUser(guestId.Fingerprint));
        Assert.Equal(ReasonCodes.UserDeleted, (await guest.WaitForAsync<Disconnected>()).Reason);
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
        await moderator.SendAsync(new BanSession(guest.Id, "laut", 60, IncludeIp: true));
        await guest.WaitForAsync<Disconnected>();

        var ban = Assert.Single(await BansAsync(moderator));
        Assert.Equal((now, modId.Fingerprint, 60, "127.0.0.1", now.AddMinutes(60)),
            (ban.CreatedAt, ban.CreatedByFingerprint, ban.DurationMinutes, ban.Ip, ban.ExpiresAt));
        Assert.Equal((null, null, 0), (ban.LiftedAt, ban.LiftedBy, ban.BlockedAttempts));
        var stored = Assert.Single(StoredBans());
        Assert.Equal((now, modId.Fingerprint, 60), (stored.CreatedAt, stored.CreatedByFingerprint, stored.DurationMinutes));
    }

    [Fact]
    public async Task Unban_ActiveOrExpired_KeepsHistoryAdmitIgnoresIt()
    {
        await moderator.SendAsync(new BanSession(guest.Id, "weg", null, false));
        await guest.WaitForAsync<Disconnected>();
        var lifted = Assert.Single(await BansAsync(moderator));
        time.Advance(TimeSpan.FromMinutes(1));
        await moderator.SendAsync(new Unban(lifted.Id) { RequestId = "u" });
        await moderator.WaitForAsync<BanList>(banList => banList.RequestId == "u");
        await moderator.SendAsync(new Unban(lifted.Id) { RequestId = "again" }); // lifting twice is not possible
        Assert.Equal(ReasonCodes.NotFound, (await moderator.ErrorAsync("again")).Code);
        Assert.IsType<Welcome>(await ReconnectGuestAsync());

        await moderator.SendAsync(new BanKnownUser(guestId.Fingerprint, "kurz", 10, false));
        Assert.Equal(2, (await BansAsync(moderator)).Count);
        Assert.Equal(ReasonCodes.Banned, Assert.IsType<Rejected>(await ReconnectGuestAsync()).Code);
        time.Advance(TimeSpan.FromMinutes(11));
        Assert.IsType<Welcome>(await ReconnectGuestAsync());

        var bans = await BansAsync(moderator);
        var ban = bans.Single(banInfo => banInfo.Id == lifted.Id);
        Assert.Equal(("mod", time.GetUtcNow().AddMinutes(-11)), (ban.LiftedBy, ban.LiftedAt));
        var expired = bans.Single(banInfo => banInfo.Id != lifted.Id);
        Assert.Equal(("kurz", null), (expired.Reason, expired.LiftedAt));
        Assert.True(expired.ExpiresAt < time.GetUtcNow());
        Assert.Equal(2, StoredBans().Count);
        Assert.Empty((await UsersAsync(admin)).Single(knownUser => knownUser.Fingerprint == guestId.Fingerprint).Bans!); // the user card only gets active bans
    }

    [Fact]
    public async Task History_RemovedAfterLogRetention_AndOnUserDelete()
    {
        await admin.SendAsync(new UpdateServerSettings("Testserver", "", null, new ServerLimits(50, 1, true, false, new TimeOnly(4, 0))));
        await admin.WaitForAsync<ServerSettingsChanged>();
        await moderator.SendAsync(new BanSession(guest.Id, "alt", 10, false));
        await guest.WaitForAsync<Disconnected>();
        guest = moderator;
        time.Advance(TimeSpan.FromMinutes(11));
        await moderator.SendAsync(new BanKnownUser(guestId.Fingerprint, "dauer", null, false));
        Assert.Equal(2, (await BansAsync(moderator)).Count);

        time.Advance(TimeSpan.FromHours(23)); // the expired ban ended 23 h ago: still kept
        await admin.SendAsync(new CreateChannel("Speichern1", ""));
        await admin.WaitForAsync<ChannelAdded>();
        Assert.Equal(2, StoredBans().Count);

        time.Advance(TimeSpan.FromHours(2)); // now more than one day: gone, the active one stays however old
        await admin.SendAsync(new CreateChannel("Speichern2", ""));
        await admin.WaitForAsync<ChannelAdded>(channelAdded => channelAdded.Channel.Name == "Speichern2");
        Assert.Equal("dauer", Assert.Single(StoredBans()).Reason);
        var active = Assert.Single(await BansAsync(moderator));

        await moderator.SendAsync(new Unban(active.Id) { RequestId = "u" });
        await moderator.WaitForAsync<BanList>(banList => banList.RequestId == "u");
        await admin.SendAsync(new DeleteKnownUser(guestId.Fingerprint));
        Assert.DoesNotContain(await UsersAsync(admin), knownUser => knownUser.Fingerprint == guestId.Fingerprint);
        Assert.Empty(await BansAsync(admin));
        Assert.Empty(StoredBans());
    }

    [Fact]
    public async Task BlockedAttempts_Counted_SavedAtMostPerMinute()
    {
        await moderator.SendAsync(new BanSession(guest.Id, "raus", null, false));
        await guest.WaitForAsync<Disconnected>();
        for (int index = 0; index < 3; index++)
        {
            if (index > 0) time.Advance(TimeSpan.FromSeconds(5));
            Assert.Equal(ReasonCodes.Banned, Assert.IsType<Rejected>(await ReconnectGuestAsync()).Code);
        }
        Assert.Equal(3, Assert.Single(await BansAsync(moderator)).BlockedAttempts);
        Assert.Equal(1, Assert.Single(StoredBans()).BlockedAttempts); // only the first of these was written

        time.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(ReasonCodes.Banned, Assert.IsType<Rejected>(await ReconnectGuestAsync()).Code);
        var ban = Assert.Single(await BansAsync(moderator));
        Assert.Equal((4, time.GetUtcNow(), "127.0.0.1"), (ban.BlockedAttempts, ban.LastAttempt, ban.LastAttemptIp));
        var stored = Assert.Single(StoredBans());
        Assert.Equal((4, time.GetUtcNow(), "127.0.0.1"), (stored.BlockedAttempts, stored.LastAttempt, stored.LastAttemptIp));
    }

    // ---- Package 84: one rank rule for every action on another user (A102) ----

    Guid GroupId(string name) => admin.Welcome.Snapshot.Groups.Single(groupInfo => groupInfo.Name == name).Id;

    /// <summary>A new client the admin puts into the group.</summary>
    async Task<TestClient> ConnectInGroupAsync(string nickname, Guid groupId)
    {
        var client = await TestClient.ConnectAsync(server, nickname, ClientIdentity.Create());
        await admin.SendAsync(new AssignGroup(client.Identity.Fingerprint, groupId));
        await admin.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == client.Id && userUpdated.User.GroupIds.Contains(groupId));
        return client;
    }

    /// <summary>A group with every right that is not the Admin group: equal to an admin, never the last admin.</summary>
    async Task<TestClient> ConnectChefAsync()
    {
        await admin.SendAsync(new CreateGroup("Chef", Permission.All));
        var chef = (await admin.WaitForAsync<GroupsChanged>()).Groups.Single(groupInfo => groupInfo.Name == "Chef").Id;
        return await ConnectInGroupAsync("chef", chef);
    }

    [Fact]
    public async Task Moderate_EqualRanks_Refused()
    {
        await using var m2 = await ConnectInGroupAsync("mod2", GroupId("Moderator"));
        var lobby = admin.Welcome.Snapshot.DefaultChannelId;
        Request[] requests =
        [
            new KickSession(m2.Id, "x"), new BanSession(m2.Id, "x", null, false), new BanKnownUser(m2.Identity.Fingerprint, "x", null, false),
            new SetSessionServerMute(m2.Id, true), new MoveUser(m2.Id, lobby),
        ];
        for (int index = 0; index < requests.Length; index++)
        {
            await moderator.SendAsync(requests[index] with { RequestId = $"m{index}" });
            Assert.Equal(ReasonCodes.PermissionDenied, (await moderator.ErrorAsync($"m{index}")).Code);
        }

        // two admins neither
        await using var a2 = await ConnectInGroupAsync("admin2", PermissionRules.AdminGroupId);
        Request[] onAdmin =
        [
            new KickSession(admin.Id, "x"), new SetSessionServerMute(admin.Id, true), new MoveUser(admin.Id, lobby), new DeleteKnownUser(adminId.Fingerprint),
            new UnassignGroup(adminId.Fingerprint, PermissionRules.AdminGroupId), new AssignGroup(adminId.Fingerprint, GroupId("Moderator")),
            new BanKnownUser(adminId.Fingerprint, "x", null, false),
        ];
        for (int index = 0; index < onAdmin.Length; index++)
        {
            await a2.SendAsync(onAdmin[index] with { RequestId = $"a{index}" });
            Assert.Equal(ReasonCodes.PermissionDenied, (await a2.ErrorAsync($"a{index}")).Code);
        }
        Assert.Contains(PermissionRules.AdminGroupId, (await UsersAsync(admin)).Single(knownUser => knownUser.Fingerprint == adminId.Fingerprint).GroupIds);
        Assert.Empty(await BansAsync(admin));
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
    public async Task Moderate_Self_Denied(string action)
    {
        var lobby = admin.Welcome.Snapshot.DefaultChannelId;
        if (action == "unmute")
        {
            await admin.SendAsync(new SetSessionServerMute(moderator.Id, true));
            await admin.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == moderator.Id && userUpdated.User.ServerMuted);
        }
        // the moderator for its own rights, a user with every right (not the Admin group) for group changes and delete
        var actor = action is "assign" or "unassign" or "delete" ? await ConnectChefAsync() : moderator;
        var fingerprint = actor.Identity.Fingerprint;
        var groupsBefore = (await UsersAsync(admin)).Single(knownUser => knownUser.Fingerprint == fingerprint).GroupIds;
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
        await admin.AssertNoMessageAsync<UserUpdated>(); // still muted, not moved, no new rights
        Assert.Equal(groupsBefore, (await UsersAsync(admin)).Single(knownUser => knownUser.Fingerprint == fingerprint).GroupIds);
        Assert.Empty(await BansAsync(admin));
        if (actor != moderator) await actor.DisposeAsync();
    }

    [Fact]
    public async Task Ban_LastAdmin_Refused()
    {
        await admin.SendAsync(new BanSession(admin.Id, "x", null, false) { RequestId = "self" });
        Assert.Equal(ReasonCodes.LastAdmin, (await admin.ErrorAsync("self")).Code);
        await admin.SendAsync(new BanKnownUser(adminId.Fingerprint, "x", null, false) { RequestId = "selfUser" });
        Assert.Equal(ReasonCodes.LastAdmin, (await admin.ErrorAsync("selfUser")).Code);
        await moderator.SendAsync(new BanSession(admin.Id, "x", null, false) { RequestId = "online" });
        Assert.Equal(ReasonCodes.LastAdmin, (await moderator.ErrorAsync("online")).Code);

        var adminSession = admin.Id;
        await admin.DisposeAsync();
        await moderator.WaitForAsync<UserLeft>(userLeft => userLeft.SessionId == adminSession);
        admin = moderator;
        await moderator.SendAsync(new BanKnownUser(adminId.Fingerprint, "x", null, true) { RequestId = "offline" });
        Assert.Equal(ReasonCodes.LastAdmin, (await moderator.ErrorAsync("offline")).Code);
        Assert.Empty(await BansAsync(moderator));
    }

    [Fact]
    public async Task Unban_StrongerBannedUser_DeniedWithoutBanList()
    {
        await moderator.SendAsync(new BanSession(guest.Id, "x", null, false));
        await guest.WaitForAsync<Disconnected>();
        guest = moderator;
        var ban = Assert.Single(await BansAsync(moderator));

        // the banned guest became a moderator meanwhile: equal to m, who may no longer lift the ban
        await admin.SendAsync(new AssignGroup(guestId.Fingerprint, GroupId("Moderator")));
        await UsersAsync(admin); // the assignment is done
        await moderator.SendAsync(new Unban(ban.Id) { RequestId = "equal" });
        Assert.Equal(ReasonCodes.PermissionDenied, (await moderator.ErrorAsync("equal")).Code);
        Assert.Null(Assert.Single(await BansAsync(moderator)).LiftedAt);
        await admin.SendAsync(new UnassignGroup(guestId.Fingerprint, GroupId("Moderator")));

        // with UserBan but without BansView: lifted, but the ban list is not sent
        await admin.SendAsync(new CreateGroup("Entbanner", PermissionRules.GuestPermissions | Permission.UserBan));
        var group = (await admin.WaitForAsync<GroupsChanged>(groupsChanged => groupsChanged.Groups.Any(groupInfo => groupInfo.Name == "Entbanner"))).Groups.Single(groupInfo => groupInfo.Name == "Entbanner").Id;
        await using var client = await ConnectInGroupAsync("entbanner", group);
        await client.SendAsync(new Unban(ban.Id) { RequestId = "lift" });
        // a request that surely fails: its answer comes after anything the unban sent back, however slow the server
        await client.SendAsync(new MoveUser(client.Id, Guid.NewGuid()) { RequestId = "probe" });
        while (await client.NextAsync() is { } message and not Error { RequestId: "probe" })
            Assert.False(message is BanList or Error, $"unexpected {message}");
        Assert.Equal("entbanner", Assert.Single(await BansAsync(moderator)).LiftedBy);
        Assert.IsType<Welcome>(await ReconnectGuestAsync());
    }
}
