using System.Text.Json.Nodes;
using OVS.Server.Data;
using OVS.Server.Permissions;
using OVS.Shared.Identity;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server;

/// <summary>
/// Package 93: channels locked to groups. A is admin (in no lock group), M is moderator, R has the group "Raid",
/// G is guest only.
/// </summary>
public sealed class ChannelLockTests : IAsyncLifetime
{
    readonly ClientIdentity adminId = ClientIdentity.Create();
    readonly ClientIdentity modId = ClientIdentity.Create();
    readonly ClientIdentity raidId = ClientIdentity.Create();
    TestServer server = null!;
    TestClient a = null!, m = null!, r = null!, g = null!;
    Guid Lobby => a.Welcome.Snapshot.DefaultChannelId;
    Guid GroupId(string name) => a.Welcome.Snapshot.Groups.Single(x => x.Name == name).Id;
    Guid ModGroup => GroupId("Moderator");
    Guid RaidGroup => GroupId("Raid");

    public async Task InitializeAsync()
    {
        server = await TestServer.StartAsync(d =>
        {
            d.Groups.Insert(1, new Group(Guid.NewGuid(), "Raid", Permission.Speak));
            TestServer.Grant(adminId, "Admin")(d);
            TestServer.Grant(modId, "Moderator")(d);
            TestServer.Grant(raidId, "Raid")(d);
        });
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

    async Task JoinAsync(TestClient c, Guid channel)
    {
        await c.SendAsync(new JoinChannel(channel));
        await c.WaitForAsync<UserUpdated>(u => u.User.SessionId == c.Id && u.User.ChannelId == channel);
    }

    async Task<string> RefusedJoinAsync(TestClient c, Guid channel, string id = "j")
    {
        await c.SendAsync(new JoinChannel(channel) { RequestId = id });
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
}
