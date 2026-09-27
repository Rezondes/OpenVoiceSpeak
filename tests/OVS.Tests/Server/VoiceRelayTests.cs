using System.Security.Cryptography;
using OVS.Shared.Identity;
using OVS.Shared.Protocol;
using OVS.Shared.Voice;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server;

/// <summary>A (admin) and B share the lobby, C sits in another channel. Real UDP on loopback.</summary>
public sealed class VoiceRelayTests : IAsyncLifetime
{
    readonly ClientIdentity adminId = ClientIdentity.Create();
    TestServer server = null!;
    TestClient a = null!, b = null!, c = null!;
    TestVoice va = null!, vb = null!, vc = null!;
    Guid other;
    static readonly byte[] Opus = RandomNumberGenerator.GetBytes(80);

    public async Task InitializeAsync()
    {
        server = await TestServer.StartAsync(TestServer.Grant(adminId, "Admin"));
        a = await TestClient.ConnectAsync(server, "a", adminId);
        b = await TestClient.ConnectAsync(server, "b");
        c = await TestClient.ConnectAsync(server, "c");
        await a.SendAsync(new CreateChannel("Other", ""));
        other = (await a.WaitForAsync<ChannelAdded>()).Channel.Id;
        await c.SendAsync(new JoinChannel(other));
        await c.WaitForAsync<UserUpdated>(u => u.User.SessionId == c.Id);

        va = new TestVoice(a, server.VoiceEndPoint);
        vb = new TestVoice(b, server.VoiceEndPoint);
        vc = new TestVoice(c, server.VoiceEndPoint);
    }

    public async Task DisposeAsync()
    {
        foreach (var v in new[] { va, vb, vc }) v.Dispose();
        foreach (var t in new[] { a, b, c }) await t.DisposeAsync();
        await server.DisposeAsync();
    }

    [Fact]
    public async Task EndToEnd_SameChannel_Received_OtherChannel_Not()
    {
        await va.HelloAsync();
        await vb.HelloAsync();
        await vc.HelloAsync();

        var packet = va.Seal(PacketType.Voice, Opus, VoiceHeader.TargetChannel);
        VoiceHeader.TryRead(packet, out var sent);
        await va.SendRawAsync(packet);

        var got = await vb.ReceiveVoiceAsync();
        Assert.NotNull(got);
        Assert.Equal(a.Id, got.Value.Header.SessionId);
        Assert.Equal(VoiceHeader.TargetChannel, got.Value.Header.Target);
        Assert.True(RelayPayload.TryParse(got.Value.Plain, out var speakerSeq, out var opus));
        Assert.Equal(sent.Seq, speakerSeq);
        Assert.Equal(Opus, opus);

        Assert.Null(await vc.ReceiveVoiceAsync(500));
    }

    [Fact]
    public async Task LinkTransmission_ReachesLinkedChannel_NormalDoesNot()
    {
        var lobby = a.Welcome.Snapshot.DefaultChannelId;
        await a.SendAsync(new LinkChannels(lobby, other));
        await c.WaitForAsync<ChannelsLinked>();
        await va.HelloAsync();
        await vc.HelloAsync();

        await va.SendAsync(PacketType.Voice, Opus, VoiceHeader.TargetChannel);
        Assert.Null(await vc.ReceiveVoiceAsync(400));

        await va.SendAsync(PacketType.Voice, Opus, VoiceHeader.TargetLinked);
        var got = await vc.ReceiveVoiceAsync();
        Assert.NotNull(got);
        Assert.Equal(VoiceHeader.TargetLinked, got.Value.Header.Target);
    }

    [Fact]
    public async Task InvalidPackets_DroppedNoResponse()
    {
        await va.HelloAsync();
        await vb.HelloAsync();

        var tampered = va.Seal(PacketType.Voice, Opus);
        tampered[^1] ^= 1;
        await va.SendRawAsync(tampered);
        Assert.Null(await vb.ReceiveVoiceAsync(300));
        Assert.Null(await va.ReceiveAsync(300));

        using var stranger = new VoiceCrypto(RandomNumberGenerator.GetBytes(32));
        await va.SendRawAsync(stranger.Seal(Direction.ClientToServer, new VoiceHeader(PacketType.Ping, 4242, 0, 0), []));
        Assert.Null(await va.ReceiveAsync(300));

        var once = va.Seal(PacketType.Voice, Opus);
        await va.SendRawAsync(once);
        await va.SendRawAsync(once);
        Assert.NotNull(await vb.ReceiveVoiceAsync());
        Assert.Null(await vb.ReceiveVoiceAsync(300));
    }

    [Fact]
    public async Task Endpoint_BoundOnlyAfterHello_UpdatesOnRebind()
    {
        await va.HelloAsync();
        await va.SendAsync(PacketType.Voice, Opus);
        Assert.Null(await vb.ReceiveVoiceAsync(300)); // b has not said hello yet

        await vb.HelloAsync();
        await va.SendAsync(PacketType.Voice, Opus);
        Assert.NotNull(await vb.ReceiveVoiceAsync());

        // b moves to a new socket (NAT rebinding), same session key, continuing sequence numbers
        using var rebound = new TestVoice(b, server.VoiceEndPoint, vb.Seq);
        await rebound.SendAsync(PacketType.Ping, []);
        Assert.NotNull(await rebound.ReceiveAsync());
        await va.SendAsync(PacketType.Voice, Opus);
        Assert.NotNull(await rebound.ReceiveVoiceAsync());
        Assert.Null(await vb.ReceiveVoiceAsync(300));
    }

    [Fact]
    public async Task ServerMutedSender_NotRelayed()
    {
        await va.HelloAsync();
        await vb.HelloAsync();
        await a.SendAsync(new SetServerMute(b.Id, true));
        await b.WaitForAsync<UserUpdated>(u => u.User.SessionId == b.Id && u.User.ServerMuted);

        await vb.SendAsync(PacketType.Voice, Opus);
        Assert.Null(await va.ReceiveVoiceAsync(400));
    }
}
