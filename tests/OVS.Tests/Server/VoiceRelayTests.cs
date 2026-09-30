using System.Net;
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

        await va.SendAsync(PacketType.Voice, Opus, VoiceHeader.TargetChannel); // frame 0

        var got = await vb.ReceiveVoiceAsync();
        Assert.NotNull(got);
        Assert.Equal(a.Id, got.Value.Header.SessionId);
        Assert.Equal(VoiceHeader.TargetChannel, got.Value.Header.Target);
        Assert.True(RelayPayload.TryParse(got.Value.Plain, out var speakerSeq, out var opus));
        Assert.Equal(0u, speakerSeq);
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
    public async Task VoiceWithoutFrameNumber_Dropped()
    {
        await va.HelloAsync();
        await vb.HelloAsync();
        // 3 bytes cannot hold [frameSeq 4][opus], so the server must drop it
        await va.SendRawAsync(va.SealRaw(PacketType.Voice, [1, 2, 3]));
        Assert.Null(await vb.ReceiveVoiceAsync(300));
    }

    /// <summary>Package 34: a muted lobby relays nothing, not even the admin's link speech; link speech into a muted channel arrives.</summary>
    [Fact]
    public async Task MutedChannel_NoFramesArrive()
    {
        var lobby = a.Welcome.Snapshot.DefaultChannelId;
        await a.SendAsync(new LinkChannels(lobby, other));
        await c.WaitForAsync<ChannelsLinked>();
        await a.SendAsync(new EditChannel(lobby, "Lobby", "", 0, IsMuted: true));
        await c.WaitForAsync<ChannelUpdated>(u => u.Channel.IsMuted);
        await va.HelloAsync();
        await vb.HelloAsync();
        await vc.HelloAsync();

        await va.SendAsync(PacketType.Voice, Opus, VoiceHeader.TargetLinked);
        Assert.Null(await vb.ReceiveVoiceAsync(400));
        Assert.Null(await vc.ReceiveVoiceAsync(100));

        // lobby open again, now "other" is muted: link speech into it is heard there
        await a.SendAsync(new EditChannel(lobby, "Lobby", "", 0, IsMuted: false));
        await a.SendAsync(new EditChannel(other, "Other", "", 1, IsMuted: true));
        await c.WaitForAsync<ChannelUpdated>(u => u.Channel.Id == other && u.Channel.IsMuted);
        await va.SendAsync(PacketType.Voice, Opus, VoiceHeader.TargetLinked);
        Assert.NotNull(await vc.ReceiveVoiceAsync());
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

    /// <summary>Package 88: a Hello from another address cannot redirect the session's voice (no reflection).</summary>
    [Fact]
    public async Task Hello_FromOtherAddress_EndpointNotChanged()
    {
        await va.HelloAsync();
        await vb.HelloAsync();
        long before = server.Voice.ForeignSourceDrops;

        // 127.0.0.2 is loopback as well, but not the address of b's control connection (127.0.0.1)
        using var spoofed = new TestVoice(b, server.VoiceEndPoint, vb.Seq, IPAddress.Parse("127.0.0.2"));
        await spoofed.SendAsync(PacketType.Hello, []);
        Assert.Null(await spoofed.ReceiveAsync(300));

        await va.SendAsync(PacketType.Voice, Opus);
        Assert.NotNull(await vb.ReceiveVoiceAsync());
        Assert.Null(await spoofed.ReceiveVoiceAsync(300));
        Assert.Equal(before + 1, server.Voice.ForeignSourceDrops);
    }

    /// <summary>Package 88: a source sending undecryptable packets is cut off before decryption; bound clients and the control channel go on.</summary>
    [Fact]
    public async Task GarbageFlood_DroppedBeforeDecrypt_ControlStaysResponsive()
    {
        await va.HelloAsync();
        // c's live session id, sealed with a wrong key, from c's own address: passes every check up to decryption
        using var stranger = new VoiceCrypto(RandomNumberGenerator.GetBytes(32));
        using var flood = new System.Net.Sockets.UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var garbage = stranger.Seal(Direction.ClientToServer, new VoiceHeader(PacketType.Ping, c.Id, 0, 0), []);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (server.Voice.PreFilterDrops == 0 && DateTime.UtcNow < deadline)
            for (int i = 0; i < 100; i++) await flood.SendAsync(garbage, server.VoiceEndPoint);
        Assert.True(server.Voice.PreFilterDrops > 0);

        await va.SendAsync(PacketType.Ping, []); // the bound endpoint of a is not held up by the flood from its address
        Assert.NotNull(await va.ReceiveAsync());
        await a.SendAsync(new CreateChannel("Nach der Flut", ""));
        await a.WaitForAsync<ChannelAdded>(m => m.Channel.Name == "Nach der Flut");
    }
}
