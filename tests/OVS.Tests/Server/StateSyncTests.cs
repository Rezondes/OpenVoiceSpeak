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
}
