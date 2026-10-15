using System.Security.Cryptography;
using OVS.Shared.Identity;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.Links;
using OVS.Shared.Protocol.Messages.ServerInfo;
using OVS.Shared.Protocol.Messages.Users;
using OVS.Shared.Voice;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server.State;

/// <summary>Package 169: with 5 AFK minutes the admin stays in the lobby (the AFK channel), the guest goes to "Raid" and idles there.</summary>
public sealed class AfkWatchTests : IAsyncLifetime
{
    readonly ClientIdentity adminId = ClientIdentity.Create();
    readonly ClientIdentity guestId = ClientIdentity.Create();
    readonly ManualTimeProvider time = new();
    TestServer server = null!;
    TestClient admin = null!, guest = null!;
    Guid lobby, raid;

    public async Task InitializeAsync()
    {
        server = await TestServer.StartAsync(serverData =>
        {
            TestServer.Grant(adminId, "Admin")(serverData);
            TestServer.Grant(guestId, "Admin")(serverData); // link speech (SpeakLinked); rights never spare anyone the AFK move
            serverData.Users.Single(user => user.Fingerprint == guestId.Fingerprint).LastNickname = "gast";
            serverData.Settings.AfkMinutes = 5;
        }, time: time);
        admin = await TestClient.ConnectAsync(server, "admin", adminId);
        guest = await TestClient.ConnectAsync(server, "gast", guestId);
        lobby = admin.Welcome.Snapshot.DefaultChannelId;
        await admin.SendAsync(new CreateChannel("Raid", ""));
        raid = (await admin.WaitForAsync<ChannelAdded>()).Channel.Id;
    }

    public async Task DisposeAsync()
    {
        await guest.DisposeAsync();
        await admin.DisposeAsync();
        await server.DisposeAsync();
    }

    async Task GuestJoinsRaidAsync()
    {
        await guest.SendAsync(new JoinChannel(raid));
        await admin.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == guest.Id && userUpdated.User.ChannelId == raid);
    }

    Task<UserUpdated> GuestMovedToLobbyAsync() =>
        admin.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == guest.Id && userUpdated.User.ChannelId == lobby);

    [Fact]
    public async Task Idle_ForMinutes_Moved()
    {
        Assert.Equal(lobby, admin.Welcome.Snapshot.Settings.AfkChannelId); // everyone knows the AFK channel
        await GuestJoinsRaidAsync();
        time.Advance(TimeSpan.FromMinutes(4));
        await admin.AssertNoMessageAsync<UserUpdated>();
        time.Advance(TimeSpan.FromMinutes(1));
        await GuestMovedToLobbyAsync(); // broadcast like a moderator's move
    }

    [Fact]
    public async Task Session_InAfkChannel_LeftAlone()
    {
        time.Advance(TimeSpan.FromMinutes(30)); // both idle in the lobby, which is the AFK channel
        await admin.AssertNoMessageAsync<UserUpdated>();
    }

    [Fact]
    public async Task Session_MutedAndIdle_StillMoved()
    {
        await GuestJoinsRaidAsync();
        await guest.SendAsync(new SetSelfState(true, true));
        await admin.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == guest.Id && userUpdated.User.SelfDeafened);
        time.Advance(TimeSpan.FromMinutes(5));
        Assert.True((await GuestMovedToLobbyAsync()).User.SelfDeafened);
    }

    [Fact]
    public async Task AfkMove_Off_NeverMoves()
    {
        var limits = admin.Welcome.Snapshot.Settings.Limits!;
        await admin.SendAsync(new UpdateServerSettings("Testserver", "", null, limits with { AfkMinutes = 0 }));
        Assert.Null((await guest.WaitForAsync<ServerSettingsChanged>()).Settings.AfkChannelId);
        await GuestJoinsRaidAsync();
        time.Advance(TimeSpan.FromMinutes(30));
        await admin.AssertNoMessageAsync<UserUpdated>();
    }

    [Fact]
    public async Task Voice_WhileIdle_ResetsIdle()
    {
        await admin.SendAsync(new LinkChannels(lobby, raid)); // the admin in the lobby hears the guest's link speech
        await guest.WaitForAsync<ChannelsLinked>();
        await GuestJoinsRaidAsync();
        using var speaker = new TestVoice(guest, server.VoiceEndPoint);
        using var listener = new TestVoice(admin, server.VoiceEndPoint);
        time.Advance(TimeSpan.FromMinutes(4));
        await speaker.HelloAsync(); // after the jump, so the voice endpoints are fresh
        await listener.HelloAsync();

        var heard = false;
        for (int attempt = 0; attempt < 5 && !heard; attempt++)
        {
            await speaker.SendAsync(PacketType.Voice, RandomNumberGenerator.GetBytes(80), VoiceHeader.TargetLinked);
            heard = await listener.ReceiveVoiceAsync(5000) is not null;
        }
        Assert.True(heard);
        time.Advance(TimeSpan.FromMinutes(4)); // 8 minutes in the channel, 4 since the voice
        await admin.AssertNoMessageAsync<UserUpdated>();
        time.Advance(TimeSpan.FromMinutes(1));
        await GuestMovedToLobbyAsync();
    }
}
