using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.Groups;
using OVS.Shared.Protocol.Messages.Users;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server.State;

public class StateSyncTests
{
    [Fact]
    public async Task Welcome_NewUser_SnapshotWithUserInLobbyAsGuest()
    {
        await using var server = await TestServer.StartAsync();
        await using var client = await TestClient.ConnectAsync(server, "anna");
        await using var testClient = await TestClient.ConnectAsync(server, "bert");

        var snapshot = testClient.Welcome.Snapshot;
        var lobby = Assert.Single(snapshot.Channels);
        Assert.Equal("Lobby", lobby.Name);
        Assert.Equal("Testserver", snapshot.Settings.Name);
        Assert.Equal(4, snapshot.Groups.Count);
        Assert.Equal(["anna", "bert"], snapshot.Users.Select(user => user.Nickname).Order());

        var self = snapshot.Users.Single(user => user.SessionId == testClient.Id);
        Assert.Equal(lobby.Id, self.ChannelId);
        Assert.Equal(OVS.Server.Permissions.PermissionRules.GuestPermissions, self.Permissions);

        var joined = await client.WaitForAsync<UserJoined>();
        Assert.Equal("bert", joined.User.Nickname);
    }

    /// <summary>Package 181 (AC7): the snapshot says whether someone holds Server-Admin, before and after the token.</summary>
    [Fact]
    public async Task Snapshot_HasServerAdmin_FollowsMembers()
    {
        await using var server = await TestServer.StartAsync();
        await using var client = await TestClient.ConnectAsync(server, "anna");
        Assert.False(client.Welcome.Snapshot.HasServerAdmin);

        await client.SendAsync(new RedeemAdminToken(server.State.PendingAdminToken!));
        await client.WaitForAsync<ServerAdminClaimed>();
        await using var later = await TestClient.ConnectAsync(server, "bert");
        Assert.True(later.Welcome.Snapshot.HasServerAdmin);
    }

    [Fact]
    public async Task NewUser_Connects_PersistedAsGuest()
    {
        await using var server = await TestServer.StartAsync();
        await using var client = await TestClient.ConnectAsync(server, "anna");
        server.State.FlushPendingSave(); // Package 87: logins are saved debounced
        var data = new OVS.Server.Data.DataStore(Path.Combine(server.DataDir, "server-data.json")).LoadOrCreate(() => throw new InvalidOperationException());
        var user = data.Users.Single(userRecord => userRecord.Fingerprint == client.Identity.Fingerprint);
        Assert.Equal("anna", user.LastNickname);
        Assert.Equal([OVS.Server.Permissions.PermissionRules.GuestGroupId], user.GroupIds);
    }

    [Fact]
    public async Task Disconnect_User_OthersReceiveUserLeft()
    {
        await using var server = await TestServer.StartAsync();
        await using var client = await TestClient.ConnectAsync(server);
        var testClient = await TestClient.ConnectAsync(server);
        var bId = testClient.Id;
        await testClient.DisposeAsync();

        Assert.Equal(bId, (await client.WaitForAsync<UserLeft>()).SessionId);
    }

    [Fact]
    public async Task SetSelfState_BroadcastsUserUpdated_DeafImpliesMute()
    {
        await using var server = await TestServer.StartAsync();
        await using var client = await TestClient.ConnectAsync(server);
        await using var testClient = await TestClient.ConnectAsync(server);

        await testClient.SendAsync(new SetSelfState(false, true));
        var update = await client.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == testClient.Id);
        Assert.True(update.User.SelfDeafened);
        Assert.True(update.User.SelfMuted);
    }

    [Fact]
    public async Task Restart_CreatedChannels_Kept()
    {
        var admin = OVS.Shared.Identity.ClientIdentity.Create();
        var server = await TestServer.StartAsync(TestServer.Grant(admin, "Admin"));
        await using (var client = await TestClient.ConnectAsync(server, identity: admin))
        {
            await client.SendAsync(new CreateChannel("Raid", ""));
            await client.WaitForAsync<ChannelAdded>();
        }
        server = await server.RestartAsync();
        await using var _ = server;

        await using var testClient = await TestClient.ConnectAsync(server);
        Assert.Contains(testClient.Welcome.Snapshot.Channels, channel => channel.Name == "Raid");
    }

    /// <summary>Package 132 (A141): every client learns a channel's bitrate, it outlasts a restart; protocol version 12.</summary>
    [Fact]
    public async Task ChannelInfo_Snapshot_CarriesBitrate()
    {
        Assert.True(WireProtocol.Version >= 12); // 12 brought the bitrate; Package 133 raised it to 13 (SmokeTests pins the current one)
        var admin = OVS.Shared.Identity.ClientIdentity.Create();
        var server = await TestServer.StartAsync(TestServer.Grant(admin, "Admin"));
        await using (var client = await TestClient.ConnectAsync(server, identity: admin))
        await using (var testClient = await TestClient.ConnectAsync(server))
        {
            Assert.Equal(ProtocolDefaults.Bitrate, Assert.Single(testClient.Welcome.Snapshot.Channels).Bitrate); // the Lobby
            await client.SendAsync(new CreateChannel("Raid", "", Bitrate: 96));
            var mirror = new OVS.Client.Net.StateMirror(testClient.Welcome);
            mirror.Apply(await testClient.WaitForAsync<ChannelAdded>());
            Assert.Equal(96, mirror.Channels.Values.Single(channel => channel.Name == "Raid").Bitrate);
        }
        server = await server.RestartAsync();
        await using var _ = server;

        await using var otherClient = await TestClient.ConnectAsync(server);
        Assert.Equal(96, otherClient.Welcome.Snapshot.Channels.Single(ch => ch.Name == "Raid").Bitrate);
    }

    [Fact]
    public async Task Request_Unknown_AnsweredWithError()
    {
        await using var server = await TestServer.StartAsync();
        await using var client = await TestClient.ConnectAsync(server);
        // Welcome is a valid message type but not a request; it is simply ignored.
        await client.SendAsync(new Pong());
        await client.SendAsync(new JoinChannel(Guid.NewGuid()) { RequestId = "j" });
        Assert.Equal(ReasonCodes.NotFound, (await client.ErrorAsync("j")).Code);
    }

    // ---- Package 92: clients get only the rights data they need ----

    static Action<OVS.Server.Data.Records.ServerData> AddGroup(string name, Permission perms) =>
        serverData => serverData.Groups.Add(new OVS.Server.Permissions.GroupRecord(Guid.NewGuid(), name, perms));

    /// <summary>Package 92 (AC1, AC2, A104): without GroupsView no group bits, others' bits never; the own entry stays full.</summary>
    [Fact]
    public async Task Welcome_WithoutGroupsView_NoPermissionBits()
    {
        var modId = OVS.Shared.Identity.ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(TestServer.Grant(modId, "Moderator"));
        await using var mod = await TestClient.ConnectAsync(server, "mod", modId);
        await using var guest = await TestClient.ConnectAsync(server, "gast");

        var snapshot = guest.Welcome.Snapshot;
        Assert.Equal(4, snapshot.Groups.Count); // id, name and order stay
        Assert.All(snapshot.Groups, groupInfo => Assert.Equal((Permission.None, false), (groupInfo.Permissions, groupInfo.AssignableByMe)));
        var modSeen = snapshot.Users.Single(user => user.SessionId == mod.Id);
        Assert.Equal((Permission.None, false), (modSeen.Permissions, modSeen.CanBeModeratedByMe));
        Assert.Equal([snapshot.Groups.Single(groupInfo => groupInfo.Name == "Moderator").Id], modSeen.GroupIds); // the group ids stay for display
        Assert.Equal(mod.Identity.Fingerprint, modSeen.Fingerprint); // A104: public data
        var self = snapshot.Users.Single(user => user.SessionId == guest.Id);
        Assert.Equal(OVS.Server.Permissions.PermissionRules.GuestPermissions, self.Permissions);
        Assert.False(self.CanBeModeratedByMe); // never oneself

        // the moderator's view of the new guest, sent to the moderator alone
        var joined = await mod.WaitForAsync<UserJoined>();
        Assert.Equal((Permission.None, true), (joined.User.Permissions, joined.User.CanBeModeratedByMe));
        Assert.All(mod.Welcome.Snapshot.Groups, groupInfo => Assert.Equal(Permission.None, groupInfo.Permissions)); // Moderator has no GroupsView

        // later updates are per recipient too
        await guest.SendAsync(new SetSelfState(true, false));
        var seenByMod = await mod.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == guest.Id);
        Assert.Equal((Permission.None, true), (seenByMod.User.Permissions, seenByMod.User.CanBeModeratedByMe));
        var seenBySelf = await guest.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == guest.Id);
        Assert.Equal((OVS.Server.Permissions.PermissionRules.GuestPermissions, false), (seenBySelf.User.Permissions, seenBySelf.User.CanBeModeratedByMe));
    }

    /// <summary>Package 92 (AC1): GroupsView keeps the full groups; GroupsAssign alone learns which groups it may hand out.</summary>
    [Fact]
    public async Task Groups_ViewOnlyRight_SeesFullGroups()
    {
        var adminId = OVS.Shared.Identity.ClientIdentity.Create();
        var assignerId = OVS.Shared.Identity.ClientIdentity.Create();
        const Permission assignerRights = OVS.Server.Permissions.PermissionRules.GuestPermissions | Permission.UsersView | Permission.GroupsAssign;
        await using var server = await TestServer.StartAsync(serverData =>
        {
            TestServer.Grant(adminId, "Admin")(serverData);
            AddGroup("Zuweiser", assignerRights)(serverData);
            TestServer.Grant(assignerId, "Zuweiser")(serverData);
        });
        await using var admin = await TestClient.ConnectAsync(server, "admin", adminId);
        await using var assigner = await TestClient.ConnectAsync(server, "zuweiser", assignerId);

        var full = admin.Welcome.Snapshot.Groups;
        Assert.Equal(OVS.Server.Permissions.PermissionRules.ModeratorPermissions, full.Single(groupInfo => groupInfo.Name == "Moderator").Permissions);
        Assert.Equal(Permission.All, full.Single(groupInfo => groupInfo.Name == "Admin").Permissions);
        Assert.Equal(assignerRights, full.Single(groupInfo => groupInfo.Name == "Zuweiser").Permissions);
        Assert.All(full, groupInfo => Assert.Equal(groupInfo.Id != WellKnownGroups.ServerAdmin, groupInfo.AssignableByMe)); // Package 181: only the token gives it

        var limited = assigner.Welcome.Snapshot.Groups;
        Assert.All(limited, groupInfo => Assert.Equal(Permission.None, groupInfo.Permissions));
        Assert.Equal(["Gast", "Zuweiser"], limited.Where(groupInfo => groupInfo.AssignableByMe).Select(groupInfo => groupInfo.Name));
        Assert.Equal(full.Select(groupInfo => (groupInfo.Id, groupInfo.Name)), limited.Select(groupInfo => (groupInfo.Id, groupInfo.Name))); // same order

        // a group change reaches each recipient in its own shape
        await admin.SendAsync(new CreateGroup("Neu", Permission.Speak));
        var forAdmin = await admin.WaitForAsync<GroupsChanged>();
        var forAssigner = await assigner.WaitForAsync<GroupsChanged>();
        Assert.Equal(Permission.Speak, forAdmin.Groups.Single(groupInfo => groupInfo.Name == "Neu").Permissions);
        var neu = forAssigner.Groups.Single(groupInfo => groupInfo.Name == "Neu");
        Assert.Equal((Permission.None, true), (neu.Permissions, neu.AssignableByMe));

        // the user list tells the requester on whom it may act
        await assigner.SendAsync(new ListUsers { RequestId = "l" });
        var list = await assigner.WaitForAsync<UserList>();
        Assert.Equal((false, false), (list.Users.Single(knownUser => knownUser.Fingerprint == adminId.Fingerprint).CanBeModeratedByMe,
            list.Users.Single(knownUser => knownUser.Fingerprint == assignerId.Fingerprint).CanBeModeratedByMe));
        await admin.SendAsync(new ListUsers { RequestId = "l" });
        var adminList = await admin.WaitForAsync<UserList>();
        Assert.True(adminList.Users.Single(knownUser => knownUser.Fingerprint == assignerId.Fingerprint).CanBeModeratedByMe);
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
        var modGroup = admin.Welcome.Snapshot.Groups.Single(groupInfo => groupInfo.Name == "Moderator").Id;
        Assert.False(bert.Welcome.Snapshot.Users.Single(user => user.SessionId == anna.Id).CanBeModeratedByMe);
        await anna.WaitForAsync<UserJoined>(userJoined => userJoined.User.SessionId == bert.Id);

        // the recipient gets stronger: anna may now act on bert
        await admin.SendAsync(new AssignGroup(anna.Identity.Fingerprint, modGroup));
        var annaSeesBert = await anna.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == bert.Id);
        Assert.True(annaSeesBert.User.CanBeModeratedByMe);
        var bertSeesAnna = await bert.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == anna.Id);
        Assert.Equal((Permission.None, false), (bertSeesAnna.User.Permissions, bertSeesAnna.User.CanBeModeratedByMe));
        Assert.Contains(modGroup, bertSeesAnna.User.GroupIds);
        Assert.True((await admin.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == anna.Id)).User.CanBeModeratedByMe);

        // the target gets as strong: equal ranks cannot act on each other
        await admin.SendAsync(new AssignGroup(bert.Identity.Fingerprint, modGroup));
        annaSeesBert = await anna.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == bert.Id && userUpdated.User.GroupIds.Contains(modGroup));
        Assert.Equal((Permission.None, false), (annaSeesBert.User.Permissions, annaSeesBert.User.CanBeModeratedByMe));

        // the target gets weaker again
        await admin.SendAsync(new UnassignGroup(bert.Identity.Fingerprint, modGroup));
        annaSeesBert = await anna.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == bert.Id && !userUpdated.User.GroupIds.Contains(modGroup));
        Assert.True(annaSeesBert.User.CanBeModeratedByMe);
        await bert.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == bert.Id && !userUpdated.User.GroupIds.Contains(modGroup)); // read up to here

        // the group loses its rights: anna drops back to a guest and no longer may act on bert
        await admin.SendAsync(new UpdateGroup(modGroup, "Moderator", Permission.Speak));
        var annaSelf = await anna.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == anna.Id && userUpdated.User.Permissions == guestRights);
        Assert.Equal((guestRights, false), (annaSelf.User.Permissions, annaSelf.User.CanBeModeratedByMe));
        annaSeesBert = await anna.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == bert.Id);
        Assert.False(annaSeesBert.User.CanBeModeratedByMe);
        // a change that shows nothing new to an observer is not sent to it (no hint about others' rights)
        await bert.AssertNoMessageAsync<UserUpdated>();
    }
}
