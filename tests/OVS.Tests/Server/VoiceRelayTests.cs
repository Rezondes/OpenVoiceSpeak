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

    /// <summary>
    /// Sends voice until the receiver hears it: a busy runner can drop a datagram even on loopback. Only for
    /// tests about where voice goes, not about how often (and not before a silence check on the same receiver).
    /// </summary>
    static async Task<(VoiceHeader Header, byte[] Plain)?> SendUntilHeardAsync(TestVoice from, TestVoice to, byte target = VoiceHeader.TargetChannel)
    {
        for (int i = 0; i < 5; i++)
        {
            await from.SendAsync(PacketType.Voice, Opus, target);
            if (await to.ReceiveVoiceAsync(5000) is { } got) return got;
        }
        return null;
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
        Assert.True(RelayPayload.TryParse(got.Value.Plain, out var speakerSeq, out var opus, out _));
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

        var got = await SendUntilHeardAsync(va, vc, VoiceHeader.TargetLinked);
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
        Assert.NotNull(await SendUntilHeardAsync(va, vc, VoiceHeader.TargetLinked));
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

    /// <summary>
    /// Package 133 (A142): the server counts the gaps in a sender's frame numbers per 5 s window and answers its ping with
    /// the echoed time and that uplink loss; the downlink loss the client reports stays on its session.
    /// </summary>
    [Fact]
    public async Task UplinkLoss_ReportedInPing()
    {
        var time = new ManualTimeProvider(); // 20 ms per frame, as the sender's clock would run
        await using var own = await TestServer.StartAsync(time: time);
        await using var x = await TestClient.ConnectAsync(own, "x");
        await using var y = await TestClient.ConnectAsync(own, "y");
        using var vx = new TestVoice(x, own.VoiceEndPoint);
        using var vy = new TestVoice(y, own.VoiceEndPoint);
        await vx.HelloAsync();
        await vy.HelloAsync();

        async Task Frame(uint n)
        {
            time.Advance(TimeSpan.FromMilliseconds(20));
            await vx.SendRawAsync(vx.SealRaw(PacketType.Voice, RelayPayload.Build(n, Opus)));
            Assert.NotNull(await vy.ReceiveVoiceAsync()); // relayed, so the server has counted it
        }
        async Task<PingPayload> Ping(PingPayload ping)
        {
            await vx.SendAsync(PacketType.Ping, ping.Build());
            var answer = await vx.ReceiveAsync();
            Assert.Equal(PacketType.Ping, answer!.Value.Header.Type);
            Assert.True(PingPayload.TryParse(answer.Value.Plain, out var back));
            return back;
        }

        for (uint n = 0; n < 100; n++)
            if (n % 10 != 5) await Frame(n); // every tenth lost on the way
        Assert.Equal(new PingPayload(42, new LossReport(100, 10)), await Ping(new PingPayload(42, new LossReport(50, 5))));
        Assert.Equal(new LossReport(50, 5), own.State.FindSession(x.Id)!.DownlinkLoss);

        // a talk pause of 4 s (into the next window): the frame number goes on where it stopped, so nothing is missed
        time.Advance(TimeSpan.FromSeconds(4));
        for (uint n = 100; n < 110; n++) await Frame(n);
        Assert.Equal(new LossReport(100, 10), (await Ping(new PingPayload(7, default))).Loss); // the last full window
        time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(new PingPayload(8, new LossReport(10, 0)), await Ping(new PingPayload(8, default)));
        Assert.Equal(default, own.State.FindSession(x.Id)!.DownlinkLoss);

        // an empty ping (an older test client) is still answered, empty
        await vx.SendAsync(PacketType.Ping, []);
        Assert.Empty((await vx.ReceiveAsync())!.Value.Plain);
    }

    /// <summary>
    /// Package 136 (A145): the server adds the speaker's previous frame only for listeners whose recent report shows at
    /// least 2 % downlink loss; the others get single frames, also from a sender that sends copies, and a frame lost on
    /// the way in reaches them from the sender's copy. On a manual clock, 20 ms per frame.
    /// </summary>
    [Fact]
    public async Task Server_AddsCopyPerLossyRecipient()
    {
        var time = new ManualTimeProvider();
        await using var own = await TestServer.StartAsync(time: time);
        await using var x = await TestClient.ConnectAsync(own, "x");
        await using var y = await TestClient.ConnectAsync(own, "y");
        await using var z = await TestClient.ConnectAsync(own, "z");
        await using var w = await TestClient.ConnectAsync(own, "w");
        using var vx = new TestVoice(x, own.VoiceEndPoint);
        using var vy = new TestVoice(y, own.VoiceEndPoint);
        using var vz = new TestVoice(z, own.VoiceEndPoint);
        using var vw = new TestVoice(w, own.VoiceEndPoint);
        foreach (var v in new[] { vx, vy, vz, vw }) await v.HelloAsync();

        async Task Report(TestVoice v, LossReport downlink)
        {
            await v.SendAsync(PacketType.Ping, new PingPayload(0, downlink).Build());
            Assert.NotNull(await v.ReceiveAsync());
        }
        await Report(vy, new LossReport(100, 10)); // y on bad Wi-Fi
        await Report(vz, new LossReport(100, 1));  // z almost clean; w never reported a figure

        var f = Enumerable.Range(0, 6).Select(_ => RandomNumberGenerator.GetBytes(40)).ToArray();
        async Task Send(uint n, byte[]? copy = null)
        {
            time.Advance(TimeSpan.FromMilliseconds(20));
            await vx.SendRawAsync(vx.SealRaw(PacketType.Voice, RelayPayload.Build(n, f[n], copy)));
        }
        static async Task Heard(TestVoice v, uint seq, byte[] opus, byte[]? copy)
        {
            var got = await v.ReceiveVoiceAsync();
            Assert.NotNull(got);
            Assert.True(RelayPayload.TryParse(got.Value.Plain, out var s, out var o, out var c));
            Assert.Equal(seq, s);
            Assert.Equal(opus, o);
            Assert.Equal(copy, c);
        }
        async Task HeardSingle(uint seq)
        {
            await Heard(vz, seq, f[seq], null);
            await Heard(vw, seq, f[seq], null);
        }

        await Send(0); // nothing before it to add
        await Heard(vy, 0, f[0], null);
        await HeardSingle(0);
        await Send(1); // the server adds the frame it kept
        await Heard(vy, 1, f[1], f[0]);
        await HeardSingle(1);
        await Send(2, f[1]); // the sender's copy: passed on to y, stripped for the others
        await Heard(vy, 2, f[2], f[1]);
        await HeardSingle(2);
        await Send(4, f[3]); // frame 3 lost on the way in: y gets it as the copy, the others on its own first
        await Heard(vy, 4, f[4], f[3]);
        await HeardSingle(3);
        await HeardSingle(4);
        Assert.Equal(new LossReport(5, 1), own.State.FindSession(x.Id)!.Uplink.Report()); // a copy is no frame received

        await Report(vy, default); // a figure with nothing expected (a pause) changes nothing
        await Send(5);
        await Heard(vy, 5, f[5], f[4]);
        await HeardSingle(5);

        // y's figure goes stale: no report for 11 s (its pings stopped), so single frames
        time.Advance(TimeSpan.FromSeconds(11));
        f = [.. f, .. Enumerable.Range(0, 3).Select(_ => RandomNumberGenerator.GetBytes(40))];
        await Send(6);
        await Heard(vy, 6, f[6], null);
        await HeardSingle(6);
        await Send(7, f[6]);
        await Heard(vy, 7, f[7], null);
        await HeardSingle(7);
        await Report(vy, new LossReport(100, 10)); // a fresh report: copies again
        await Send(8);
        await Heard(vy, 8, f[8], f[7]);
        await HeardSingle(8);
        Assert.Null(await vy.ReceiveVoiceAsync(300));
        Assert.Null(await vz.ReceiveVoiceAsync(10));
    }

    /// <summary>
    /// Package 136 (review): the frame before only goes along under the same routing, right after it and only if it was
    /// relayed or never arrived; the extra packet of a recovered frame costs a token of the sender's rate limit, and a
    /// recovered frame arriving late itself is not relayed again. x (admin) speaks in the lobby with y (lossy) and z;
    /// the channel Other is linked, with c (lossy) and d.
    /// </summary>
    [Fact]
    public async Task Server_Copies_SameRoute_RecentOnly_Charged()
    {
        var time = new ManualTimeProvider();
        await using var own = await TestServer.StartAsync(TestServer.Grant(adminId, "Admin"), time: time);
        await using var x = await TestClient.ConnectAsync(own, "x", adminId);
        await using var y = await TestClient.ConnectAsync(own, "y");
        await using var z = await TestClient.ConnectAsync(own, "z");
        await using var cc = await TestClient.ConnectAsync(own, "c");
        await using var d = await TestClient.ConnectAsync(own, "d");
        await x.SendAsync(new CreateChannel("Other", ""));
        var otherId = (await x.WaitForAsync<ChannelAdded>()).Channel.Id;
        foreach (var t in new[] { cc, d })
        {
            await t.SendAsync(new JoinChannel(otherId));
            await t.WaitForAsync<UserUpdated>(u => u.User.SessionId == t.Id);
        }
        await x.SendAsync(new LinkChannels(x.Welcome.Snapshot.DefaultChannelId, otherId));
        await d.WaitForAsync<ChannelsLinked>();
        using var vx = new TestVoice(x, own.VoiceEndPoint);
        using var vy = new TestVoice(y, own.VoiceEndPoint);
        using var vz = new TestVoice(z, own.VoiceEndPoint);
        using var vcc = new TestVoice(cc, own.VoiceEndPoint);
        using var vd = new TestVoice(d, own.VoiceEndPoint);
        foreach (var v in new[] { vx, vy, vz, vcc, vd }) await v.HelloAsync();
        foreach (var v in new[] { vy, vcc })
        {
            await v.SendAsync(PacketType.Ping, new PingPayload(0, new LossReport(100, 10)).Build());
            Assert.NotNull(await v.ReceiveAsync());
        }

        var f = Enumerable.Range(0, 60).Select(_ => RandomNumberGenerator.GetBytes(40)).ToArray();
        async Task Send(uint n, byte target, byte[]? copy = null, int advanceMs = 20)
        {
            time.Advance(TimeSpan.FromMilliseconds(advanceMs));
            await vx.SendRawAsync(vx.SealRaw(PacketType.Voice, RelayPayload.Build(n, f[n], copy), target));
        }
        static async Task Heard(TestVoice v, uint seq, byte[] opus, byte[]? copy)
        {
            var got = await v.ReceiveVoiceAsync();
            Assert.NotNull(got);
            Assert.True(RelayPayload.TryParse(got.Value.Plain, out var s, out var o, out var c));
            Assert.Equal(seq, s);
            Assert.Equal(opus, o);
            Assert.Equal(copy, c);
        }
        const byte Ch = VoiceHeader.TargetChannel, Linked = VoiceHeader.TargetLinked;

        await Send(0, Ch);
        await Send(1, Ch);
        await Heard(vy, 0, f[0], null);
        await Heard(vy, 1, f[1], f[0]);
        foreach (uint n in new uint[] { 0, 1 }) await Heard(vz, n, f[n], null);
        // frame 2 (for the lobby only) lost on the way in; frame 3 is linked and carries it: it reaches no one, alone or as copy
        await Send(3, Linked, f[2], 40);
        await Heard(vy, 3, f[3], null);
        await Heard(vz, 3, f[3], null);
        await Heard(vcc, 3, f[3], null);
        await Heard(vd, 3, f[3], null);
        await Send(4, Linked, f[3]); // the same routing again: the copy goes along
        await Heard(vcc, 4, f[4], f[3]);
        await Heard(vd, 4, f[4], null);
        await Heard(vy, 4, f[4], f[3]);
        await Heard(vz, 4, f[4], null);

        // a frame sent while muted arrives but is relayed to no one, so it never travels later, alone or as copy
        await x.SendAsync(new SetSelfState(true, false));
        await x.WaitForAsync<UserUpdated>(u => u.User.SessionId == x.Id && u.User.SelfMuted);
        await Send(5, Linked, f[4]);
        await vx.SendAsync(PacketType.Ping, []); // handled while still muted
        Assert.NotNull(await vx.ReceiveAsync());
        await x.SendAsync(new SetSelfState(false, false));
        await x.WaitForAsync<UserUpdated>(u => u.User.SessionId == x.Id && !u.User.SelfMuted);
        await Send(6, Linked, f[5]);
        foreach (var v in new[] { vy, vz, vcc, vd }) await Heard(v, 6, f[6], null);

        // the end of the last talk spurt (7 sent, 8 lost) is no copy for the first frame 2 s later
        await Send(7, Linked, f[6]);
        foreach (var v in new[] { vy, vcc }) await Heard(v, 7, f[7], f[6]);
        foreach (var v in new[] { vz, vd }) await Heard(v, 7, f[7], null);
        await Send(9, Linked, f[8], 2000);
        foreach (var v in new[] { vy, vz, vcc, vd }) await Heard(v, 9, f[9], null);

        // a recovered frame arriving late itself is not relayed twice
        await Send(10, Linked, f[9]);
        await Send(12, Linked, f[11], 40);
        await Send(11, Linked, null, 0);
        await vx.SendAsync(PacketType.Ping, []); // the answer shows the late 11 was handled before the clock moves on
        Assert.NotNull(await vx.ReceiveAsync());
        foreach (uint n in new uint[] { 10, 11, 12 }) await Heard(vz, n, f[n], null);
        await Heard(vy, 10, f[10], f[9]);
        await Heard(vy, 12, f[12], f[11]);
        await Heard(vcc, 10, f[10], f[9]);
        await Heard(vcc, 12, f[12], f[11]);
        foreach (uint n in new uint[] { 10, 11, 12 }) await Heard(vd, n, f[n], null);

        // a copy never crosses a move while talking: the frame before went to other listeners
        async Task Move(Guid to)
        {
            await x.SendAsync(new JoinChannel(to));
            await x.WaitForAsync<UserUpdated>(u => u.User.SessionId == x.Id && u.User.ChannelId == to);
        }
        await Send(13, Ch);
        await Heard(vy, 13, f[13], null); // the target changed from linked
        await Heard(vz, 13, f[13], null);
        await Move(otherId);
        await Send(14, Ch, f[13]); // 13 went to the lobby: no copy in Other
        await Heard(vcc, 14, f[14], null);
        await Heard(vd, 14, f[14], null);
        await Move(x.Welcome.Snapshot.DefaultChannelId);
        await Send(16, Ch, f[15]); // 15 lost, the last frame relayed (14) went to Other: neither a copy nor a packet of its own here
        await Heard(vy, 16, f[16], null);
        await Heard(vz, 16, f[16], null);

        // a hostile sender skipping every other number with copies of the skipped ones: the extra packets come out of its
        // own budget (burst 10), so a listener without copies still gets 10 packets, not 19
        time.Advance(TimeSpan.FromSeconds(1));
        for (uint n = 21; n < 40; n += 2)
        {
            await Send(n, Linked, f[n - 1], 0); // no time passes: nothing refills the budget
            if (n is > 21 and < 31) await Heard(vz, n - 1, f[n - 1], null); // recovered while tokens last
            if (n <= 31) await Heard(vz, n, f[n], null); // 31 takes the last token, its extra packet finds none
        }
        Assert.Null(await vz.ReceiveVoiceAsync(300)); // 33 to 39 over the budget
    }

    // ---- Package 141 (A150): voice over the TLS connection ----

    static Task SendTcpAsync(TestClient from, uint seq, byte[] opus, byte target = VoiceHeader.TargetChannel) =>
        from.SendAsync(new VoiceUp(seq, target, Convert.ToBase64String(opus)));

    /// <summary>An empty VoiceUp only switches the session to TCP voice (a listener that does not talk).</summary>
    static async Task SwitchToTcpAsync(TestServer on, TestClient client)
    {
        await client.SendAsync(new VoiceUp(0, VoiceHeader.TargetChannel, ""));
        await WaitUntil(() => on.State.FindSession(client.Id)!.TcpVoice);
    }

    static async Task HeardTcp(TestClient at, uint speaker, uint seq, byte[] opus, byte target = VoiceHeader.TargetChannel) =>
        Assert.Equal(new VoiceDown(speaker, seq, target, Convert.ToBase64String(opus)), await at.WaitForAsync<VoiceDown>());

    /// <summary>Package 141 (AC3): rights and routing over TCP are those of UDP (same channel, link, server mute, deafened).</summary>
    [Fact]
    public async Task Tcp_RoutingLikeUdp()
    {
        foreach (var t in new[] { a, b, c }) await SwitchToTcpAsync(server, t);

        await SendTcpAsync(a, 0, Opus);
        await HeardTcp(b, a.Id, 0, Opus);
        await c.AssertNoMessageAsync<VoiceDown>();

        await a.SendAsync(new LinkChannels(a.Welcome.Snapshot.DefaultChannelId, other));
        await c.WaitForAsync<ChannelsLinked>();
        await SendTcpAsync(a, 1, Opus, VoiceHeader.TargetLinked);
        await HeardTcp(c, a.Id, 1, Opus, VoiceHeader.TargetLinked);
        await HeardTcp(b, a.Id, 1, Opus, VoiceHeader.TargetLinked);

        await b.SendAsync(new SetSelfState(false, true));
        await a.WaitForAsync<UserUpdated>(u => u.User.SessionId == b.Id && u.User.SelfDeafened);
        await SendTcpAsync(a, 2, Opus, VoiceHeader.TargetLinked);
        await HeardTcp(c, a.Id, 2, Opus, VoiceHeader.TargetLinked);
        await b.AssertNoMessageAsync<VoiceDown>(); // deafened

        await a.SendAsync(new SetServerMute(c.Id, true));
        await c.WaitForAsync<UserUpdated>(u => u.User.SessionId == c.Id && u.User.ServerMuted);
        await SendTcpAsync(c, 0, Opus, VoiceHeader.TargetLinked);
        await a.AssertNoMessageAsync<VoiceDown>(); // server muted
        Assert.Equal(TimeSpan.FromMilliseconds(60), server.State.FindSession(a.Id)!.SpeechTime); // Package 70: relayed frames count
    }

    /// <summary>
    /// Package 141 (AC2): a UDP and a TCP client hear each other, each on its own path. A reliable leg gets no copies: a
    /// frame lost on the UDP way in reaches the TCP listener on its own from the sender's copy. A UDP Hello alone (the
    /// client trying UDP again) changes nothing; its first ping or voice packet moves its voice back to UDP, so no frame
    /// arrives twice. On a manual clock, 20 ms per frame.
    /// </summary>
    [Fact]
    public async Task TcpAndUdp_Mixed()
    {
        var time = new ManualTimeProvider();
        await using var own = await TestServer.StartAsync(time: time);
        await using var x = await TestClient.ConnectAsync(own, "x");
        await using var y = await TestClient.ConnectAsync(own, "y");
        using var vx = new TestVoice(x, own.VoiceEndPoint);
        await vx.HelloAsync();
        await SwitchToTcpAsync(own, y);
        var f = Enumerable.Range(0, 6).Select(_ => RandomNumberGenerator.GetBytes(40)).ToArray();
        async Task Send(uint n, byte[]? copy = null)
        {
            time.Advance(TimeSpan.FromMilliseconds(20));
            await vx.SendRawAsync(vx.SealRaw(PacketType.Voice, RelayPayload.Build(n, f[n], copy)));
        }

        await Send(0);
        await HeardTcp(y, x.Id, 0, f[0]);
        await Send(2, f[1]); // 1 lost on the way in
        await HeardTcp(y, x.Id, 1, f[1]);
        await HeardTcp(y, x.Id, 2, f[2]);

        var back = RandomNumberGenerator.GetBytes(60);
        await SendTcpAsync(y, 7, back);
        var got = await vx.ReceiveVoiceAsync();
        Assert.NotNull(got);
        Assert.Equal(y.Id, got.Value.Header.SessionId);
        Assert.True(RelayPayload.TryParse(got.Value.Plain, out var seq, out var opus, out var none));
        Assert.Equal(7u, seq);
        Assert.Equal(back, opus);
        Assert.Null(none);

        using var vy = new TestVoice(y, own.VoiceEndPoint);
        await vy.HelloAsync(); // UDP answers again, voice stays on TCP until the client uses it
        await Send(3);
        await HeardTcp(y, x.Id, 3, f[3]);
        await vy.SendAsync(PacketType.Ping, []);
        Assert.NotNull(await vy.ReceiveAsync());
        Assert.False(own.State.FindSession(y.Id)!.TcpVoice);
        await Send(4);
        Assert.True(RelayPayload.TryParse((await vy.ReceiveVoiceAsync())!.Value.Plain, out seq, out _, out _));
        Assert.Equal(4u, seq);
        await y.AssertNoMessageAsync<VoiceDown>();
    }

    /// <summary>Package 141: oversized, undecodable or wrongly addressed voice over TCP is dropped like a bad UDP payload, the connection stays.</summary>
    [Fact]
    public async Task Tcp_InvalidVoice_Dropped()
    {
        await SwitchToTcpAsync(server, b);
        await a.SendAsync(new VoiceUp(0, VoiceHeader.TargetChannel, Convert.ToBase64String(new byte[VoiceHeader.MaxOpusSize + 1])));
        await a.SendAsync(new VoiceUp(1, VoiceHeader.TargetChannel, "nicht base64!"));
        await a.SendAsync(new VoiceUp(2, 7, Convert.ToBase64String(Opus)));
        await b.AssertNoMessageAsync<VoiceDown>();
        await SendTcpAsync(a, 3, Opus);
        await HeardTcp(b, a.Id, 3, Opus);
    }

    /// <summary>
    /// Package 141 (review): a VoiceUp not newer than the sender's last UDP frame was queued before it switched back; it is
    /// neither relayed nor moves the session to TCP again. A newer one (also the empty switch with the next number) does.
    /// </summary>
    [Fact]
    public async Task Tcp_StaleVoiceUp_AfterUdp_Ignored()
    {
        await va.HelloAsync();
        await vb.HelloAsync();
        await va.SendAsync(PacketType.Voice, Opus); // frame 0 over UDP
        Assert.NotNull(await vb.ReceiveVoiceAsync());
        var session = server.State.FindSession(a.Id)!;

        await SendTcpAsync(a, 0, Opus);
        await a.SendAsync(new VoiceUp(0, VoiceHeader.TargetChannel, ""));
        await a.SendAsync(new Ping());
        await a.WaitForAsync<Pong>(); // the VoiceUps before it are handled
        Assert.False(session.TcpVoice);
        Assert.Null(await vb.ReceiveVoiceAsync(300));

        await SendTcpAsync(a, 1, Opus);
        await WaitUntil(() => session.TcpVoice);
        Assert.NotNull(await vb.ReceiveVoiceAsync());
    }

    static async Task WaitUntil(Func<bool> condition)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && watch.ElapsedMilliseconds < 10_000) await Task.Delay(20);
        Assert.True(condition());
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

        Assert.NotNull(await SendUntilHeardAsync(va, vb));
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
        var deadline = DateTime.UtcNow.AddSeconds(30); // ends as soon as the filter kicks in
        while (server.Voice.PreFilterDrops == 0 && DateTime.UtcNow < deadline)
            for (int i = 0; i < 100; i++) await flood.SendAsync(garbage, server.VoiceEndPoint);
        Assert.True(server.Voice.PreFilterDrops > 0);

        await va.SendAsync(PacketType.Ping, []); // the bound endpoint of a is not held up by the flood from its address
        Assert.NotNull(await va.ReceiveAsync());
        await a.SendAsync(new CreateChannel("Nach der Flut", ""));
        await a.WaitForAsync<ChannelAdded>(m => m.Channel.Name == "Nach der Flut");
    }
}
