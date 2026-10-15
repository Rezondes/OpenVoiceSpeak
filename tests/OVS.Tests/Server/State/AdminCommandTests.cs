using OVS.Server;
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

public class AdminCommandTests
{
    [Fact]
    public void AdminToken_RedeemedTwice_OnlyFirstValid()
    {
        var token = new AdminToken();
        var value = token.Generate();
        Assert.False(token.TryRedeem("falsch"));
        Assert.True(token.TryRedeem($" {value} "));
        Assert.False(token.TryRedeem(value));
        Assert.Null(token.Current);
    }

    [Fact]
    public async Task AdminToken_AdminExistsOrNot_GeneratedOnlyWithout()
    {
        await using (var fresh = await TestServer.StartAsync())
            Assert.NotNull(fresh.State.PendingAdminToken);

        using var adminId = ClientIdentity.Create();
        await using var withAdmin = await TestServer.StartAsync(TestServer.Grant(adminId, "Admin"));
        Assert.Null(withAdmin.State.PendingAdminToken);
    }

    [Fact]
    public async Task Redeem_BroadcastsNewPermissions_ThenTokenInvalid()
    {
        await using var server = await TestServer.StartAsync();
        await using var client = await TestClient.ConnectAsync(server);
        await using var testClient = await TestClient.ConnectAsync(server);

        await client.SendAsync(new RedeemAdminToken(server.State.PendingAdminToken!));
        var update = await testClient.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == client.Id);
        Assert.Equal(Permission.None, update.User.Permissions); // Package 92: others' rights stay with the server
        Assert.Contains(PermissionRules.AdminGroupId, update.User.GroupIds);
        Assert.Equal(Permission.All, (await client.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == client.Id)).User.Permissions);

        await testClient.SendAsync(new RedeemAdminToken("egal") { RequestId = "t" });
        Assert.Equal(ReasonCodes.InvalidToken, (await testClient.ErrorAsync("t")).Code);
    }

    [Fact]
    public async Task Redeem_WrongToken_InvalidToken()
    {
        await using var server = await TestServer.StartAsync();
        await using var client = await TestClient.ConnectAsync(server);
        await client.SendAsync(new RedeemAdminToken("falsch") { RequestId = "t" });
        Assert.Equal(ReasonCodes.InvalidToken, (await client.ErrorAsync("t")).Code);
        Assert.NotNull(server.State.PendingAdminToken);
    }

    [Fact]
    public async Task CreateGroup_AsAdmin_BroadcastsGroupsChanged()
    {
        var admin = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(TestServer.Grant(admin, "Admin"));
        await using var client = await TestClient.ConnectAsync(server, identity: admin);
        await using var testClient = await TestClient.ConnectAsync(server);

        await client.SendAsync(new CreateGroup("Team", Permission.Speak | Permission.SpeakLinked));
        var changed = await client.WaitForAsync<GroupsChanged>();
        Assert.Contains(changed.Groups, groupInfo => groupInfo.Name == "Team" && groupInfo.Permissions == (Permission.Speak | Permission.SpeakLinked));
        // Package 92: everyone hears of it, the rights only reach GroupsView holders
        Assert.Contains((await testClient.WaitForAsync<GroupsChanged>()).Groups, groupInfo => groupInfo.Name == "Team" && groupInfo.Permissions == Permission.None);
    }

    [Fact]
    public async Task UpdateGroup_OnlineMember_PermissionsChange()
    {
        var admin = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(TestServer.Grant(admin, "Admin"));
        await using var client = await TestClient.ConnectAsync(server, identity: admin);
        await using var testClient = await TestClient.ConnectAsync(server);

        await client.SendAsync(new UpdateGroup(PermissionRules.GuestGroupId, "Gast", Permission.Speak | Permission.SpeakLinked));
        var groups = await client.WaitForAsync<GroupsChanged>(); // Package 92: the rights only reach GroupsView holders
        Assert.Equal(Permission.Speak | Permission.SpeakLinked, groups.Groups.Single(groupInfo => groupInfo.Id == PermissionRules.GuestGroupId).Permissions);
        var update = await testClient.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == testClient.Id); // the own entry keeps the full rights
        Assert.Equal(Permission.Speak | Permission.SpeakLinked, update.User.Permissions);
    }

    [Fact]
    public async Task CreateGroup_GrantingUnownedPermission_PermissionDenied()
    {
        var manager = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(serverData =>
        {
            serverData.Groups.Add(new GroupRecord(Guid.NewGuid(), "Verwalter", Permission.GroupsCreate | Permission.Speak));
            TestServer.Grant(manager, "Verwalter")(serverData);
        });
        await using var client = await TestClient.ConnectAsync(server, identity: manager);

        await client.SendAsync(new CreateGroup("Hack", Permission.UserBan) { RequestId = "g" });
        Assert.Equal(ReasonCodes.PermissionDenied, (await client.ErrorAsync("g")).Code);

        await client.SendAsync(new CreateGroup("Ok", Permission.Speak));
        await client.WaitForAsync<GroupsChanged>();
    }

    [Theory]
    [InlineData("", ReasonCodes.InvalidName)]
    [InlineData("123456789012345678901234567890123", ReasonCodes.InvalidName)]
    [InlineData(" gast ", ReasonCodes.NameTaken)]
    public async Task CreateGroup_InvalidOrDuplicateName_Error(string name, string code)
    {
        var admin = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(TestServer.Grant(admin, "Admin"));
        await using var client = await TestClient.ConnectAsync(server, identity: admin);
        await client.SendAsync(new CreateGroup(name, Permission.Speak) { RequestId = "g" });
        Assert.Equal(code, (await client.ErrorAsync("g")).Code);
    }

    [Fact]
    public async Task Assign_GroupStrongerThanActor_PermissionDenied()
    {
        var assigner = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(serverData =>
        {
            serverData.Groups.Add(new GroupRecord(Guid.NewGuid(), "Zuweiser", Permission.GroupsAssign | Permission.Speak));
            TestServer.Grant(assigner, "Zuweiser")(serverData);
        });
        await using var client = await TestClient.ConnectAsync(server, identity: assigner);
        var modGroup = client.Welcome.Snapshot.Groups.Single(groupInfo => groupInfo.Name == "Moderator").Id;

        await client.SendAsync(new AssignGroup(assigner.Fingerprint, modGroup) { RequestId = "a" });
        Assert.Equal(ReasonCodes.PermissionDenied, (await client.ErrorAsync("a")).Code);
    }

    [Fact]
    public async Task Unassign_OfflineUser_ListShowsNickname()
    {
        var admin = ClientIdentity.Create();
        var modGroup = Guid.Empty;
        await using var server = await TestServer.StartAsync(serverData =>
        {
            TestServer.Grant(admin, "Admin")(serverData);
            modGroup = serverData.Groups.Single(groupRecord => groupRecord.Name == "Moderator").Id;
            serverData.Users.Add(new UserRecord { Fingerprint = "offline", LastNickname = "Otto", GroupIds = [PermissionRules.GuestGroupId, modGroup] });
        });
        await using var client = await TestClient.ConnectAsync(server, identity: admin);

        await client.SendAsync(new UnassignGroup("offline", modGroup));
        await client.SendAsync(new ListUsers { RequestId = "l" });
        var otto = (await client.WaitForAsync<UserList>(userList => userList.RequestId == "l")).Users.Single(knownUser => knownUser.Fingerprint == "offline");
        Assert.Equal("Otto", otto.LastNickname);
        Assert.Equal([PermissionRules.GuestGroupId], otto.GroupIds);
    }

    [Fact]
    public async Task RedeemedAdminAndPassword_Restart_Survive()
    {
        var server = await TestServer.StartAsync();
        var id = ClientIdentity.Create();
        await using (var client = await TestClient.ConnectAsync(server, "anna", id))
        {
            await client.SendAsync(new RedeemAdminToken(server.State.PendingAdminToken!));
            await client.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == client.Id && userUpdated.User.Permissions == Permission.All);
            await client.SendAsync(new UpdateServerSettings("S", "", "geheim"));
            await client.WaitForAsync<ServerSettingsChanged>();
        }
        server = await server.RestartAsync();
        await using var _ = server;

        Assert.Null(server.State.PendingAdminToken); // an admin exists now
        await using var again = await TestClient.OpenAsync(server.Port, id);
        var welcome = Assert.IsType<Welcome>(await again.HandshakeAsync("anna", "geheim"));
        Assert.Equal(Permission.All, welcome.Snapshot.Users.Single(user => user.SessionId == welcome.SessionId).Permissions);
        Assert.DoesNotContain("geheim", await File.ReadAllTextAsync(Path.Combine(server.DataDir, "server-data.json")), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("updateAdmin")]
    [InlineData("deleteAdmin")]
    [InlineData("deleteGuest")]
    public async Task ProtectedGroups_Changed_Refused(string action)
    {
        var admin = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(TestServer.Grant(admin, "Admin"));
        await using var client = await TestClient.ConnectAsync(server, identity: admin);

        Request request = action switch
        {
            "updateAdmin" => new UpdateGroup(PermissionRules.AdminGroupId, "Admin", Permission.Speak),
            "deleteAdmin" => new DeleteGroup(PermissionRules.AdminGroupId),
            _ => new DeleteGroup(PermissionRules.GuestGroupId),
        };
        await client.SendAsync(request with { RequestId = "p" });
        Assert.Equal(ReasonCodes.ProtectedGroup, (await client.ErrorAsync("p")).Code);
    }

    [Fact]
    public async Task DeleteGroup_AssignedUsers_LoseIt()
    {
        var admin = ClientIdentity.Create();
        var mod = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(serverData =>
        {
            TestServer.Grant(admin, "Admin")(serverData);
            TestServer.Grant(mod, "Moderator")(serverData);
        });
        await using var client = await TestClient.ConnectAsync(server, identity: admin);
        await using var testClient = await TestClient.ConnectAsync(server, identity: mod);
        var modGroup = client.Welcome.Snapshot.Groups.Single(groupInfo => groupInfo.Name == "Moderator").Id;

        await client.SendAsync(new DeleteGroup(modGroup));
        // the update that takes the group away (another one for the moderator may come first under load)
        var update = await client.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == testClient.Id && !userUpdated.User.GroupIds.Contains(modGroup));
        Assert.DoesNotContain(modGroup, update.User.GroupIds);
        Assert.Equal(Permission.None, update.User.Permissions);
    }

    [Fact]
    public async Task Assign_OfflineUser_PersistedAndListed()
    {
        var admin = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(serverData =>
        {
            TestServer.Grant(admin, "Admin")(serverData);
            serverData.Users.Add(new UserRecord { Fingerprint = "offline", LastNickname = "Otto", GroupIds = [PermissionRules.GuestGroupId] });
        });
        await using var client = await TestClient.ConnectAsync(server, identity: admin);
        var modGroup = client.Welcome.Snapshot.Groups.Single(groupInfo => groupInfo.Name == "Moderator").Id;

        await client.SendAsync(new AssignGroup("offline", modGroup));
        await client.SendAsync(new ListUsers { RequestId = "l" });
        var list = await client.WaitForAsync<UserList>(userList => userList.RequestId == "l");
        Assert.Contains(modGroup, list.Users.Single(knownUser => knownUser.Fingerprint == "offline").GroupIds);

        var restarted = await server.RestartAsync();
        await using var _ = restarted;
        await using var a2 = await TestClient.ConnectAsync(restarted, identity: admin);
        await a2.SendAsync(new ListUsers { RequestId = "l2" });
        list = await a2.WaitForAsync<UserList>(userList => userList.RequestId == "l2");
        Assert.Contains(modGroup, list.Users.Single(knownUser => knownUser.Fingerprint == "offline").GroupIds);
    }

    [Fact]
    public async Task Unassign_LastAdmin_Error()
    {
        var admin = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(TestServer.Grant(admin, "Admin"));
        await using var client = await TestClient.ConnectAsync(server, identity: admin);

        await client.SendAsync(new UnassignGroup(admin.Fingerprint, PermissionRules.AdminGroupId) { RequestId = "u" });
        Assert.Equal(ReasonCodes.LastAdmin, (await client.ErrorAsync("u")).Code);
    }

    [Fact]
    public async Task Unassign_FromStrongerUser_Denied()
    {
        // Package 84: the group must be within the actor's rights and the target weaker than the actor
        var admin = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(TestServer.Grant(admin, "Admin"));
        await using var client = await TestClient.ConnectAsync(server, "admin", admin);
        await using var testClient = await TestClient.ConnectAsync(server, "zuteiler");
        await using var mod = await TestClient.ConnectAsync(server, "mod");
        await using var guest = await TestClient.ConnectAsync(server, "gast");
        var modGroup = client.Welcome.Snapshot.Groups.Single(groupInfo => groupInfo.Name == "Moderator").Id;
        await client.SendAsync(new CreateGroup("Zuteiler", PermissionRules.GuestPermissions | Permission.GroupsAssign));
        var assigner = (await client.WaitForAsync<GroupsChanged>()).Groups.Single(groupInfo => groupInfo.Name == "Zuteiler").Id;
        await client.SendAsync(new AssignGroup(testClient.Identity.Fingerprint, assigner));
        await client.SendAsync(new AssignGroup(mod.Identity.Fingerprint, modGroup));
        await client.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == mod.Id);

        await testClient.SendAsync(new UnassignGroup(mod.Identity.Fingerprint, PermissionRules.GuestGroupId) { RequestId = "stronger" });
        Assert.Equal(ReasonCodes.PermissionDenied, (await testClient.ErrorAsync("stronger")).Code);
        await testClient.SendAsync(new AssignGroup(mod.Identity.Fingerprint, assigner) { RequestId = "strongerAssign" });
        Assert.Equal(ReasonCodes.PermissionDenied, (await testClient.ErrorAsync("strongerAssign")).Code);

        // a plain guest is weaker: allowed
        await testClient.SendAsync(new AssignGroup(guest.Identity.Fingerprint, assigner));
        Assert.Equal(PermissionRules.GuestPermissions | Permission.GroupsAssign,
            (await guest.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == guest.Id)).User.Permissions); // Package 92: seen in the own entry
        await client.SendAsync(new ListUsers { RequestId = "l" });
        var users = (await client.WaitForAsync<UserList>(userList => userList.RequestId == "l")).Users;
        Assert.Equal([PermissionRules.GuestGroupId, modGroup], users.Single(knownUser => knownUser.Fingerprint == mod.Identity.Fingerprint).GroupIds);
    }

    [Fact]
    public async Task ListUsers_WithoutGroupsAssign_PermissionDenied()
    {
        await using var server = await TestServer.StartAsync();
        await using var client = await TestClient.ConnectAsync(server);
        await client.SendAsync(new ListUsers { RequestId = "l" });
        Assert.Equal(ReasonCodes.PermissionDenied, (await client.ErrorAsync("l")).Code);
    }

    [Fact]
    public async Task UpdateSettings_NewPassword_AppliesToNextHandshake()
    {
        var admin = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(TestServer.Grant(admin, "Admin"));
        await using var client = await TestClient.ConnectAsync(server, identity: admin);

        await client.SendAsync(new UpdateServerSettings("Neu", "Hallo", "pw"));
        var changed = await client.WaitForAsync<ServerSettingsChanged>();
        Assert.Equal(new ServerSettingsInfo("Neu", "Hallo", true), changed.Settings with { Limits = null });

        await using var without = await TestClient.OpenAsync(server.Port);
        Assert.Equal(ReasonCodes.WrongPassword, Assert.IsType<Rejected>(await without.HandshakeAsync("x")).Code);
        await using var with = await TestClient.OpenAsync(server.Port);
        Assert.IsType<Welcome>(await with.HandshakeAsync("y", "pw"));

        // null keeps the password, "" removes it
        await client.SendAsync(new UpdateServerSettings("Neu", "Hallo", null));
        Assert.True((await client.WaitForAsync<ServerSettingsChanged>()).Settings.HasPassword);
        await client.SendAsync(new UpdateServerSettings("Neu", "Hallo", ""));
        Assert.False((await client.WaitForAsync<ServerSettingsChanged>()).Settings.HasPassword);
    }

    [Theory]
    [InlineData("", "", ReasonCodes.InvalidName)]
    [InlineData("ok", "LONG", ReasonCodes.InvalidValue)]
    public async Task UpdateSettings_InvalidValues_Error(string name, string welcome, string code)
    {
        var admin = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(TestServer.Grant(admin, "Admin"));
        await using var client = await TestClient.ConnectAsync(server, identity: admin);

        await client.SendAsync(new UpdateServerSettings(name, welcome == "LONG" ? new string('x', 501) : welcome, null) { RequestId = "s" });
        Assert.Equal(code, (await client.ErrorAsync("s")).Code);
    }

    // ---- Package 69: limits, logs and restart ----

    static readonly ServerLimits NewLimits = new(20, 7, false, true, new TimeOnly(3, 30));

    [Fact]
    public async Task UpdateSettings_Limits_ValidatedAndBroadcast()
    {
        var admin = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(TestServer.Grant(admin, "Admin"));
        await using var client = await TestClient.ConnectAsync(server, identity: admin);
        await using var testClient = await TestClient.ConnectAsync(server);
        Assert.Equal(new ServerLimits(50, 30, true, false, new TimeOnly(4, 0)), client.Welcome.Snapshot.Settings.Limits);
        Assert.Null(testClient.Welcome.Snapshot.Settings.Limits); // only for those with the right

        await client.SendAsync(new UpdateServerSettings("Neu", "", null, NewLimits with { MaxUsers = 0 }) { RequestId = "1" });
        var error = await client.ErrorAsync("1");
        Assert.Equal((ReasonCodes.InvalidValue, "Max users: 1 to 100000."), (error.Code, error.Detail));
        await client.SendAsync(new UpdateServerSettings("Neu", "", null, NewLimits with { LogDays = 5000 }) { RequestId = "2" });
        error = await client.ErrorAsync("2");
        Assert.Equal((ReasonCodes.InvalidValue, "Keep logs: 0 to 3650 days."), (error.Code, error.Detail));
        await client.SendAsync(new UpdateServerSettings("Neu", "", new string('p', 129), NewLimits) { RequestId = "3" });
        error = await client.ErrorAsync("3");
        Assert.Equal((ReasonCodes.InvalidValue, "A password has at most 128 characters."), (error.Code, error.Detail));
        var store = new DataStore(Path.Combine(server.DataDir, DataStore.FileName));
        var unchanged = store.LoadOrCreate(() => throw new InvalidOperationException()).Settings;
        Assert.Equal(("Testserver", 50, 30), (unchanged.Name, unchanged.MaxUsers, unchanged.LogDays)); // nothing half applied

        await client.SendAsync(new UpdateServerSettings("Neu", "", null, NewLimits));
        Assert.Equal(NewLimits, (await client.WaitForAsync<ServerSettingsChanged>()).Settings.Limits);
        var seen = await testClient.WaitForAsync<ServerSettingsChanged>();
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
        await using var client = await TestClient.ConnectAsync(server, identity: admin);
        await using var testClient = await TestClient.ConnectAsync(server);
        await using var otherClient = await TestClient.ConnectAsync(server);

        await client.SendAsync(new UpdateServerSettings("S", "", null, NewLimits with { MaxUsers = 2 }));
        await client.WaitForAsync<ServerSettingsChanged>();
        Assert.Equal(3, server.State.SessionCount);

        await using var other = await TestClient.OpenAsync(server.Port);
        Assert.Equal(ReasonCodes.ServerFull, Assert.IsType<Rejected>(await other.HandshakeAsync("vierter")).Code);
        Assert.Equal(3, server.State.SessionCount);
    }

    [Fact]
    public async Task Settings_ServerConfigGranted_LimitsDelivered()
    {
        var admin = ClientIdentity.Create();
        var guest = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(TestServer.Grant(admin, "Admin"));
        await using var client = await TestClient.ConnectAsync(server, identity: admin);
        await using var testClient = await TestClient.ConnectAsync(server, identity: guest);
        await client.SendAsync(new AssignGroup(guest.Fingerprint, PermissionRules.AdminGroupId));
        Assert.NotNull((await testClient.WaitForAsync<ServerSettingsChanged>()).Settings.Limits);
    }

    // ---- Package 169: the AFK channel ----

    [Fact]
    public async Task Afk_RangeAndChannel_Checked()
    {
        var admin = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(TestServer.Grant(admin, "Admin"));
        await using var client = await TestClient.ConnectAsync(server, identity: admin);
        await using var testClient = await TestClient.ConnectAsync(server);
        var limits = client.Welcome.Snapshot.Settings.Limits!;
        Assert.Equal((null, 0), (limits.AfkChannelId, limits.AfkMinutes)); // default: the lobby, off
        Assert.Null(testClient.Welcome.Snapshot.Settings.AfkChannelId);
        await client.SendAsync(new CreateChannel("", "", Kind: ChannelKind.Separator));
        var separator = (await client.WaitForAsync<ChannelAdded>()).Channel.Id;
        await client.SendAsync(new CreateChannel("Pause", ""));
        var pause = (await client.WaitForAsync<ChannelAdded>()).Channel.Id;

        foreach (var (id, refused) in new[] { ("1", limits with { AfkMinutes = -1 }), ("2", limits with { AfkMinutes = 1441 }),
                     ("3", limits with { AfkChannelId = Guid.NewGuid(), AfkMinutes = 5 }), ("4", limits with { AfkChannelId = separator, AfkMinutes = 5 }) })
        {
            await client.SendAsync(new UpdateServerSettings("Testserver", "", null, refused) { RequestId = id });
            Assert.Equal(ReasonCodes.InvalidValue, (await client.ErrorAsync(id)).Code);
        }
        await client.SendAsync(new UpdateServerSettings("Testserver", "", null, limits with { AfkChannelId = pause, AfkMinutes = 1440 }));
        Assert.Equal(pause, (await testClient.WaitForAsync<ServerSettingsChanged>()).Settings.AfkChannelId);
        var saved = new DataStore(Path.Combine(server.DataDir, DataStore.FileName)).LoadOrCreate(() => throw new InvalidOperationException()).Settings;
        Assert.Equal((pause, 1440), (saved.AfkChannelId, saved.AfkMinutes));
    }

    [Fact]
    public async Task AfkChannel_Deleted_FallsBackToDefault()
    {
        var admin = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(TestServer.Grant(admin, "Admin"));
        await using var client = await TestClient.ConnectAsync(server, identity: admin);
        await using var testClient = await TestClient.ConnectAsync(server);
        await client.SendAsync(new CreateChannel("Pause", ""));
        var pause = (await client.WaitForAsync<ChannelAdded>()).Channel.Id;
        await client.SendAsync(new UpdateServerSettings("Testserver", "", null, client.Welcome.Snapshot.Settings.Limits! with { AfkChannelId = pause, AfkMinutes = 10 }));
        Assert.Equal(pause, (await testClient.WaitForAsync<ServerSettingsChanged>()).Settings.AfkChannelId);

        await client.SendAsync(new DeleteChannel(pause));
        Assert.Equal(client.Welcome.Snapshot.DefaultChannelId, (await testClient.WaitForAsync<ServerSettingsChanged>()).Settings.AfkChannelId);
        var limits = (await client.WaitForAsync<ServerSettingsChanged>(changed => changed.Settings.Limits?.AfkChannelId is null)).Settings.Limits!;
        Assert.Equal(10, limits.AfkMinutes); // still on, now in the lobby
    }

    [Fact]
    public async Task UpdateSettings_AsGuest_PermissionDenied()
    {
        await using var server = await TestServer.StartAsync();
        await using var client = await TestClient.ConnectAsync(server);
        await client.SendAsync(new UpdateServerSettings("x", "", null) { RequestId = "s" });
        Assert.Equal(ReasonCodes.PermissionDenied, (await client.ErrorAsync("s")).Code);
    }

    [Fact]
    public async Task AdminChanges_Restart_Survive()
    {
        var admin = ClientIdentity.Create();
        var server = await TestServer.StartAsync(TestServer.Grant(admin, "Admin"));
        await using (var client = await TestClient.ConnectAsync(server, identity: admin))
        {
            await client.SendAsync(new CreateGroup("Team", Permission.Speak));
            await client.WaitForAsync<GroupsChanged>();
            await client.SendAsync(new UpdateServerSettings("Umbenannt", "", null));
            await client.WaitForAsync<ServerSettingsChanged>();
        }
        server = await server.RestartAsync();
        await using var _ = server;
        await using var testClient = await TestClient.ConnectAsync(server);
        Assert.Contains(testClient.Welcome.Snapshot.Groups, groupInfo => groupInfo.Name == "Team");
        Assert.Equal("Umbenannt", testClient.Welcome.Snapshot.Settings.Name);
    }

    // ---- Package 37: group order ----

    static async Task<GroupServerRig> GroupServerAsync()
    {
        var adminId = ClientIdentity.Create();
        var modId = ClientIdentity.Create();
        var server = await TestServer.StartAsync(serverData =>
        {
            TestServer.Grant(adminId, "Admin")(serverData);
            TestServer.Grant(modId, "Moderator")(serverData);
        });
        return new(server, await TestClient.ConnectAsync(server, "admin", adminId), await TestClient.ConnectAsync(server, "mod", modId),
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
        var ids = admin.Welcome.Snapshot.Groups.Select(groupInfo => groupInfo.Id).Reverse().ToList();

        await admin.SendAsync(new ReorderGroups(ids));
        var changed = await guest.WaitForAsync<GroupsChanged>();
        Assert.Equal(ids, changed.Groups.Select(groupInfo => groupInfo.Id));
        Assert.Contains(server.Log, line => line.Contains("Groups reordered by admin", StringComparison.Ordinal));

        await using var late = await TestClient.ConnectAsync(server, "spaet");
        Assert.Equal(ids, late.Welcome.Snapshot.Groups.Select(groupInfo => groupInfo.Id));

        await mod.SendAsync(new KickSession(admin.Id, "") { RequestId = "k" }); // the moderator still cannot touch the admin
        Assert.Equal(ReasonCodes.PermissionDenied, (await mod.ErrorAsync("k")).Code);
    }

    [Fact]
    public async Task ReorderGroups_IncompleteOrWithoutRight_Rejected()
    {
        var (server, admin, mod, guest) = await GroupServerAsync();
        await using var _s = server;
        await using var _a = admin;
        await using var _m = mod;
        await using var _g = guest;
        var ids = admin.Welcome.Snapshot.Groups.Select(groupInfo => groupInfo.Id).ToList();

        await admin.SendAsync(new ReorderGroups(ids.Skip(1).ToList()) { RequestId = "fehlt" });
        Assert.Equal(ReasonCodes.InvalidValue, (await admin.ErrorAsync("fehlt")).Code);
        await guest.SendAsync(new ReorderGroups(ids) { RequestId = "recht" });
        Assert.Equal(ReasonCodes.PermissionDenied, (await guest.ErrorAsync("recht")).Code);
    }

    /// <summary>Package 71: the overview shows a user's active bans, also to someone who may not see the ban list.</summary>
    [Fact]
    public async Task ListUsers_ActiveBans_Included()
    {
        var viewer = ClientIdentity.Create();
        var guest = ClientIdentity.Create();
        var time = new ManualTimeProvider();
        await using var server = await TestServer.StartAsync(serverData =>
        {
            serverData.Groups.Add(new GroupRecord(Guid.NewGuid(), "Leser", Permission.UsersView));
            TestServer.Grant(viewer, "Leser")(serverData);
            serverData.Users.Add(new UserRecord { Fingerprint = guest.Fingerprint, LastNickname = "gast", GroupIds = [PermissionRules.GuestGroupId] });
            serverData.Users.Add(new UserRecord { Fingerprint = "frei", LastNickname = "Frei", GroupIds = [PermissionRules.GuestGroupId] });
            serverData.Bans.Add(new BanRecord { Id = Guid.NewGuid(), Fingerprint = guest.Fingerprint, Nickname = "gast", Reason = "aktiv", CreatedBy = "mod" });
            serverData.Bans.Add(new BanRecord { Id = Guid.NewGuid(), Fingerprint = guest.Fingerprint, Nickname = "gast", Reason = "kurz", CreatedBy = "mod",
                ExpiresAt = time.GetUtcNow().AddMinutes(5) });
        }, time: time);
        time.Advance(TimeSpan.FromMinutes(10)); // the short ban has run out
        await using var client = await TestClient.ConnectAsync(server, identity: viewer);

        await client.SendAsync(new ListUsers { RequestId = "l" });
        var users = (await client.WaitForAsync<UserList>(userList => userList.RequestId == "l")).Users;
        var ban = Assert.Single(users.Single(knownUser => knownUser.Fingerprint == guest.Fingerprint).Bans!);
        Assert.Equal(("aktiv", "mod", (DateTimeOffset?)null), (ban.Reason, ban.CreatedBy, ban.ExpiresAt));
        Assert.Empty(users.Single(knownUser => knownUser.Fingerprint == "frei").Bans ?? []);
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
    public async Task AdminRequests_WithoutTheirRight_Refused(string action, Permission right)
    {
        Assert.Equal(ReasonCodes.PermissionDenied, await ErrorOfAsync(action, Permission.All & ~right));
        Assert.NotEqual(ReasonCodes.PermissionDenied, await ErrorOfAsync(action, Permission.All));
    }

    /// <summary>
    /// Sends the request as a member of a group with exactly <paramref name="rights"/> (not the Admin group) and returns
    /// its error code, or null when it succeeded. A request that surely fails follows, so "no error" needs no timeout.
    /// </summary>
    static async Task<string?> ErrorOfAsync(string action, Permission rights)
    {
        var actor = ClientIdentity.Create();
        var target = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(serverData =>
        {
            TestServer.Grant(ClientIdentity.Create(), "Admin")(serverData); // someone else is admin, so no token is pending
            serverData.Groups.Add(new GroupRecord(Guid.NewGuid(), "Tester", rights));
            TestServer.Grant(actor, "Tester")(serverData);
        });
        await using var client = await TestClient.ConnectAsync(server, "tester", actor);
        await using var testClient = await TestClient.ConnectAsync(server, "ziel", target);
        var groups = client.Welcome.Snapshot.Groups;
        var mod = groups.Single(groupInfo => groupInfo.Name == "Moderator").Id;
        Request request = action switch
        {
            "listUsers" => new ListUsers(),
            "listBans" => new ListBans(),
            "ban" => new BanSession(testClient.Id, "x", null, false),
            "unban" => new Unban(Guid.NewGuid()),
            "kick" => new KickSession(testClient.Id, "x"),
            "assign" => new AssignGroup(target.Fingerprint, mod),
            "unassign" => new UnassignGroup(target.Fingerprint, PermissionRules.GuestGroupId),
            "createGroup" => new CreateGroup("Neu", Permission.Speak),
            "updateGroup" => new UpdateGroup(PermissionRules.GuestGroupId, "Gast", Permission.Speak),
            "reorderGroups" => new ReorderGroups(groups.Select(groupInfo => groupInfo.Id).Reverse().ToList()),
            "deleteGroup" => new DeleteGroup(mod),
            "banUser" => new BanKnownUser(target.Fingerprint, "x", null, false),
            "deleteUser" => new DeleteKnownUser(target.Fingerprint),
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };
        await client.SendAsync(request with { RequestId = "r" });
        await client.SendAsync(new RedeemAdminToken("falsch") { RequestId = "probe" });
        var error = await client.WaitForAsync<Error>(otherError => otherError.RequestId is "r" or "probe");
        return error.RequestId == "r" ? error.Code : null;
    }

    [Fact]
    public async Task GroupsCreateOnly_EditOrDelete_Refused()
    {
        var creator = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(serverData =>
        {
            serverData.Groups.Add(new GroupRecord(Guid.NewGuid(), "Anleger", Permission.GroupsCreate | Permission.Speak));
            TestServer.Grant(creator, "Anleger")(serverData);
        });
        await using var client = await TestClient.ConnectAsync(server, identity: creator);
        var ids = client.Welcome.Snapshot.Groups.Select(groupInfo => groupInfo.Id).ToList();

        await client.SendAsync(new CreateGroup("Neu", Permission.Speak));
        var created = (await client.WaitForAsync<GroupsChanged>()).Groups.Single(groupInfo => groupInfo.Name == "Neu");

        await client.SendAsync(new UpdateGroup(created.Id, "Anders", Permission.Speak) { RequestId = "u" });
        Assert.Equal(ReasonCodes.PermissionDenied, (await client.ErrorAsync("u")).Code);
        await client.SendAsync(new DeleteGroup(created.Id) { RequestId = "d" });
        Assert.Equal(ReasonCodes.PermissionDenied, (await client.ErrorAsync("d")).Code);
        await client.SendAsync(new ReorderGroups([.. ids.Append(created.Id).Reverse()]) { RequestId = "o" });
        Assert.Equal(ReasonCodes.PermissionDenied, (await client.ErrorAsync("o")).Code);
    }

    /// <summary>Package 167: a group's colour is "#RRGGBB" or none, stored in capitals and kept over a restart; anything else is refused.</summary>
    [Fact]
    public async Task GroupColor_ValidOrNot_StoredOrRefused()
    {
        var admin = ClientIdentity.Create();
        var server = await TestServer.StartAsync(TestServer.Grant(admin, "Admin"));
        Guid team;
        await using (var client = await TestClient.ConnectAsync(server, identity: admin))
        {
            Assert.All(client.Welcome.Snapshot.Groups, groupInfo => Assert.Null(groupInfo.Color)); // the groups made before have none
            // a wrong colour cannot be made as a GroupColor; sent as JSON by another client it is refused before any handler
            string[] wrongColors = ["\"red\"", "\"#12345\"", "\"#1234567\"", "\"#GG0000\"", "\"\"", "12"];
            for (var index = 0; index < wrongColors.Length; index++)
            {
                await client.SendRawAsync($$"""{"type":"createGroup","requestId":"c{{index}}","name":"Falsch","permissions":1,"color":{{wrongColors[index]}}}""");
                Assert.Equal(ReasonCodes.InvalidValue, (await client.ErrorAsync("c" + index)).Code);
            }
            await client.SendRawAsync("""{"type":"createGroup","requestId":"lower","name":"Team","permissions":1,"color":"#aa33ff"}""");
            var created = (await client.WaitForAsync<GroupsChanged>(changed => changed.Groups.Any(groupInfo => groupInfo.Name == "Team"))).Groups.Single(groupInfo => groupInfo.Name == "Team");
            team = created.Id;
            Assert.Equal(new GroupColor(0xAA, 0x33, 0xFF), created.Color); // either case is read
            await client.SendAsync(new UpdateGroup(team, "Team", Permission.Speak, new GroupColor(0, 0xFF, 0)));
            await client.WaitForAsync<GroupsChanged>(changed => changed.Groups.Any(groupInfo => groupInfo.Color == new GroupColor(0, 0xFF, 0)));
        }
        server = await server.RestartAsync();
        await using var _ = server;
        await using var again = await TestClient.ConnectAsync(server, identity: admin);
        Assert.Equal(new GroupColor(0, 0xFF, 0), again.Welcome.Snapshot.Groups.Single(groupInfo => groupInfo.Id == team).Color);
        await again.SendAsync(new UpdateGroup(team, "Team", Permission.Speak));
        await again.WaitForAsync<GroupsChanged>(changed => changed.Groups.Single(groupInfo => groupInfo.Id == team).Color is null); // no colour again
    }

    sealed record GroupServerRig(TestServer Server, TestClient Admin, TestClient Mod, TestClient Guest);
}
