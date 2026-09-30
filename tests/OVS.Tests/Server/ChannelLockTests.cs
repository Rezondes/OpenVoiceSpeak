using System.Text.Json.Nodes;
using OVS.Server.Data;
using OVS.Server.Permissions;
using OVS.Shared.Identity;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server;

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
    TestClient a = null!, m = null!, r = null!, g = null!; // B connects only where needed: 5 connections per IP
    Guid Lobby => a.Welcome.Snapshot.DefaultChannelId;
    Guid GroupId(string name) => a.Welcome.Snapshot.Groups.Single(x => x.Name == name).Id;
    Guid ModGroup => GroupId("Moderator");
    Guid RaidGroup => GroupId("Raid");

    public async Task InitializeAsync()
    {
        server = await TestServer.StartAsync(d =>
        {
            d.Groups.Insert(1, new Group(Guid.NewGuid(), "Raid", Permission.Speak));
            d.Groups.Insert(1, new Group(Guid.NewGuid(), "Umgeher", Permission.Speak | Permission.ChannelPasswordBypass));
            int mod = d.Groups.FindIndex(x => x.Name == "Moderator");
            d.Groups[mod] = d.Groups[mod] with { Permissions = d.Groups[mod].Permissions | Permission.ChannelJoinFull };
            TestServer.Grant(adminId, "Admin")(d);
            TestServer.Grant(modId, "Moderator")(d);
            TestServer.Grant(raidId, "Raid")(d);
            TestServer.Grant(bypassId, "Umgeher")(d);
        }, time: time);
        a = await TestClient.ConnectAsync(server, "admin", adminId);
        m = await TestClient.ConnectAsync(server, "mod", modId);
        r = await TestClient.ConnectAsync(server, "raider", raidId);
        g = await TestClient.ConnectAsync(server, "gast");
    }

    public async Task DisposeAsync()
    {
        foreach (var c in new[] { a, m, r, g }) await c.DisposeAsync();
        await server.DisposeAsync();
    }

    async Task<ChannelInfo> CreateAsync(string name, params Guid[] groups)
    {
        await a.SendAsync(new CreateChannel(name, "", AllowedGroupIds: groups));
        return (await a.WaitForAsync<ChannelAdded>(c => c.Channel.Name == name)).Channel;
    }

    async Task<ChannelInfo> EditAsync(ChannelInfo channel, IReadOnlyList<Guid>? groups)
    {
        await a.SendAsync(new EditChannel(channel.Id, channel.Name, channel.Description, channel.Order, AllowedGroupIds: groups));
        return (await a.WaitForAsync<ChannelUpdated>(u => u.Channel.Id == channel.Id)).Channel;
    }

    async Task<ChannelInfo> CreateWithPasswordAsync(string name, string password, int maxUsers = 0, params Guid[] groups)
    {
        await a.SendAsync(new CreateChannel(name, "", MaxUsers: maxUsers, AllowedGroupIds: groups, Password: password));
        return (await a.WaitForAsync<ChannelAdded>(c => c.Channel.Name == name)).Channel;
    }

    async Task JoinAsync(TestClient c, Guid channel, string? password = null)
    {
        await c.SendAsync(new JoinChannel(channel, password));
        await c.WaitForAsync<UserUpdated>(u => u.User.SessionId == c.Id && u.User.ChannelId == channel);
    }

    async Task<string> RefusedJoinAsync(TestClient c, Guid channel, string id = "j", string? password = null)
    {
        await c.SendAsync(new JoinChannel(channel, password) { RequestId = id });
        return (await c.ErrorAsync(id)).Code;
    }

    async Task<Guid> ChannelOfAsync(TestClient c)
    {
        await using var late = await TestClient.ConnectAsync(server, "spaet" + Guid.NewGuid().ToString("N")[..6]);
        return late.Welcome.Snapshot.Users.Single(u => u.SessionId == c.Id).ChannelId;
    }

    [Fact]
    public async Task CreateEdit_GroupLock_Validated_NotOnDefault()
    {
        var raid = await CreateAsync("Raid", ModGroup, RaidGroup, RaidGroup);
        Assert.Equal(new[] { ModGroup, RaidGroup }.Order(), raid.AllowedGroupIds!.Order());
        Assert.Equal(new[] { ModGroup, RaidGroup }.Order(), (await g.WaitForAsync<ChannelAdded>()).Channel.AllowedGroupIds!.Order()); // everyone sees the lock

        await a.SendAsync(new CreateChannel("Fremd", "", AllowedGroupIds: [Guid.NewGuid()]) { RequestId = "u" });
        Assert.Equal(Codes.InvalidValue, (await a.ErrorAsync("u")).Code);
        var lobby = a.Welcome.Snapshot.Channels.Single(c => c.Id == Lobby);
        await a.SendAsync(new EditChannel(Lobby, lobby.Name, "", lobby.Order, AllowedGroupIds: [RaidGroup]) { RequestId = "d" });
        Assert.Equal(Codes.InvalidValue, (await a.ErrorAsync("d")).Code);

        // null keeps the lock, the empty list removes it; an empty list on create means no lock
        Assert.Equal(new[] { ModGroup, RaidGroup }.Order(), (await EditAsync(raid with { Name = "Raid 2" }, null)).AllowedGroupIds!.Order());
        Assert.Equal([RaidGroup], (await EditAsync(raid, [RaidGroup])).AllowedGroupIds);
        await using (var late = await TestClient.ConnectAsync(server, "spaet"))
            Assert.Equal([RaidGroup], late.Welcome.Snapshot.Channels.Single(c => c.Id == raid.Id).AllowedGroupIds);
        Assert.Null((await EditAsync(raid, [])).AllowedGroupIds);
        Assert.Null((await CreateAsync("Offen")).AllowedGroupIds);

        // rights: only ChannelCreate/ChannelEdit set a lock
        await m.SendAsync(new EditChannel(raid.Id, raid.Name, "", raid.Order, AllowedGroupIds: [ModGroup]) { RequestId = "p" });
        Assert.Equal(Codes.PermissionDenied, (await m.ErrorAsync("p")).Code);
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
        Assert.Equal(Codes.ChannelLocked, await RefusedJoinAsync(g, raid.Id));
        await JoinAsync(r, raid.Id);
        await JoinAsync(m, raid.Id);
        await JoinAsync(a, raid.Id);
        Assert.Equal(Lobby, await ChannelOfAsync(g));
    }

    [Fact]
    public async Task Move_OnlyIfMoverQualifies_TargetNotChecked()
    {
        var raidOnly = await CreateAsync("Raid", RaidGroup);
        await m.SendAsync(new MoveUser(g.Id, raidOnly.Id) { RequestId = "mv" });
        Assert.Equal(Codes.ChannelLocked, (await m.ErrorAsync("mv")).Code); // the mover is not in Raid

        var mods = await CreateAsync("Mods", ModGroup);
        await m.SendAsync(new MoveUser(g.Id, mods.Id));
        await g.WaitForAsync<UserUpdated>(u => u.User.SessionId == g.Id && u.User.ChannelId == mods.Id); // the guest is not checked

        await a.SendAsync(new MoveUser(g.Id, raidOnly.Id)); // admins move anyone anywhere
        await g.WaitForAsync<UserUpdated>(u => u.User.SessionId == g.Id && u.User.ChannelId == raidOnly.Id);
    }

    [Fact]
    public async Task Move_TargetAlreadyInLockedChannel_SilentNoOp()
    {
        var raidOnly = await CreateAsync("Raid", RaidGroup);
        await a.SendAsync(new MoveUser(g.Id, raidOnly.Id));
        await m.WaitForAsync<UserUpdated>(u => u.User.SessionId == g.Id && u.User.ChannelId == raidOnly.Id);

        // the mover could not join, but the guest is there already: no error and no broadcast
        await m.SendAsync(new MoveUser(g.Id, raidOnly.Id) { RequestId = "mv" });
        await m.SendAsync(new MoveUser(g.Id, Guid.NewGuid()) { RequestId = "probe" });
        while (await m.NextAsync() is { } message and not Error { RequestId: "probe" })
            Assert.False(message is Error { RequestId: "mv" } or UserUpdated, $"unexpected {message}");
    }

    [Fact]
    public async Task Move_TargetAlreadyInPasswordChannel_SilentNoOp()
    {
        var secret = await CreateWithPasswordAsync("Geheim", "pw123");
        await a.SendAsync(new MoveUser(g.Id, secret.Id));
        await m.WaitForAsync<UserUpdated>(u => u.User.SessionId == g.Id && u.User.ChannelId == secret.Id);

        // nobody but admins may move into a password channel, but the guest is there already: no error and no broadcast
        await m.SendAsync(new MoveUser(g.Id, secret.Id) { RequestId = "mv" });
        await m.SendAsync(new MoveUser(g.Id, Guid.NewGuid()) { RequestId = "probe" });
        while (await m.NextAsync() is { } message and not Error { RequestId: "probe" })
            Assert.False(message is Error { RequestId: "mv" } or UserUpdated, $"unexpected {message}");
    }

    [Fact]
    public async Task LockChange_NobodyMovedOut()
    {
        var open = await CreateAsync("Offen");
        await JoinAsync(g, open.Id);
        await JoinAsync(r, open.Id);
        var locked = await EditAsync(open, [RaidGroup]);
        Assert.Equal([RaidGroup], locked.AllowedGroupIds);

        // losing the group does not move anybody out either
        await a.SendAsync(new UnassignGroup(raidId.Fingerprint, RaidGroup));
        await a.WaitForAsync<UserUpdated>(u => u.User.SessionId == r.Id && !u.User.GroupIds.Contains(RaidGroup));
        await EditAsync(locked, [ModGroup]);
        Assert.Equal(open.Id, await ChannelOfAsync(g));
        Assert.Equal(open.Id, await ChannelOfAsync(r));
    }

    [Fact]
    public async Task GroupDeleted_RemovedFromLocks_EmptyMeansAdminsOnly()
    {
        var raid = await CreateAsync("Raid", RaidGroup);
        var both = await CreateAsync("Beide", RaidGroup, ModGroup);
        await a.SendAsync(new DeleteGroup(RaidGroup));
        Assert.Equal([], (await g.WaitForAsync<ChannelUpdated>(u => u.Channel.Id == raid.Id)).Channel.AllowedGroupIds);
        Assert.Equal([ModGroup], (await g.WaitForAsync<ChannelUpdated>(u => u.Channel.Id == both.Id)).Channel.AllowedGroupIds);

        Assert.Equal(Codes.ChannelLocked, await RefusedJoinAsync(r, raid.Id));
        Assert.Equal(Codes.ChannelLocked, await RefusedJoinAsync(m, raid.Id));
        await JoinAsync(a, raid.Id);
        await using var late = await TestClient.ConnectAsync(server, "spaet");
        Assert.Equal([], late.Welcome.Snapshot.Channels.Single(c => c.Id == raid.Id).AllowedGroupIds);
    }

    // ---- Package 94: password lock ----

    string DataFile => File.ReadAllText(Path.Combine(server.DataDir, DataStore.FileName));

    async Task<string> ErrorOfAsync(TestClient c, Request request)
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        await c.SendAsync(request with { RequestId = id });
        return (await c.ErrorAsync(id)).Code;
    }

    [Fact]
    public async Task Password_SetChangeRemove_HashedNeverSent_NotOnDefault()
    {
        var secret = await CreateWithPasswordAsync("Geheim", "geheim1");
        Assert.True(secret.HasPassword);
        Assert.True((await g.WaitForAsync<ChannelAdded>()).Channel.HasPassword);
        Assert.DoesNotContain("geheim1", DataFile);
        var hash = Stored(DataFile).Channels.Single(c => c.Id == secret.Id).PasswordHash;
        Assert.StartsWith("pbkdf2$", hash);

        var edit = new EditChannel(secret.Id, "Geheim", "", secret.Order);
        await a.SendAsync(edit);
        Assert.True((await a.WaitForAsync<ChannelUpdated>(u => u.Channel.Id == secret.Id)).Channel.HasPassword); // null keeps it
        Assert.Equal(hash, Stored(DataFile).Channels.Single(c => c.Id == secret.Id).PasswordHash);
        await a.SendAsync(edit with { Password = "neu2" });
        Assert.True((await a.WaitForAsync<ChannelUpdated>(u => u.Channel.Id == secret.Id)).Channel.HasPassword);
        Assert.NotEqual(hash, Stored(DataFile).Channels.Single(c => c.Id == secret.Id).PasswordHash);
        await JoinAsync(g, secret.Id, "neu2");

        Assert.Equal(Codes.InvalidValue, await ErrorOfAsync(a, edit with { Password = new string('x', ProtocolInfo.MaxPasswordLength + 1) }));
        var lobby = a.Welcome.Snapshot.Channels.Single(c => c.Id == Lobby);
        Assert.Equal(Codes.InvalidValue, await ErrorOfAsync(a, new EditChannel(Lobby, lobby.Name, "", lobby.Order, Password: "x")));
        Assert.Equal(Codes.PermissionDenied, await ErrorOfAsync(m, new CreateChannel("X", "", Password: "x")));

        await a.SendAsync(edit with { Password = "" });
        Assert.False((await a.WaitForAsync<ChannelUpdated>(u => u.Channel.Id == secret.Id)).Channel.HasPassword);
        Assert.Null(Stored(DataFile).Channels.Single(c => c.Id == secret.Id).PasswordHash);
        Assert.False((await CreateWithPasswordAsync("Offen", "")).HasPassword);
        Assert.DoesNotContain(server.Log, l => l.Contains("geheim1") || l.Contains("neu2"));
    }

    static ServerData Stored(string json) =>
        System.Text.Json.JsonSerializer.Deserialize<ServerData>(json, new System.Text.Json.JsonSerializerOptions(ProtocolJson.Options))!;

    [Fact]
    public async Task Join_PasswordRequired_WrongRefused_BypassAndAdminJoin()
    {
        var secret = await CreateWithPasswordAsync("Geheim", "geheim1");
        await using var b = await TestClient.ConnectAsync(server, "umgeher", bypassId);
        Assert.Equal(Codes.ChannelPasswordRequired, await RefusedJoinAsync(g, secret.Id, "a"));
        Assert.Equal(Codes.WrongChannelPassword, await RefusedJoinAsync(g, secret.Id, "b", "falsch"));
        await JoinAsync(g, secret.Id, "geheim1");
        await JoinAsync(b, secret.Id); // the bypass right
        await JoinAsync(a, secret.Id); // admins
        Assert.Equal(Codes.ChannelPasswordRequired, await RefusedJoinAsync(m, secret.Id, "c"));
    }

    [Fact]
    public async Task BothLocks_BypassSkipsOnlyPassword()
    {
        var both = await CreateWithPasswordAsync("Beide", "geheim1", 0, RaidGroup);
        await using var b = await TestClient.ConnectAsync(server, "umgeher", bypassId);
        Assert.Equal(Codes.ChannelLocked, await RefusedJoinAsync(b, both.Id, "a")); // the bypass is no group
        Assert.Equal(Codes.ChannelLocked, await RefusedJoinAsync(g, both.Id, "b", "geheim1"));
        Assert.Equal(Codes.ChannelPasswordRequired, await RefusedJoinAsync(r, both.Id, "c"));
        await JoinAsync(r, both.Id, "geheim1");

        await a.SendAsync(new AssignGroup(bypassId.Fingerprint, RaidGroup));
        await b.WaitForAsync<UserUpdated>(u => u.User.SessionId == b.Id && u.User.GroupIds.Contains(RaidGroup));
        await JoinAsync(b, both.Id); // group and bypass
        await JoinAsync(a, both.Id); // admins neither
    }

    [Fact]
    public async Task Move_IntoPasswordChannel_RefusedExceptAdmin()
    {
        var secret = await CreateWithPasswordAsync("Geheim", "geheim1");
        await JoinAsync(m, secret.Id, "geheim1"); // even a mover who knows it and sits inside
        Assert.Equal(Codes.ChannelPasswordRequired, await ErrorOfAsync(m, new MoveUser(g.Id, secret.Id)));
        await a.SendAsync(new MoveUser(g.Id, secret.Id));
        await g.WaitForAsync<UserUpdated>(u => u.User.SessionId == g.Id && u.User.ChannelId == secret.Id);
    }

    /// <summary>Package 94 (A110): admins move anyone anywhere, past group lock, password and user limit at once.</summary>
    [Fact]
    public async Task Admin_MovesGuestIntoFullGroupAndPasswordLockedChannel()
    {
        var full = await CreateWithPasswordAsync("Voll", "geheim1", 1, RaidGroup);
        await JoinAsync(r, full.Id, "geheim1");
        Assert.Equal(Codes.ChannelLocked, await ErrorOfAsync(m, new MoveUser(g.Id, full.Id))); // UserMove and ChannelJoinFull are not enough
        Assert.Equal(Lobby, await ChannelOfAsync(g));

        await a.SendAsync(new MoveUser(g.Id, full.Id));
        await g.WaitForAsync<UserUpdated>(u => u.User.SessionId == g.Id && u.User.ChannelId == full.Id);
    }

    [Fact]
    public async Task WrongPasswords_Throttled_NotLogged()
    {
        var secret = await CreateWithPasswordAsync("Geheim", "geheim1");
        var other = await CreateWithPasswordAsync("Anders", "anders1");
        for (int i = 0; i < 5; i++)
            Assert.Equal(Codes.WrongChannelPassword, await RefusedJoinAsync(g, secret.Id, "w" + i, "falsch" + i));
        Assert.Equal(Codes.RateLimited, await RefusedJoinAsync(g, secret.Id, "x", "geheim1")); // even the right one
        await JoinAsync(g, other.Id, "anders1"); // per channel
        time.Advance(TimeSpan.FromMinutes(4));
        Assert.Equal(Codes.RateLimited, await RefusedJoinAsync(g, secret.Id, "y", "geheim1"));
        time.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1));
        await JoinAsync(g, secret.Id, "geheim1");

        Assert.Contains(server.Log, l => l.Contains("Falsches Channel-Passwort") && l.Contains("gast"));
        Assert.DoesNotContain(server.Log, l => l.Contains("falsch0") || l.Contains("geheim1") || l.Contains("anders1"));
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
        await JoinAsync(g, open.Id);
        await a.SendAsync(new EditChannel(open.Id, open.Name, "", open.Order, Password: "geheim1"));
        Assert.True((await a.WaitForAsync<ChannelUpdated>(u => u.Channel.Id == open.Id)).Channel.HasPassword);
        await a.SendAsync(new EditChannel(open.Id, open.Name, "", open.Order, Password: "geheim2"));
        await a.WaitForAsync<ChannelUpdated>(u => u.Channel.Id == open.Id);
        Assert.Equal(open.Id, await ChannelOfAsync(g));
    }
}
