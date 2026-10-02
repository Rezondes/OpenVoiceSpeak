using OVS.Server;
using OVS.Server.Data;
using OVS.Server.Permissions;
using OVS.Shared.Identity;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server;

public class AdminCommandTests
{
    [Fact]
    public void AdminToken_Redeem_Once_Valid_Twice_Invalid()
    {
        var token = new AdminToken();
        var value = token.Generate();
        Assert.False(token.TryRedeem("falsch"));
        Assert.True(token.TryRedeem($" {value} "));
        Assert.False(token.TryRedeem(value));
        Assert.Null(token.Current);
    }

    [Fact]
    public async Task NoAdmin_TokenGenerated_AdminExists_NoToken()
    {
        await using (var fresh = await TestServer.StartAsync())
            Assert.NotNull(fresh.State.PendingAdminToken);

        await using var withAdmin = await TestServer.StartAsync(TestServer.Grant(ClientIdentity.Create(), "Admin"));
        Assert.Null(withAdmin.State.PendingAdminToken);
    }

    [Fact]
    public async Task Redeem_BroadcastsNewPermissions_ThenTokenInvalid()
    {
        await using var server = await TestServer.StartAsync();
        await using var a = await TestClient.ConnectAsync(server);
        await using var b = await TestClient.ConnectAsync(server);

        await a.SendAsync(new RedeemAdminToken(server.State.PendingAdminToken!));
        var update = await b.WaitForAsync<UserUpdated>(u => u.User.SessionId == a.Id);
        Assert.Equal(Permission.None, update.User.Permissions); // Package 92: others' rights stay with the server
        Assert.Contains(PermissionRules.AdminGroupId, update.User.GroupIds);
        Assert.Equal(Permission.All, (await a.WaitForAsync<UserUpdated>(u => u.User.SessionId == a.Id)).User.Permissions);

        await b.SendAsync(new RedeemAdminToken("egal") { RequestId = "t" });
        Assert.Equal(Codes.InvalidToken, (await b.ErrorAsync("t")).Code);
    }

    [Fact]
    public async Task Redeem_WrongToken_InvalidToken()
    {
        await using var server = await TestServer.StartAsync();
        await using var a = await TestClient.ConnectAsync(server);
        await a.SendAsync(new RedeemAdminToken("falsch") { RequestId = "t" });
        Assert.Equal(Codes.InvalidToken, (await a.ErrorAsync("t")).Code);
        Assert.NotNull(server.State.PendingAdminToken);
    }

    [Fact]
    public async Task CreateGroup_AsAdmin_BroadcastsGroupsChanged()
    {
        var admin = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(TestServer.Grant(admin, "Admin"));
        await using var a = await TestClient.ConnectAsync(server, identity: admin);
        await using var g = await TestClient.ConnectAsync(server);

        await a.SendAsync(new CreateGroup("Team", Permission.Speak | Permission.SpeakLinked));
        var changed = await a.WaitForAsync<GroupsChanged>();
        Assert.Contains(changed.Groups, x => x.Name == "Team" && x.Permissions == (Permission.Speak | Permission.SpeakLinked));
        // Package 92: everyone hears of it, the rights only reach GroupsView holders
        Assert.Contains((await g.WaitForAsync<GroupsChanged>()).Groups, x => x.Name == "Team" && x.Permissions == Permission.None);
    }

    [Fact]
    public async Task UpdateGroup_ChangesOnlineMemberPermissions()
    {
        var admin = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(TestServer.Grant(admin, "Admin"));
        await using var a = await TestClient.ConnectAsync(server, identity: admin);
        await using var g = await TestClient.ConnectAsync(server);

        await a.SendAsync(new UpdateGroup(PermissionRules.GuestGroupId, "Gast", Permission.Speak | Permission.SpeakLinked));
        var groups = await a.WaitForAsync<GroupsChanged>(); // Package 92: the rights only reach GroupsView holders
        Assert.Equal(Permission.Speak | Permission.SpeakLinked, groups.Groups.Single(x => x.Id == PermissionRules.GuestGroupId).Permissions);
        var update = await g.WaitForAsync<UserUpdated>(u => u.User.SessionId == g.Id); // the own entry keeps the full rights
        Assert.Equal(Permission.Speak | Permission.SpeakLinked, update.User.Permissions);
    }

    [Fact]
    public async Task CreateGroup_GrantingUnownedPermission_PermissionDenied()
    {
        var manager = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(d =>
        {
            d.Groups.Add(new Group(Guid.NewGuid(), "Verwalter", Permission.GroupsCreate | Permission.Speak));
            TestServer.Grant(manager, "Verwalter")(d);
        });
        await using var v = await TestClient.ConnectAsync(server, identity: manager);

        await v.SendAsync(new CreateGroup("Hack", Permission.UserBan) { RequestId = "g" });
        Assert.Equal(Codes.PermissionDenied, (await v.ErrorAsync("g")).Code);

        await v.SendAsync(new CreateGroup("Ok", Permission.Speak));
        await v.WaitForAsync<GroupsChanged>();
    }

    [Theory]
    [InlineData("", Codes.InvalidName)]
    [InlineData("123456789012345678901234567890123", Codes.InvalidName)]
    [InlineData(" gast ", Codes.NameTaken)]
    public async Task CreateGroup_InvalidOrDuplicateName_Error(string name, string code)
    {
        var admin = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(TestServer.Grant(admin, "Admin"));
        await using var a = await TestClient.ConnectAsync(server, identity: admin);
        await a.SendAsync(new CreateGroup(name, Permission.Speak) { RequestId = "g" });
        Assert.Equal(code, (await a.ErrorAsync("g")).Code);
    }

    [Fact]
    public async Task Assign_GroupStrongerThanActor_PermissionDenied()
    {
        var assigner = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(d =>
        {
            d.Groups.Add(new Group(Guid.NewGuid(), "Zuweiser", Permission.GroupsAssign | Permission.Speak));
            TestServer.Grant(assigner, "Zuweiser")(d);
        });
        await using var z = await TestClient.ConnectAsync(server, identity: assigner);
        var modGroup = z.Welcome.Snapshot.Groups.Single(g => g.Name == "Moderator").Id;

        await z.SendAsync(new AssignGroup(assigner.Fingerprint, modGroup) { RequestId = "a" });
        Assert.Equal(Codes.PermissionDenied, (await z.ErrorAsync("a")).Code);
    }

    [Fact]
    public async Task Unassign_OfflineUser_And_ListShowsNickname()
    {
        var admin = ClientIdentity.Create();
        var modGroup = Guid.Empty;
        await using var server = await TestServer.StartAsync(d =>
        {
            TestServer.Grant(admin, "Admin")(d);
            modGroup = d.Groups.Single(g => g.Name == "Moderator").Id;
            d.Users.Add(new UserRecord { Fingerprint = "offline", LastNickname = "Otto", GroupIds = [PermissionRules.GuestGroupId, modGroup] });
        });
        await using var a = await TestClient.ConnectAsync(server, identity: admin);

        await a.SendAsync(new UnassignGroup("offline", modGroup));
        await a.SendAsync(new ListUsers { RequestId = "l" });
        var otto = (await a.WaitForAsync<UserList>(l => l.RequestId == "l")).Users.Single(u => u.Fingerprint == "offline");
        Assert.Equal("Otto", otto.LastNickname);
        Assert.Equal([PermissionRules.GuestGroupId], otto.GroupIds);
    }

    [Fact]
    public async Task RedeemedAdmin_And_Password_SurviveRestart()
    {
        var server = await TestServer.StartAsync();
        var id = ClientIdentity.Create();
        await using (var a = await TestClient.ConnectAsync(server, "anna", id))
        {
            await a.SendAsync(new RedeemAdminToken(server.State.PendingAdminToken!));
            await a.WaitForAsync<UserUpdated>(u => u.User.SessionId == a.Id && u.User.Permissions == Permission.All);
            await a.SendAsync(new UpdateServerSettings("S", "", "geheim"));
            await a.WaitForAsync<ServerSettingsChanged>();
        }
        server = await server.RestartAsync();
        await using var _ = server;

        Assert.Null(server.State.PendingAdminToken); // an admin exists now
        await using var again = await TestClient.OpenAsync(server.Port, id);
        var welcome = Assert.IsType<Welcome>(await again.HandshakeAsync("anna", "geheim"));
        Assert.Equal(Permission.All, welcome.Snapshot.Users.Single(u => u.SessionId == welcome.SessionId).Permissions);
        Assert.DoesNotContain("geheim", File.ReadAllText(Path.Combine(server.DataDir, "server-data.json")));
    }

    [Theory]
    [InlineData("updateAdmin")]
    [InlineData("deleteAdmin")]
    [InlineData("deleteGuest")]
    public async Task ProtectedGroups_Errors(string action)
    {
        var admin = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(TestServer.Grant(admin, "Admin"));
        await using var a = await TestClient.ConnectAsync(server, identity: admin);

        Request r = action switch
        {
            "updateAdmin" => new UpdateGroup(PermissionRules.AdminGroupId, "Admin", Permission.Speak),
            "deleteAdmin" => new DeleteGroup(PermissionRules.AdminGroupId),
            _ => new DeleteGroup(PermissionRules.GuestGroupId),
        };
        await a.SendAsync(r with { RequestId = "p" });
        Assert.Equal(Codes.ProtectedGroup, (await a.ErrorAsync("p")).Code);
    }

    [Fact]
    public async Task DeleteGroup_RemovedFromUsers()
    {
        var admin = ClientIdentity.Create();
        var mod = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(d =>
        {
            TestServer.Grant(admin, "Admin")(d);
            TestServer.Grant(mod, "Moderator")(d);
        });
        await using var a = await TestClient.ConnectAsync(server, identity: admin);
        await using var m = await TestClient.ConnectAsync(server, identity: mod);
        var modGroup = a.Welcome.Snapshot.Groups.Single(g => g.Name == "Moderator").Id;

        await a.SendAsync(new DeleteGroup(modGroup));
        // the update that takes the group away (another one for the moderator may come first under load)
        var update = await a.WaitForAsync<UserUpdated>(u => u.User.SessionId == m.Id && !u.User.GroupIds.Contains(modGroup));
        Assert.DoesNotContain(modGroup, update.User.GroupIds);
        Assert.Equal(Permission.None, update.User.Permissions);
    }

    [Fact]
    public async Task Assign_OfflineUser_Persisted_AndListed()
    {
        var admin = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(d =>
        {
            TestServer.Grant(admin, "Admin")(d);
            d.Users.Add(new UserRecord { Fingerprint = "offline", LastNickname = "Otto", GroupIds = [PermissionRules.GuestGroupId] });
        });
        await using var a = await TestClient.ConnectAsync(server, identity: admin);
        var modGroup = a.Welcome.Snapshot.Groups.Single(g => g.Name == "Moderator").Id;

        await a.SendAsync(new AssignGroup("offline", modGroup));
        await a.SendAsync(new ListUsers { RequestId = "l" });
        var list = await a.WaitForAsync<UserList>(l => l.RequestId == "l");
        Assert.Contains(modGroup, list.Users.Single(u => u.Fingerprint == "offline").GroupIds);

        var restarted = await server.RestartAsync();
        await using var _ = restarted;
        await using var a2 = await TestClient.ConnectAsync(restarted, identity: admin);
        await a2.SendAsync(new ListUsers { RequestId = "l2" });
        list = await a2.WaitForAsync<UserList>(l => l.RequestId == "l2");
        Assert.Contains(modGroup, list.Users.Single(u => u.Fingerprint == "offline").GroupIds);
    }

    [Fact]
    public async Task Unassign_LastAdmin_Error()
    {
        var admin = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(TestServer.Grant(admin, "Admin"));
        await using var a = await TestClient.ConnectAsync(server, identity: admin);

        await a.SendAsync(new UnassignGroup(admin.Fingerprint, PermissionRules.AdminGroupId) { RequestId = "u" });
        Assert.Equal(Codes.LastAdmin, (await a.ErrorAsync("u")).Code);
    }

    [Fact]
    public async Task Unassign_FromStrongerUser_Denied()
    {
        // Package 84: the group must be within the actor's rights and the target weaker than the actor
        var admin = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(TestServer.Grant(admin, "Admin"));
        await using var a = await TestClient.ConnectAsync(server, "admin", admin);
        await using var z = await TestClient.ConnectAsync(server, "zuteiler");
        await using var mod = await TestClient.ConnectAsync(server, "mod");
        await using var guest = await TestClient.ConnectAsync(server, "gast");
        var modGroup = a.Welcome.Snapshot.Groups.Single(g => g.Name == "Moderator").Id;
        await a.SendAsync(new CreateGroup("Zuteiler", PermissionRules.GuestPermissions | Permission.GroupsAssign));
        var assigner = (await a.WaitForAsync<GroupsChanged>()).Groups.Single(g => g.Name == "Zuteiler").Id;
        await a.SendAsync(new AssignGroup(z.Identity.Fingerprint, assigner));
        await a.SendAsync(new AssignGroup(mod.Identity.Fingerprint, modGroup));
        await a.WaitForAsync<UserUpdated>(u => u.User.SessionId == mod.Id);

        await z.SendAsync(new UnassignGroup(mod.Identity.Fingerprint, PermissionRules.GuestGroupId) { RequestId = "stronger" });
        Assert.Equal(Codes.PermissionDenied, (await z.ErrorAsync("stronger")).Code);
        await z.SendAsync(new AssignGroup(mod.Identity.Fingerprint, assigner) { RequestId = "strongerAssign" });
        Assert.Equal(Codes.PermissionDenied, (await z.ErrorAsync("strongerAssign")).Code);

        // a plain guest is weaker: allowed
        await z.SendAsync(new AssignGroup(guest.Identity.Fingerprint, assigner));
        Assert.Equal(PermissionRules.GuestPermissions | Permission.GroupsAssign,
            (await guest.WaitForAsync<UserUpdated>(u => u.User.SessionId == guest.Id)).User.Permissions); // Package 92: seen in the own entry
        await a.SendAsync(new ListUsers { RequestId = "l" });
        var users = (await a.WaitForAsync<UserList>(l => l.RequestId == "l")).Users;
        Assert.Equal([PermissionRules.GuestGroupId, modGroup], users.Single(u => u.Fingerprint == mod.Identity.Fingerprint).GroupIds);
    }

    [Fact]
    public async Task ListUsers_WithoutGroupsAssign_PermissionDenied()
    {
        await using var server = await TestServer.StartAsync();
        await using var g = await TestClient.ConnectAsync(server);
        await g.SendAsync(new ListUsers { RequestId = "l" });
        Assert.Equal(Codes.PermissionDenied, (await g.ErrorAsync("l")).Code);
    }

    [Fact]
    public async Task UpdateSettings_NewPasswordAppliesToNextHandshake()
    {
        var admin = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(TestServer.Grant(admin, "Admin"));
        await using var a = await TestClient.ConnectAsync(server, identity: admin);

        await a.SendAsync(new UpdateServerSettings("Neu", "Hallo", "pw"));
        var changed = await a.WaitForAsync<ServerSettingsChanged>();
        Assert.Equal(new ServerSettingsInfo("Neu", "Hallo", true), changed.Settings with { Limits = null });

        await using var without = await TestClient.OpenAsync(server.Port);
        Assert.Equal(Codes.WrongPassword, Assert.IsType<Rejected>(await without.HandshakeAsync("x")).Code);
        await using var with = await TestClient.OpenAsync(server.Port);
        Assert.IsType<Welcome>(await with.HandshakeAsync("y", "pw"));

        // null keeps the password, "" removes it
        await a.SendAsync(new UpdateServerSettings("Neu", "Hallo", null));
        Assert.True((await a.WaitForAsync<ServerSettingsChanged>()).Settings.HasPassword);
        await a.SendAsync(new UpdateServerSettings("Neu", "Hallo", ""));
        Assert.False((await a.WaitForAsync<ServerSettingsChanged>()).Settings.HasPassword);
    }

    [Theory]
    [InlineData("", "", Codes.InvalidName)]
    [InlineData("ok", "LONG", Codes.InvalidValue)]
    public async Task UpdateSettings_InvalidValues_Error(string name, string welcome, string code)
    {
        var admin = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(TestServer.Grant(admin, "Admin"));
        await using var a = await TestClient.ConnectAsync(server, identity: admin);

        await a.SendAsync(new UpdateServerSettings(name, welcome == "LONG" ? new string('x', 501) : welcome, null) { RequestId = "s" });
        Assert.Equal(code, (await a.ErrorAsync("s")).Code);
    }

    // ---- Package 69: limits, logs and restart ----

    static readonly ServerLimits NewLimits = new(20, 7, false, true, new TimeOnly(3, 30));

    [Fact]
    public async Task UpdateSettings_Limits_ValidatedAndBroadcast()
    {
        var admin = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(TestServer.Grant(admin, "Admin"));
        await using var a = await TestClient.ConnectAsync(server, identity: admin);
        await using var g = await TestClient.ConnectAsync(server);
        Assert.Equal(new ServerLimits(50, 30, true, false, new TimeOnly(4, 0)), a.Welcome.Snapshot.Settings.Limits);
        Assert.Null(g.Welcome.Snapshot.Settings.Limits); // only for those with the right

        await a.SendAsync(new UpdateServerSettings("Neu", "", null, NewLimits with { MaxUsers = 0 }) { RequestId = "1" });
        Assert.Equal(Codes.InvalidValue, (await a.ErrorAsync("1")).Code);
        await a.SendAsync(new UpdateServerSettings("Neu", "", null, NewLimits with { LogDays = 5000 }) { RequestId = "2" });
        Assert.Equal(Codes.InvalidValue, (await a.ErrorAsync("2")).Code);
        var store = new DataStore(Path.Combine(server.DataDir, DataStore.FileName));
        var unchanged = store.LoadOrCreate(() => throw new InvalidOperationException()).Settings;
        Assert.Equal(("Testserver", 50, 30), (unchanged.Name, unchanged.MaxUsers, unchanged.LogDays)); // nothing half applied

        await a.SendAsync(new UpdateServerSettings("Neu", "", null, NewLimits));
        Assert.Equal(NewLimits, (await a.WaitForAsync<ServerSettingsChanged>()).Settings.Limits);
        var seen = await g.WaitForAsync<ServerSettingsChanged>();
        Assert.Equal("Neu", seen.Settings.Name);
        Assert.Null(seen.Settings.Limits);
        var saved = store.LoadOrCreate(() => throw new InvalidOperationException()).Settings;
        Assert.Equal((20, 7, false, true, new TimeOnly(3, 30)),
            (saved.MaxUsers, saved.LogDays, saved.LogRotateDaily, saved.AutoRestart, saved.AutoRestartTime));
    }

    [Fact]
    public async Task MaxUsersLowered_NextJoinRejected_NobodyKicked()
    {
        var admin = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(TestServer.Grant(admin, "Admin"));
        await using var a = await TestClient.ConnectAsync(server, identity: admin);
        await using var b = await TestClient.ConnectAsync(server);
        await using var c = await TestClient.ConnectAsync(server);

        await a.SendAsync(new UpdateServerSettings("S", "", null, NewLimits with { MaxUsers = 2 }));
        await a.WaitForAsync<ServerSettingsChanged>();
        Assert.Equal(3, server.State.SessionCount);

        await using var d = await TestClient.OpenAsync(server.Port);
        Assert.Equal(Codes.ServerFull, Assert.IsType<Rejected>(await d.HandshakeAsync("vierter")).Code);
        Assert.Equal(3, server.State.SessionCount);
    }

    [Fact]
    public async Task ServerConfigGranted_LimitsDelivered()
    {
        var admin = ClientIdentity.Create();
        var guest = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(TestServer.Grant(admin, "Admin"));
        await using var a = await TestClient.ConnectAsync(server, identity: admin);
        await using var g = await TestClient.ConnectAsync(server, identity: guest);
        await a.SendAsync(new AssignGroup(guest.Fingerprint, PermissionRules.AdminGroupId));
        Assert.NotNull((await g.WaitForAsync<ServerSettingsChanged>()).Settings.Limits);
    }

    [Fact]
    public async Task UpdateSettings_AsGuest_PermissionDenied()
    {
        await using var server = await TestServer.StartAsync();
        await using var g = await TestClient.ConnectAsync(server);
        await g.SendAsync(new UpdateServerSettings("x", "", null) { RequestId = "s" });
        Assert.Equal(Codes.PermissionDenied, (await g.ErrorAsync("s")).Code);
    }

    [Fact]
    public async Task AdminChanges_SurviveRestart()
    {
        var admin = ClientIdentity.Create();
        var server = await TestServer.StartAsync(TestServer.Grant(admin, "Admin"));
        await using (var a = await TestClient.ConnectAsync(server, identity: admin))
        {
            await a.SendAsync(new CreateGroup("Team", Permission.Speak));
            await a.WaitForAsync<GroupsChanged>();
            await a.SendAsync(new UpdateServerSettings("Umbenannt", "", null));
            await a.WaitForAsync<ServerSettingsChanged>();
        }
        server = await server.RestartAsync();
        await using var _ = server;
        await using var b = await TestClient.ConnectAsync(server);
        Assert.Contains(b.Welcome.Snapshot.Groups, g => g.Name == "Team");
        Assert.Equal("Umbenannt", b.Welcome.Snapshot.Settings.Name);
    }

    // ---- Package 37: group order ----

    static async Task<(TestServer Server, TestClient Admin, TestClient Mod, TestClient Guest)> GroupServerAsync()
    {
        var adminId = ClientIdentity.Create();
        var modId = ClientIdentity.Create();
        var server = await TestServer.StartAsync(d =>
        {
            TestServer.Grant(adminId, "Admin")(d);
            TestServer.Grant(modId, "Moderator")(d);
        });
        return (server, await TestClient.ConnectAsync(server, "admin", adminId), await TestClient.ConnectAsync(server, "mod", modId),
            await TestClient.ConnectAsync(server, "gast"));
    }

    [Fact]
    public async Task ReorderGroups_PersistsAndBroadcasts_RankUnchanged()
    {
        var (server, admin, mod, guest) = await GroupServerAsync();
        await using var _s = server;
        await using var _a = admin;
        await using var _m = mod;
        await using var _g = guest;
        var ids = admin.Welcome.Snapshot.Groups.Select(g => g.Id).Reverse().ToList();

        await admin.SendAsync(new ReorderGroups(ids));
        var changed = await guest.WaitForAsync<GroupsChanged>();
        Assert.Equal(ids, changed.Groups.Select(g => g.Id));
        Assert.Contains(server.Log, l => l.Contains("Gruppen umsortiert von admin"));

        await using var late = await TestClient.ConnectAsync(server, "spaet");
        Assert.Equal(ids, late.Welcome.Snapshot.Groups.Select(g => g.Id));

        await mod.SendAsync(new Kick(admin.Id, "") { RequestId = "k" }); // the moderator still cannot touch the admin
        Assert.Equal(Codes.PermissionDenied, (await mod.ErrorAsync("k")).Code);
    }

    [Fact]
    public async Task ReorderGroups_IncompleteOrWithoutRight_Rejected()
    {
        var (server, admin, mod, guest) = await GroupServerAsync();
        await using var _s = server;
        await using var _a = admin;
        await using var _m = mod;
        await using var _g = guest;
        var ids = admin.Welcome.Snapshot.Groups.Select(g => g.Id).ToList();

        await admin.SendAsync(new ReorderGroups(ids.Skip(1).ToList()) { RequestId = "fehlt" });
        Assert.Equal(Codes.InvalidValue, (await admin.ErrorAsync("fehlt")).Code);
        await guest.SendAsync(new ReorderGroups(ids) { RequestId = "recht" });
        Assert.Equal(Codes.PermissionDenied, (await guest.ErrorAsync("recht")).Code);
    }

    /// <summary>Package 71: the overview shows a user's active bans, also to someone who may not see the ban list.</summary>
    [Fact]
    public async Task ListUsers_IncludesActiveBans()
    {
        var viewer = ClientIdentity.Create();
        var guest = ClientIdentity.Create();
        var time = new ManualTimeProvider();
        await using var server = await TestServer.StartAsync(d =>
        {
            d.Groups.Add(new Group(Guid.NewGuid(), "Leser", Permission.UsersView));
            TestServer.Grant(viewer, "Leser")(d);
            d.Users.Add(new UserRecord { Fingerprint = guest.Fingerprint, LastNickname = "gast", GroupIds = [PermissionRules.GuestGroupId] });
            d.Users.Add(new UserRecord { Fingerprint = "frei", LastNickname = "Frei", GroupIds = [PermissionRules.GuestGroupId] });
            d.Bans.Add(new BanRecord { Id = Guid.NewGuid(), Fingerprint = guest.Fingerprint, Nickname = "gast", Reason = "aktiv", CreatedBy = "mod" });
            d.Bans.Add(new BanRecord { Id = Guid.NewGuid(), Fingerprint = guest.Fingerprint, Nickname = "gast", Reason = "kurz", CreatedBy = "mod",
                ExpiresAt = time.GetUtcNow().AddMinutes(5) });
        }, time: time);
        time.Advance(TimeSpan.FromMinutes(10)); // the short ban has run out
        await using var v = await TestClient.ConnectAsync(server, identity: viewer);

        await v.SendAsync(new ListUsers { RequestId = "l" });
        var users = (await v.WaitForAsync<UserList>(l => l.RequestId == "l")).Users;
        var ban = Assert.Single(users.Single(u => u.Fingerprint == guest.Fingerprint).Bans!);
        Assert.Equal(("aktiv", "mod", (DateTimeOffset?)null), (ban.Reason, ban.CreatedBy, ban.ExpiresAt));
        Assert.Empty(users.Single(u => u.Fingerprint == "frei").Bans ?? []);
    }

    // ---- Package 76: one right per request ----

    public static TheoryData<string, Permission> RightPerRequest => new()
    {
        { "listUsers", Permission.UsersView },
        { "listBans", Permission.BansView },
        { "ban", Permission.UserBan },
        { "unban", Permission.UserBan },
        { "kick", Permission.UserKick },
        { "assign", Permission.GroupsAssign },
        { "unassign", Permission.GroupsAssign },
        { "createGroup", Permission.GroupsCreate },
        { "updateGroup", Permission.GroupsManage },
        { "reorderGroups", Permission.GroupsManage },
        { "deleteGroup", Permission.GroupsDelete },
        { "banUser", Permission.UserBan },       // Package 72
        { "deleteUser", Permission.UserDelete },
    };

    [Theory]
    [MemberData(nameof(RightPerRequest))]
    public async Task EachAdminRequest_RequiresItsOwnRight(string action, Permission right)
    {
        Assert.Equal(Codes.PermissionDenied, await ErrorOfAsync(action, Permission.All & ~right));
        Assert.NotEqual(Codes.PermissionDenied, await ErrorOfAsync(action, Permission.All));
    }

    /// <summary>
    /// Sends the request as a member of a group with exactly <paramref name="rights"/> (not the Admin group) and returns
    /// its error code, or null when it succeeded. A request that surely fails follows, so "no error" needs no timeout.
    /// </summary>
    static async Task<string?> ErrorOfAsync(string action, Permission rights)
    {
        var actor = ClientIdentity.Create();
        var target = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(d =>
        {
            TestServer.Grant(ClientIdentity.Create(), "Admin")(d); // someone else is admin, so no token is pending
            d.Groups.Add(new Group(Guid.NewGuid(), "Tester", rights));
            TestServer.Grant(actor, "Tester")(d);
        });
        await using var a = await TestClient.ConnectAsync(server, "tester", actor);
        await using var t = await TestClient.ConnectAsync(server, "ziel", target);
        var groups = a.Welcome.Snapshot.Groups;
        var mod = groups.Single(g => g.Name == "Moderator").Id;
        Request r = action switch
        {
            "listUsers" => new ListUsers(),
            "listBans" => new ListBans(),
            "ban" => new Ban(t.Id, "x", null, false),
            "unban" => new Unban(Guid.NewGuid()),
            "kick" => new Kick(t.Id, "x"),
            "assign" => new AssignGroup(target.Fingerprint, mod),
            "unassign" => new UnassignGroup(target.Fingerprint, PermissionRules.GuestGroupId),
            "createGroup" => new CreateGroup("Neu", Permission.Speak),
            "updateGroup" => new UpdateGroup(PermissionRules.GuestGroupId, "Gast", Permission.Speak),
            "reorderGroups" => new ReorderGroups(groups.Select(g => g.Id).Reverse().ToList()),
            "deleteGroup" => new DeleteGroup(mod),
            "banUser" => new BanUser(target.Fingerprint, "x", null, false),
            "deleteUser" => new DeleteUser(target.Fingerprint),
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };
        await a.SendAsync(r with { RequestId = "r" });
        await a.SendAsync(new RedeemAdminToken("falsch") { RequestId = "probe" });
        var error = await a.WaitForAsync<Error>(e => e.RequestId is "r" or "probe");
        return error.RequestId == "r" ? error.Code : null;
    }

    [Fact]
    public async Task GroupsCreateOnly_CannotEditOrDelete()
    {
        var creator = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(d =>
        {
            d.Groups.Add(new Group(Guid.NewGuid(), "Anleger", Permission.GroupsCreate | Permission.Speak));
            TestServer.Grant(creator, "Anleger")(d);
        });
        await using var c = await TestClient.ConnectAsync(server, identity: creator);
        var ids = c.Welcome.Snapshot.Groups.Select(g => g.Id).ToList();

        await c.SendAsync(new CreateGroup("Neu", Permission.Speak));
        var created = (await c.WaitForAsync<GroupsChanged>()).Groups.Single(g => g.Name == "Neu");

        await c.SendAsync(new UpdateGroup(created.Id, "Anders", Permission.Speak) { RequestId = "u" });
        Assert.Equal(Codes.PermissionDenied, (await c.ErrorAsync("u")).Code);
        await c.SendAsync(new DeleteGroup(created.Id) { RequestId = "d" });
        Assert.Equal(Codes.PermissionDenied, (await c.ErrorAsync("d")).Code);
        await c.SendAsync(new ReorderGroups([.. ids.Append(created.Id).Reverse()]) { RequestId = "o" });
        Assert.Equal(Codes.PermissionDenied, (await c.ErrorAsync("o")).Code);
    }
}
