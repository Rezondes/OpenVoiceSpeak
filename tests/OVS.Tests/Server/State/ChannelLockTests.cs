using System.Text.Json.Nodes;
using OVS.Server.Data;
using OVS.Server.Data.Records;
using OVS.Server.Permissions;
using OVS.Shared.Identity;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Framing;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.Groups;
using OVS.Shared.Protocol.Messages.Users;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server.State;

/// <summary>
/// Packages 93 and 94: channels locked to groups and with a password. A is admin (in no lock group), M is moderator
/// (with ChannelJoinFull), R has the group "Raid", B has the group "Umgeher" with ChannelPasswordBypass, G is guest only.
/// </summary>
public sealed class ChannelLockTests : IAsyncLifetime
{
    readonly ClientIdentity adminId = ClientIdentity.Create();
    readonly ClientIdentity modId = ClientIdentity.Create();
    readonly ClientIdentity raidId = ClientIdentity.Create();
    readonly ClientIdentity bypassId = ClientIdentity.Create();
    readonly ManualTimeProvider time = new();
    TestServer server = null!;
    TestClient admin = null!, moderator = null!, raider = null!, guest = null!; // B connects only where needed: 5 connections per IP
    Guid Lobby => admin.Welcome.Snapshot.DefaultChannelId;
    Guid GroupId(string name) => admin.Welcome.Snapshot.Groups.Single(groupInfo => groupInfo.Name == name).Id;
    Guid ModGroup => GroupId("Moderator");
    Guid RaidGroup => GroupId("Raid");

    public async Task InitializeAsync()
    {
        server = await TestServer.StartAsync(serverData =>
        {
            serverData.Groups.Insert(1, new GroupRecord(Guid.NewGuid(), "Raid", Permission.Speak));
            serverData.Groups.Insert(1, new GroupRecord(Guid.NewGuid(), "Umgeher", Permission.Speak | Permission.ChannelPasswordBypass));
            int mod = serverData.Groups.FindIndex(groupRecord => groupRecord.Name == "Moderator");
            serverData.Groups[mod] = serverData.Groups[mod] with { Permissions = serverData.Groups[mod].Permissions | Permission.ChannelJoinFull };
            TestServer.Grant(adminId, "Admin")(serverData);
            TestServer.Grant(modId, "Moderator")(serverData);
            TestServer.Grant(raidId, "Raid")(serverData);
            TestServer.Grant(bypassId, "Umgeher")(serverData);
        }, time: time);
        admin = await TestClient.ConnectAsync(server, "admin", adminId);
        moderator = await TestClient.ConnectAsync(server, "mod", modId);
        raider = await TestClient.ConnectAsync(server, "raider", raidId);
        guest = await TestClient.ConnectAsync(server, "gast");
    }

    public async Task DisposeAsync()
    {
        foreach (var client in new[] { admin, moderator, raider, guest }) await client.DisposeAsync();
        await server.DisposeAsync();
    }

    async Task<ChannelInfo> CreateAsync(string name, params Guid[] groups)
    {
        await admin.SendAsync(new CreateChannel(name, "", AllowedGroupIds: groups));
        return (await admin.WaitForAsync<ChannelAdded>(channelAdded => channelAdded.Channel.Name == name)).Channel;
    }

    async Task<ChannelInfo> EditAsync(ChannelInfo channel, IReadOnlyList<Guid>? groups)
    {
        await admin.SendAsync(new EditChannel(channel.Id, channel.Name, channel.Description, channel.Order, AllowedGroupIds: groups));
        return (await admin.WaitForAsync<ChannelUpdated>(channelUpdated => channelUpdated.Channel.Id == channel.Id)).Channel;
    }

    async Task<ChannelInfo> CreateWithPasswordAsync(string name, string password, int maxUsers = 0, params Guid[] groups)
    {
        await admin.SendAsync(new CreateChannel(name, "", MaxUsers: maxUsers, AllowedGroupIds: groups, Password: password));
        return (await admin.WaitForAsync<ChannelAdded>(channelAdded => channelAdded.Channel.Name == name)).Channel;
    }

    async Task JoinAsync(TestClient client, Guid channel, string? password = null)
    {
        await client.SendAsync(new JoinChannel(channel, password));
        await client.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == client.Id && userUpdated.User.ChannelId == channel);
    }

    async Task<string> RefusedJoinAsync(TestClient client, Guid channel, string id = "j", string? password = null)
    {
        await client.SendAsync(new JoinChannel(channel, password) { RequestId = id });
        return (await client.ErrorAsync(id)).Code;
    }

    async Task<Guid> ChannelOfAsync(TestClient client)
    {
        await using var late = await TestClient.ConnectAsync(server, "spaet" + Guid.NewGuid().ToString("N")[..6]);
        return late.Welcome.Snapshot.Users.Single(user => user.SessionId == client.Id).ChannelId;
    }

    [Fact]
    public async Task CreateEdit_GroupLock_Validated_NotOnDefault()
    {
        var raid = await CreateAsync("Raid", ModGroup, RaidGroup, RaidGroup);
        Assert.Equal(new[] { ModGroup, RaidGroup }.Order(), raid.AllowedGroupIds!.Order());
        Assert.Equal(new[] { ModGroup, RaidGroup }.Order(), (await guest.WaitForAsync<ChannelAdded>()).Channel.AllowedGroupIds!.Order()); // everyone sees the lock

        await admin.SendAsync(new CreateChannel("Fremd", "", AllowedGroupIds: [Guid.NewGuid()]) { RequestId = "u" });
        Assert.Equal(ReasonCodes.InvalidValue, (await admin.ErrorAsync("u")).Code);
        var lobby = admin.Welcome.Snapshot.Channels.Single(channel => channel.Id == Lobby);
        await admin.SendAsync(new EditChannel(Lobby, lobby.Name, "", lobby.Order, AllowedGroupIds: [RaidGroup]) { RequestId = "d" });
        Assert.Equal(ReasonCodes.InvalidValue, (await admin.ErrorAsync("d")).Code);

        // null keeps the lock, the empty list removes it; an empty list on create means no lock
        Assert.Equal(new[] { ModGroup, RaidGroup }.Order(), (await EditAsync(raid with { Name = "Raid 2" }, null)).AllowedGroupIds!.Order());
        Assert.Equal([RaidGroup], (await EditAsync(raid, [RaidGroup])).AllowedGroupIds);
        await using (var late = await TestClient.ConnectAsync(server, "spaet"))
            Assert.Equal([RaidGroup], late.Welcome.Snapshot.Channels.Single(channel => channel.Id == raid.Id).AllowedGroupIds);
        Assert.Null((await EditAsync(raid, [])).AllowedGroupIds);
        Assert.Null((await CreateAsync("Offen")).AllowedGroupIds);

        // rights: only ChannelCreate/ChannelEdit set a lock
        await moderator.SendAsync(new EditChannel(raid.Id, raid.Name, "", raid.Order, AllowedGroupIds: [ModGroup]) { RequestId = "p" });
        Assert.Equal(ReasonCodes.PermissionDenied, (await moderator.ErrorAsync("p")).Code);
    }

    /// <summary>Package 93: a data file from before the lock loads without a new data version, every channel open.</summary>
    [Fact]
    public void OldData_LoadsWithoutLock()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ovs-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new DataStore(Path.Combine(dir, DataStore.FileName));
            store.Save(ServerData.CreateDefault(new OVS.Server.ServerConfig(0, dir, 50, "x", "")));
            var json = JsonNode.Parse(File.ReadAllText(store.Path))!;
            foreach (var channel in json["channels"]!.AsArray()) channel!.AsObject().Remove("allowedGroupIds");
            File.WriteAllText(store.Path, json.ToJsonString());
            var loaded = store.LoadOrCreate(() => throw new InvalidOperationException());
            Assert.Null(loaded.Channels.Single().AllowedGroupIds);
            Assert.Equal(ServerData.CurrentVersion, loaded.DataVersion);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Join_RequiresOneListedGroup_AdminAlways()
    {
        var raid = await CreateAsync("Raid", ModGroup, RaidGroup);
        Assert.Equal(ReasonCodes.ChannelLocked, await RefusedJoinAsync(guest, raid.Id));
        await JoinAsync(raider, raid.Id);
        await JoinAsync(moderator, raid.Id);
        await JoinAsync(admin, raid.Id);
        Assert.Equal(Lobby, await ChannelOfAsync(guest));
    }

    [Fact]
    public async Task Move_OnlyIfMoverQualifies_TargetNotChecked()
    {
        var raidOnly = await CreateAsync("Raid", RaidGroup);
        await moderator.SendAsync(new MoveUser(guest.Id, raidOnly.Id) { RequestId = "mv" });
        Assert.Equal(ReasonCodes.ChannelLocked, (await moderator.ErrorAsync("mv")).Code); // the mover is not in Raid

        var mods = await CreateAsync("Mods", ModGroup);
        await moderator.SendAsync(new MoveUser(guest.Id, mods.Id));
        await guest.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == guest.Id && userUpdated.User.ChannelId == mods.Id); // the guest is not checked

        await admin.SendAsync(new MoveUser(guest.Id, raidOnly.Id)); // admins move anyone anywhere
        await guest.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == guest.Id && userUpdated.User.ChannelId == raidOnly.Id);
    }

    [Fact]
    public async Task Move_TargetAlreadyInLockedChannel_SilentNoOp()
    {
        var raidOnly = await CreateAsync("Raid", RaidGroup);
        await admin.SendAsync(new MoveUser(guest.Id, raidOnly.Id));
        await moderator.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == guest.Id && userUpdated.User.ChannelId == raidOnly.Id);

        // the mover could not join, but the guest is there already: no error and no broadcast
        await moderator.SendAsync(new MoveUser(guest.Id, raidOnly.Id) { RequestId = "mv" });
        await moderator.SendAsync(new MoveUser(guest.Id, Guid.NewGuid()) { RequestId = "probe" });
        while (await moderator.NextAsync() is { } message and not Error { RequestId: "probe" })
            Assert.False(message is Error { RequestId: "mv" } or UserUpdated, $"unexpected {message}");
    }

    [Fact]
    public async Task Move_TargetAlreadyInPasswordChannel_SilentNoOp()
    {
        var secret = await CreateWithPasswordAsync("Geheim", "pw123");
        await admin.SendAsync(new MoveUser(guest.Id, secret.Id));
        await moderator.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == guest.Id && userUpdated.User.ChannelId == secret.Id);

        // nobody but admins may move into a password channel, but the guest is there already: no error and no broadcast
        await moderator.SendAsync(new MoveUser(guest.Id, secret.Id) { RequestId = "mv" });
        await moderator.SendAsync(new MoveUser(guest.Id, Guid.NewGuid()) { RequestId = "probe" });
        while (await moderator.NextAsync() is { } message and not Error { RequestId: "probe" })
            Assert.False(message is Error { RequestId: "mv" } or UserUpdated, $"unexpected {message}");
    }

    [Fact]
    public async Task LockChange_NobodyMovedOut()
    {
        var open = await CreateAsync("Offen");
        await JoinAsync(guest, open.Id);
        await JoinAsync(raider, open.Id);
        var locked = await EditAsync(open, [RaidGroup]);
        Assert.Equal([RaidGroup], locked.AllowedGroupIds);

        // losing the group does not move anybody out either
        await admin.SendAsync(new UnassignGroup(raidId.Fingerprint, RaidGroup));
        await admin.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == raider.Id && !userUpdated.User.GroupIds.Contains(RaidGroup));
        await EditAsync(locked, [ModGroup]);
        Assert.Equal(open.Id, await ChannelOfAsync(guest));
        Assert.Equal(open.Id, await ChannelOfAsync(raider));
    }

    [Fact]
    public async Task GroupDeleted_RemovedFromLocks_EmptyMeansAdminsOnly()
    {
        var raid = await CreateAsync("Raid", RaidGroup);
        var both = await CreateAsync("Beide", RaidGroup, ModGroup);
        await admin.SendAsync(new DeleteGroup(RaidGroup));
        Assert.Equal([], (await guest.WaitForAsync<ChannelUpdated>(channelUpdated => channelUpdated.Channel.Id == raid.Id)).Channel.AllowedGroupIds);
        Assert.Equal([ModGroup], (await guest.WaitForAsync<ChannelUpdated>(channelUpdated => channelUpdated.Channel.Id == both.Id)).Channel.AllowedGroupIds);

        Assert.Equal(ReasonCodes.ChannelLocked, await RefusedJoinAsync(raider, raid.Id));
        Assert.Equal(ReasonCodes.ChannelLocked, await RefusedJoinAsync(moderator, raid.Id));
        await JoinAsync(admin, raid.Id);
        await using var late = await TestClient.ConnectAsync(server, "spaet");
        Assert.Equal([], late.Welcome.Snapshot.Channels.Single(channel => channel.Id == raid.Id).AllowedGroupIds);
    }

    // ---- Package 94: password lock ----

    string DataFile => File.ReadAllText(Path.Combine(server.DataDir, DataStore.FileName));

    async Task<string> ErrorOfAsync(TestClient client, Request request)
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        await client.SendAsync(request with { RequestId = id });
        return (await client.ErrorAsync(id)).Code;
    }

    [Fact]
    public async Task Password_SetChangeRemove_HashedNeverSent_NotOnDefault()
    {
        var secret = await CreateWithPasswordAsync("Geheim", "geheim1");
        Assert.True(secret.HasPassword);
        Assert.True((await guest.WaitForAsync<ChannelAdded>()).Channel.HasPassword);
        Assert.DoesNotContain("geheim1", DataFile);
        var hash = Stored(DataFile).Channels.Single(channel => channel.Id == secret.Id).StoredPassword?.Text;
        Assert.StartsWith("pbkdf2$", hash);

        var edit = new EditChannel(secret.Id, "Geheim", "", secret.Order);
        await admin.SendAsync(edit);
        Assert.True((await admin.WaitForAsync<ChannelUpdated>(channelUpdated => channelUpdated.Channel.Id == secret.Id)).Channel.HasPassword); // null keeps it
        Assert.Equal(hash, Stored(DataFile).Channels.Single(channel => channel.Id == secret.Id).StoredPassword?.Text);
        await admin.SendAsync(edit with { Password = "neu2" });
        Assert.True((await admin.WaitForAsync<ChannelUpdated>(channelUpdated => channelUpdated.Channel.Id == secret.Id)).Channel.HasPassword);
        Assert.NotEqual(hash, Stored(DataFile).Channels.Single(channel => channel.Id == secret.Id).StoredPassword?.Text);
        await JoinAsync(guest, secret.Id, "neu2");

        Assert.Equal(ReasonCodes.InvalidValue, await ErrorOfAsync(admin, edit with { Password = new string('x', ProtocolLimits.MaxPasswordLength + 1) }));
        var lobby = admin.Welcome.Snapshot.Channels.Single(channel => channel.Id == Lobby);
        Assert.Equal(ReasonCodes.InvalidValue, await ErrorOfAsync(admin, new EditChannel(Lobby, lobby.Name, "", lobby.Order, Password: "x")));
        Assert.Equal(ReasonCodes.PermissionDenied, await ErrorOfAsync(moderator, new CreateChannel("X", "", Password: "x")));

        await admin.SendAsync(edit with { Password = "" });
        Assert.False((await admin.WaitForAsync<ChannelUpdated>(channelUpdated => channelUpdated.Channel.Id == secret.Id)).Channel.HasPassword);
        Assert.Null(Stored(DataFile).Channels.Single(channel => channel.Id == secret.Id).StoredPassword?.Text);
        Assert.False((await CreateWithPasswordAsync("Offen", "")).HasPassword);
        Assert.DoesNotContain(server.Log, line => line.Contains("geheim1") || line.Contains("neu2"));
    }

    static ServerData Stored(string json) =>
        System.Text.Json.JsonSerializer.Deserialize<ServerData>(json, new System.Text.Json.JsonSerializerOptions(ProtocolJson.Options))!;

    [Fact]
    public async Task Join_PasswordRequired_WrongRefused_BypassAndAdminJoin()
    {
        var secret = await CreateWithPasswordAsync("Geheim", "geheim1");
        await using var client = await TestClient.ConnectAsync(server, "umgeher", bypassId);
        Assert.Equal(ReasonCodes.ChannelPasswordRequired, await RefusedJoinAsync(guest, secret.Id, "a"));
        Assert.Equal(ReasonCodes.WrongChannelPassword, await RefusedJoinAsync(guest, secret.Id, "b", "falsch"));
        await JoinAsync(guest, secret.Id, "geheim1");
        await JoinAsync(client, secret.Id); // the bypass right
        await JoinAsync(admin, secret.Id); // admins
        Assert.Equal(ReasonCodes.ChannelPasswordRequired, await RefusedJoinAsync(moderator, secret.Id, "c"));
    }

    [Fact]
    public async Task BothLocks_BypassSkipsOnlyPassword()
    {
        var both = await CreateWithPasswordAsync("Beide", "geheim1", 0, RaidGroup);
        await using var client = await TestClient.ConnectAsync(server, "umgeher", bypassId);
        Assert.Equal(ReasonCodes.ChannelLocked, await RefusedJoinAsync(client, both.Id, "a")); // the bypass is no group
        Assert.Equal(ReasonCodes.ChannelLocked, await RefusedJoinAsync(guest, both.Id, "b", "geheim1"));
        Assert.Equal(ReasonCodes.ChannelPasswordRequired, await RefusedJoinAsync(raider, both.Id, "c"));
        await JoinAsync(raider, both.Id, "geheim1");

        await admin.SendAsync(new AssignGroup(bypassId.Fingerprint, RaidGroup));
        await client.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == client.Id && userUpdated.User.GroupIds.Contains(RaidGroup));
        await JoinAsync(client, both.Id); // group and bypass
        await JoinAsync(admin, both.Id); // admins neither
    }

    [Fact]
    public async Task Move_IntoPasswordChannel_RefusedExceptAdmin()
    {
        var secret = await CreateWithPasswordAsync("Geheim", "geheim1");
        await JoinAsync(moderator, secret.Id, "geheim1"); // even a mover who knows it and sits inside
        Assert.Equal(ReasonCodes.ChannelPasswordRequired, await ErrorOfAsync(moderator, new MoveUser(guest.Id, secret.Id)));
        await admin.SendAsync(new MoveUser(guest.Id, secret.Id));
        await guest.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == guest.Id && userUpdated.User.ChannelId == secret.Id);
    }

    /// <summary>Package 94 (A110): admins move anyone anywhere, past group lock, password and user limit at once.</summary>
    [Fact]
    public async Task Admin_MovesGuestIntoFullGroupAndPasswordLockedChannel()
    {
        var full = await CreateWithPasswordAsync("Voll", "geheim1", 1, RaidGroup);
        await JoinAsync(raider, full.Id, "geheim1");
        Assert.Equal(ReasonCodes.ChannelLocked, await ErrorOfAsync(moderator, new MoveUser(guest.Id, full.Id))); // UserMove and ChannelJoinFull are not enough
        Assert.Equal(Lobby, await ChannelOfAsync(guest));

        await admin.SendAsync(new MoveUser(guest.Id, full.Id));
        await guest.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == guest.Id && userUpdated.User.ChannelId == full.Id);
    }

    [Fact]
    public async Task WrongPasswords_Throttled_NotLogged()
    {
        var secret = await CreateWithPasswordAsync("Geheim", "geheim1");
        var other = await CreateWithPasswordAsync("Anders", "anders1");
        for (int index = 0; index < 5; index++)
            Assert.Equal(ReasonCodes.WrongChannelPassword, await RefusedJoinAsync(guest, secret.Id, "w" + index, "falsch" + index));
        Assert.Equal(ReasonCodes.RateLimited, await RefusedJoinAsync(guest, secret.Id, "x", "geheim1")); // even the right one
        await JoinAsync(guest, other.Id, "anders1"); // per channel
        time.Advance(TimeSpan.FromMinutes(4));
        Assert.Equal(ReasonCodes.RateLimited, await RefusedJoinAsync(guest, secret.Id, "y", "geheim1"));
        time.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1));
        await JoinAsync(guest, secret.Id, "geheim1");

        Assert.Contains(server.Log, line => line.Contains("Falsches Channel-Passwort") && line.Contains("gast"));
        Assert.DoesNotContain(server.Log, line => line.Contains("falsch0") || line.Contains("geheim1") || line.Contains("anders1"));
        foreach (var file in Directory.GetFiles(Path.Combine(server.DataDir, "logs"), "*", SearchOption.AllDirectories))
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var text = new StreamReader(stream).ReadToEnd();
            Assert.DoesNotContain("falsch0", text);
            Assert.DoesNotContain("geheim1", text);
        }
    }

    [Fact]
    public async Task PasswordChange_NobodyMovedOut()
    {
        var open = await CreateAsync("Offen");
        await JoinAsync(guest, open.Id);
        await admin.SendAsync(new EditChannel(open.Id, open.Name, "", open.Order, Password: "geheim1"));
        Assert.True((await admin.WaitForAsync<ChannelUpdated>(channelUpdated => channelUpdated.Channel.Id == open.Id)).Channel.HasPassword);
        await admin.SendAsync(new EditChannel(open.Id, open.Name, "", open.Order, Password: "geheim2"));
        await admin.WaitForAsync<ChannelUpdated>(channelUpdated => channelUpdated.Channel.Id == open.Id);
        Assert.Equal(open.Id, await ChannelOfAsync(guest));
    }
}
