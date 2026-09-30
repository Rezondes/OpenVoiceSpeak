using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server;

public class StateSyncTests
{
    [Fact]
    public async Task Welcome_ContainsSnapshotAndNewUserInLobbyAsGuest()
    {
        await using var server = await TestServer.StartAsync();
        await using var a = await TestClient.ConnectAsync(server, "anna");
        await using var b = await TestClient.ConnectAsync(server, "bert");

        var snapshot = b.Welcome.Snapshot;
        var lobby = Assert.Single(snapshot.Channels);
        Assert.Equal("Lobby", lobby.Name);
        Assert.Equal("Testserver", snapshot.Settings.Name);
        Assert.Equal(3, snapshot.Groups.Count);
        Assert.Equal(["anna", "bert"], snapshot.Users.Select(u => u.Nickname).Order());

        var self = snapshot.Users.Single(u => u.SessionId == b.Id);
        Assert.Equal(lobby.Id, self.ChannelId);
        Assert.Equal(OVS.Server.Permissions.PermissionRules.GuestPermissions, self.Permissions);

        var joined = await a.WaitForAsync<UserJoined>();
        Assert.Equal("bert", joined.User.Nickname);
    }

    [Fact]
    public async Task NewUser_IsPersistedAsGuest()
    {
        await using var server = await TestServer.StartAsync();
        await using var a = await TestClient.ConnectAsync(server, "anna");
        server.State.FlushPendingSave(); // Package 87: logins are saved debounced
        var data = new OVS.Server.Data.DataStore(Path.Combine(server.DataDir, "server-data.json")).LoadOrCreate(() => throw new InvalidOperationException());
        var user = data.Users.Single(u => u.Fingerprint == a.Identity.Fingerprint);
        Assert.Equal("anna", user.LastNickname);
        Assert.Equal([OVS.Server.Permissions.PermissionRules.GuestGroupId], user.GroupIds);
    }

    [Fact]
    public async Task Disconnect_OthersReceiveUserLeft()
    {
        await using var server = await TestServer.StartAsync();
        await using var a = await TestClient.ConnectAsync(server);
        var b = await TestClient.ConnectAsync(server);
        var bId = b.Id;
        await b.DisposeAsync();

        Assert.Equal(bId, (await a.WaitForAsync<UserLeft>()).SessionId);
    }

    [Fact]
    public async Task SetSelfState_BroadcastsUserUpdated_DeafImpliesMute()
    {
        await using var server = await TestServer.StartAsync();
        await using var a = await TestClient.ConnectAsync(server);
        await using var b = await TestClient.ConnectAsync(server);

        await b.SendAsync(new SetSelfState(false, true));
        var update = await a.WaitForAsync<UserUpdated>(u => u.User.SessionId == b.Id);
        Assert.True(update.User.SelfDeafened);
        Assert.True(update.User.SelfMuted);
    }

    [Fact]
    public async Task Restart_KeepsCreatedChannels()
    {
        var admin = OVS.Shared.Identity.ClientIdentity.Create();
        var server = await TestServer.StartAsync(TestServer.Grant(admin, "Admin"));
        await using (var a = await TestClient.ConnectAsync(server, identity: admin))
        {
            await a.SendAsync(new CreateChannel("Raid", ""));
            await a.WaitForAsync<ChannelAdded>();
        }
        server = await server.RestartAsync();
        await using var _ = server;

        await using var b = await TestClient.ConnectAsync(server);
        Assert.Contains(b.Welcome.Snapshot.Channels, c => c.Name == "Raid");
    }

    [Fact]
    public async Task UnknownRequest_IsAnsweredWithError()
    {
        await using var server = await TestServer.StartAsync();
        await using var a = await TestClient.ConnectAsync(server);
        // Welcome is a valid message type but not a request; it is simply ignored.
        await a.SendAsync(new Pong());
        await a.SendAsync(new JoinChannel(Guid.NewGuid()) { RequestId = "j" });
        Assert.Equal(Codes.NotFound, (await a.ErrorAsync("j")).Code);
    }

    // ---- Package 92: clients get only the rights data they need ----

    static Action<OVS.Server.Data.ServerData> AddGroup(string name, Permission perms) =>
        d => d.Groups.Add(new OVS.Server.Permissions.Group(Guid.NewGuid(), name, perms));

    /// <summary>Package 92 (AC1, AC2, A104): without GroupsView no group bits, others' bits never; the own entry stays full.</summary>
    [Fact]
    public async Task Welcome_WithoutGroupsView_NoPermissionBits()
    {
        var modId = OVS.Shared.Identity.ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(TestServer.Grant(modId, "Moderator"));
        await using var mod = await TestClient.ConnectAsync(server, "mod", modId);
        await using var guest = await TestClient.ConnectAsync(server, "gast");

        var snapshot = guest.Welcome.Snapshot;
        Assert.Equal(3, snapshot.Groups.Count); // id, name and order stay
        Assert.All(snapshot.Groups, g => Assert.Equal((Permission.None, false), (g.Permissions, g.AssignableByMe)));
        var modSeen = snapshot.Users.Single(u => u.SessionId == mod.Id);
        Assert.Equal((Permission.None, false), (modSeen.Permissions, modSeen.CanBeModeratedByMe));
        Assert.Equal([snapshot.Groups.Single(g => g.Name == "Moderator").Id], modSeen.GroupIds); // the group ids stay for display
        Assert.Equal(mod.Identity.Fingerprint, modSeen.Fingerprint); // A104: public data
        var self = snapshot.Users.Single(u => u.SessionId == guest.Id);
        Assert.Equal(OVS.Server.Permissions.PermissionRules.GuestPermissions, self.Permissions);
        Assert.False(self.CanBeModeratedByMe); // never oneself

        // the moderator's view of the new guest, sent to the moderator alone
        var joined = await mod.WaitForAsync<UserJoined>();
        Assert.Equal((Permission.None, true), (joined.User.Permissions, joined.User.CanBeModeratedByMe));
        Assert.All(mod.Welcome.Snapshot.Groups, g => Assert.Equal(Permission.None, g.Permissions)); // Moderator has no GroupsView

        // later updates are per recipient too
        await guest.SendAsync(new SetSelfState(true, false));
        var seenByMod = await mod.WaitForAsync<UserUpdated>(u => u.User.SessionId == guest.Id);
        Assert.Equal((Permission.None, true), (seenByMod.User.Permissions, seenByMod.User.CanBeModeratedByMe));
        var seenBySelf = await guest.WaitForAsync<UserUpdated>(u => u.User.SessionId == guest.Id);
        Assert.Equal((OVS.Server.Permissions.PermissionRules.GuestPermissions, false), (seenBySelf.User.Permissions, seenBySelf.User.CanBeModeratedByMe));
    }

    /// <summary>Package 92 (AC1): GroupsView keeps the full groups; GroupsAssign alone learns which groups it may hand out.</summary>
    [Fact]
    public async Task GroupsView_StillSeesFullGroups()
    {
        var adminId = OVS.Shared.Identity.ClientIdentity.Create();
        var assignerId = OVS.Shared.Identity.ClientIdentity.Create();
        const Permission assignerRights = OVS.Server.Permissions.PermissionRules.GuestPermissions | Permission.UsersView | Permission.GroupsAssign;
        await using var server = await TestServer.StartAsync(d =>
        {
            TestServer.Grant(adminId, "Admin")(d);
            AddGroup("Zuweiser", assignerRights)(d);
            TestServer.Grant(assignerId, "Zuweiser")(d);
        });
        await using var admin = await TestClient.ConnectAsync(server, "admin", adminId);
        await using var assigner = await TestClient.ConnectAsync(server, "zuweiser", assignerId);

        var full = admin.Welcome.Snapshot.Groups;
        Assert.Equal(OVS.Server.Permissions.PermissionRules.ModeratorPermissions, full.Single(g => g.Name == "Moderator").Permissions);
        Assert.Equal(Permission.All, full.Single(g => g.Name == "Admin").Permissions);
        Assert.Equal(assignerRights, full.Single(g => g.Name == "Zuweiser").Permissions);
        Assert.All(full, g => Assert.True(g.AssignableByMe));

        var limited = assigner.Welcome.Snapshot.Groups;
        Assert.All(limited, g => Assert.Equal(Permission.None, g.Permissions));
        Assert.Equal(["Gast", "Zuweiser"], limited.Where(g => g.AssignableByMe).Select(g => g.Name));
        Assert.Equal(full.Select(g => (g.Id, g.Name)), limited.Select(g => (g.Id, g.Name))); // same order

        // a group change reaches each recipient in its own shape
        await admin.SendAsync(new CreateGroup("Neu", Permission.Speak));
        var forAdmin = await admin.WaitForAsync<GroupsChanged>();
        var forAssigner = await assigner.WaitForAsync<GroupsChanged>();
        Assert.Equal(Permission.Speak, forAdmin.Groups.Single(g => g.Name == "Neu").Permissions);
        var neu = forAssigner.Groups.Single(g => g.Name == "Neu");
        Assert.Equal((Permission.None, true), (neu.Permissions, neu.AssignableByMe));

        // the user list tells the requester on whom it may act
        await assigner.SendAsync(new ListUsers { RequestId = "l" });
        var list = await assigner.WaitForAsync<UserList>();
        Assert.Equal((false, false), (list.Users.Single(u => u.Fingerprint == adminId.Fingerprint).CanBeModeratedByMe,
            list.Users.Single(u => u.Fingerprint == assignerId.Fingerprint).CanBeModeratedByMe));
        await admin.SendAsync(new ListUsers { RequestId = "l" });
        var adminList = await admin.WaitForAsync<UserList>();
        Assert.True(adminList.Users.Single(u => u.Fingerprint == assignerId.Fingerprint).CanBeModeratedByMe);
    }

    /// <summary>Package 92 (AC3): flags follow rights changes of the recipient and of the target.</summary>
    [Fact]
    public async Task Flags_PerRecipient_RecomputedOnRightsChange()
    {
        const Permission guestRights = OVS.Server.Permissions.PermissionRules.GuestPermissions;
        var adminId = OVS.Shared.Identity.ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(TestServer.Grant(adminId, "Admin"));
        await using var admin = await TestClient.ConnectAsync(server, "admin", adminId);
        await using var anna = await TestClient.ConnectAsync(server, "anna");
        await using var bert = await TestClient.ConnectAsync(server, "bert");
        var modGroup = admin.Welcome.Snapshot.Groups.Single(g => g.Name == "Moderator").Id;
        Assert.False(bert.Welcome.Snapshot.Users.Single(u => u.SessionId == anna.Id).CanBeModeratedByMe);
        await anna.WaitForAsync<UserJoined>(u => u.User.SessionId == bert.Id);

        // the recipient gets stronger: anna may now act on bert
        await admin.SendAsync(new AssignGroup(anna.Identity.Fingerprint, modGroup));
        var annaSeesBert = await anna.WaitForAsync<UserUpdated>(u => u.User.SessionId == bert.Id);
        Assert.True(annaSeesBert.User.CanBeModeratedByMe);
        var bertSeesAnna = await bert.WaitForAsync<UserUpdated>(u => u.User.SessionId == anna.Id);
        Assert.Equal((Permission.None, false), (bertSeesAnna.User.Permissions, bertSeesAnna.User.CanBeModeratedByMe));
        Assert.Contains(modGroup, bertSeesAnna.User.GroupIds);
        Assert.True((await admin.WaitForAsync<UserUpdated>(u => u.User.SessionId == anna.Id)).User.CanBeModeratedByMe);

        // the target gets as strong: equal ranks cannot act on each other
        await admin.SendAsync(new AssignGroup(bert.Identity.Fingerprint, modGroup));
        annaSeesBert = await anna.WaitForAsync<UserUpdated>(u => u.User.SessionId == bert.Id);
        Assert.Equal((Permission.None, false), (annaSeesBert.User.Permissions, annaSeesBert.User.CanBeModeratedByMe));

        // the target gets weaker again
        await admin.SendAsync(new UnassignGroup(bert.Identity.Fingerprint, modGroup));
        annaSeesBert = await anna.WaitForAsync<UserUpdated>(u => u.User.SessionId == bert.Id);
        Assert.True(annaSeesBert.User.CanBeModeratedByMe);
        await bert.WaitForAsync<UserUpdated>(u => u.User.SessionId == bert.Id && !u.User.GroupIds.Contains(modGroup)); // read up to here

        // the group loses its rights: anna drops back to a guest and no longer may act on bert
        await admin.SendAsync(new UpdateGroup(modGroup, "Moderator", Permission.Speak));
        var annaSelf = await anna.WaitForAsync<UserUpdated>(u => u.User.SessionId == anna.Id && u.User.Permissions == guestRights);
        Assert.Equal((guestRights, false), (annaSelf.User.Permissions, annaSelf.User.CanBeModeratedByMe));
        annaSeesBert = await anna.WaitForAsync<UserUpdated>(u => u.User.SessionId == bert.Id);
        Assert.False(annaSeesBert.User.CanBeModeratedByMe);
        // a change that shows nothing new to an observer is not sent to it (no hint about others' rights)
        await bert.AssertNoMessageAsync<UserUpdated>();
    }
}
