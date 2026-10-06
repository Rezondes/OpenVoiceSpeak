using System.Net;
using System.Security.Cryptography;
using OVS.Client.Exceptions;
using OVS.Client.Net;
using OVS.Shared.Identity;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages.Moderation;
using OVS.Shared.Protocol.Messages.Users;
using OVS.Shared.Voice;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Net;

public sealed class ClientNetTests() : TempDirectoryTest("ovs-client-", quiet: false)
{
    KnownServers Known() => new(Path.Combine(dir, "known_servers.json"));

    Task<ClientConnection> Connect(TestServer server, string nick, ClientIdentity? id = null, Func<TofuPrompt, Task<bool>>? confirm = null,
        KnownServers? known = null, string? password = null) =>
        ClientConnection.ConnectAsync("127.0.0.1", server.Port, id ?? ClientIdentity.Create(), nick, password,
            known ?? Known(), confirm ?? (_ => Task.FromResult(true)));

    [Fact]
    public void KnownServers_Check_Cases()
    {
        var known = Known();
        Assert.Equal(TofuResult.Unknown, known.Check("Host", 7000, "aa"));
        known.Trust("Host", 7000, "aa");
        Assert.Equal(TofuResult.Known, Known().Check("host", 7000, "aa"));
        Assert.Equal(TofuResult.Mismatch, Known().Check("host", 7000, "bb"));
        Assert.Equal(TofuResult.Unknown, Known().Check("host", 7001, "aa"));
    }

    [Theory]
    [InlineData(TofuResult.Unknown, true)]
    [InlineData(TofuResult.Mismatch, false)] // a changed certificate must not be accepted by just pressing Enter
    public void TofuPrompt_DefaultButton(TofuResult result, bool acceptIsDefault) =>
        Assert.Equal(acceptIsDefault, new TofuPrompt("h", 1, "fp", result).AcceptIsDefault);

    [Fact]
    public void Identity_LoadOrCreate_Twice_SameFingerprint()
    {
        using var a = ClientStorage.LoadOrCreateIdentity(dir);
        using var b = ClientStorage.LoadOrCreateIdentity(dir);
        Assert.Equal(a.Fingerprint, b.Fingerprint);
    }

    [Fact]
    public async Task FirstConnect_AsksAndStores_SecondConnectSilent()
    {
        await using var server = await TestServer.StartAsync();
        var prompts = new List<TofuPrompt>();
        Task<bool> Confirm(TofuPrompt p)
        {
            prompts.Add(p);
            return Task.FromResult(true);
        }

        await (await Connect(server, "a", confirm: Confirm)).DisposeAsync();
        await (await Connect(server, "b", confirm: Confirm)).DisposeAsync();

        var prompt = Assert.Single(prompts);
        Assert.Equal(TofuResult.Unknown, prompt.Result);
        Assert.Equal(CertFingerprint.Of(server.Certificate), prompt.Fingerprint);
    }

    [Fact]
    public async Task ChangedCert_Mismatch_Refused_NoHelloSent()
    {
        await using var server = await TestServer.StartAsync();
        var known = Known();
        known.Trust("127.0.0.1", server.Port, Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)));
        TofuPrompt? seen = null;

        await Assert.ThrowsAsync<TofuRejectedException>(() => Connect(server, "a", known: known, confirm: p =>
        {
            seen = p;
            return Task.FromResult(false);
        }));
        Assert.Equal(TofuResult.Mismatch, seen!.Result);
        Assert.Equal(0, server.State.SessionCount);
        Assert.DoesNotContain(server.Log, l => l.Contains("verbunden"));
    }

    [Fact]
    public async Task Rejected_Throws_WithCode()
    {
        await using var server = await TestServer.StartAsync(password: "pw");
        var e = await Assert.ThrowsAsync<ConnectionRejectedException>(() => Connect(server, "a"));
        Assert.Equal(Codes.WrongPassword, e.Code);
    }

    [Fact]
    public async Task Connect_MirrorShowsSelfInLobby_AndSecondClient()
    {
        await using var server = await TestServer.StartAsync();
        await using var first = await Connect(server, "anna");
        var mirror = new StateMirror(first.Welcome);
        var joined = new TaskCompletionSource();
        first.MessageReceived += m =>
        {
            lock (mirror) mirror.Apply(m);
            if (m is UserJoined) joined.TrySetResult();
        };

        Assert.Equal(mirror.DefaultChannelId, mirror.Self!.ChannelId);
        await using var second = await Connect(server, "bert");
        await joined.Task.WaitAsync(TimeSpan.FromSeconds(10));
        lock (mirror) Assert.Contains(mirror.Users.Values, u => u.Nickname == "bert");
    }

    [Fact]
    public async Task ServerKick_RaisesDisconnectedWithReason()
    {
        var admin = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(TestServer.Grant(admin, "Admin"));
        await using var a = await TestClient.ConnectAsync(server, identity: admin);
        await using var victim = await Connect(server, "opfer");
        var reason = new TaskCompletionSource<(string, string?)>();
        victim.Disconnected += (r, d) => reason.TrySetResult((r, d));

        await a.SendAsync(new Kick(victim.Welcome.SessionId, "tschüss"));
        Assert.Equal((Codes.Kicked, "tschüss"), await reason.Task.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task Voice_SendFromA_ReceivedByB_Identical()
    {
        await using var server = await TestServer.StartAsync();
        await using var a = await Connect(server, "a");
        await using var b = await Connect(server, "b");
        using var va = new VoiceClient(server.VoiceEndPoint, a.Welcome.SessionId, Convert.FromBase64String(a.Welcome.VoiceKey));
        using var vb = new VoiceClient(server.VoiceEndPoint, b.Welcome.SessionId, Convert.FromBase64String(b.Welcome.VoiceKey));
        var received = new TaskCompletionSource<(uint Speaker, byte Target, byte[] Opus)>();
        vb.VoiceReceived += (speaker, _, target, opus, _) => received.TrySetResult((speaker, target, opus));
        va.Start();
        vb.Start();
        await AsyncWait.Until(() => va.Reachable && vb.Reachable);

        var opus = RandomNumberGenerator.GetBytes(70);
        va.SendVoice(opus, VoiceHeader.TargetChannel);
        var got = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(a.Welcome.SessionId, got.Speaker);
        Assert.Equal(VoiceHeader.TargetChannel, got.Target);
        Assert.Equal(opus, got.Opus);
    }

    [Fact]
    public async Task Voice_PingBetweenFrames_KeepsSpeakerSeqContiguous()
    {
        // Regression: pings used to consume the sequence the receiver's jitter buffer orders by,
        // so every keep-alive during speech looked like a lost frame (+20 ms latency each time).
        await using var server = await TestServer.StartAsync();
        await using var a = await Connect(server, "a");
        await using var b = await Connect(server, "b");
        using var va = new VoiceClient(server.VoiceEndPoint, a.Welcome.SessionId, Convert.FromBase64String(a.Welcome.VoiceKey));
        using var vb = new VoiceClient(server.VoiceEndPoint, b.Welcome.SessionId, Convert.FromBase64String(b.Welcome.VoiceKey));
        var seqs = new List<uint>();
        var two = new TaskCompletionSource();
        vb.VoiceReceived += (_, speakerSeq, _, _, _) =>
        {
            lock (seqs)
            {
                seqs.Add(speakerSeq);
                if (seqs.Count == 2) two.TrySetResult();
            }
        };
        va.Start();
        vb.Start();
        await AsyncWait.Until(() => va.Reachable && vb.Reachable);

        va.SendVoice([1, 2, 3], VoiceHeader.TargetChannel);
        va.SendPing();
        va.SendVoice([4, 5, 6], VoiceHeader.TargetChannel);
        await two.Task.WaitAsync(TimeSpan.FromSeconds(10));

        lock (seqs) Assert.Equal(seqs[0] + 1, seqs[1]);
    }

    static VoiceClient Voice(ClientConnection c, IPEndPoint to, TimeProvider? time = null) =>
        new(to, c.Welcome.SessionId, Convert.FromBase64String(c.Welcome.VoiceKey), time);

    /// <summary>Package 126: advances the voice clock in steps of the Hello retry, so every timer tick runs.</summary>
    static void Advance(ManualTimeProvider time, TimeSpan by)
    {
        for (var done = TimeSpan.Zero; done < by; done += TimeSpan.FromMilliseconds(500))
            time.Advance(TimeSpan.FromMilliseconds(500));
    }

    /// <summary>Package 126: a lost first Hello used to leave the session without voice for good.</summary>
    [Fact]
    public async Task FirstHelloLost_StillGetsVoice()
    {
        await using var server = await TestServer.StartAsync();
        await using var a = await Connect(server, "a");
        await using var b = await Connect(server, "b");
        using var relay = new LossyUdpRelay(server.VoiceEndPoint) { DropNext = 1 };
        var time = new ManualTimeProvider();
        using var va = Voice(a, relay.EndPoint, time);
        using var vb = Voice(b, server.VoiceEndPoint);
        var atA = new TaskCompletionSource<byte[]>();
        var atB = new TaskCompletionSource<byte[]>();
        va.VoiceReceived += (_, _, _, opus, _) => atA.TrySetResult(opus);
        vb.VoiceReceived += (_, _, _, opus, _) => atB.TrySetResult(opus);
        va.Start();
        vb.Start();
        await AsyncWait.Until(() => vb.Reachable && relay.Count(PacketType.Hello) == 1);
        Assert.False(va.Reachable);

        time.Advance(TimeSpan.FromMilliseconds(500)); // within 1 s: the retry
        await AsyncWait.Until(() => va.Reachable);
        va.SendVoice([1, 2, 3], VoiceHeader.TargetChannel);
        vb.SendVoice([4, 5, 6], VoiceHeader.TargetChannel);
        Assert.Equal([1, 2, 3], await atB.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal([4, 5, 6], await atA.Task.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    /// <summary>Package 126: 20 s without any packet, then the network is back.</summary>
    [Fact]
    public async Task UdpOutage_Recovers()
    {
        await using var server = await TestServer.StartAsync();
        await using var a = await Connect(server, "a");
        using var relay = new LossyUdpRelay(server.VoiceEndPoint);
        var time = new ManualTimeProvider();
        using var va = Voice(a, relay.EndPoint, time);
        va.Start();
        await AsyncWait.Until(() => va.Reachable);

        relay.DropAll = true;
        Advance(time, TimeSpan.FromSeconds(14.5));
        Assert.True(va.Reachable);
        Advance(time, TimeSpan.FromSeconds(0.5));
        Assert.False(va.Reachable);
        Advance(time, TimeSpan.FromSeconds(5));
        Assert.False(va.Reachable);
        await AsyncWait.Until(() => relay.Count(PacketType.Hello) == 11); // the first, then every 500 ms from 15 s; all dropped

        relay.DropAll = false;
        Advance(time, TimeSpan.FromSeconds(2)); // one ping interval (Package 133: 2 s)
        await AsyncWait.Until(() => va.Reachable);
    }

    /// <summary>
    /// Package 126: pings lost for 10 s must not count as lost, an overdue answer is asked again. Package 133: a ping every
    /// 2 s instead of 5 s.
    /// </summary>
    [Fact]
    public async Task TwoLostPings_StayReachable()
    {
        await using var server = await TestServer.StartAsync();
        await using var a = await Connect(server, "a");
        using var relay = new LossyUdpRelay(server.VoiceEndPoint);
        var time = new ManualTimeProvider();
        using var va = Voice(a, relay.EndPoint, time);
        va.Start();
        await AsyncWait.Until(() => va.Reachable);

        relay.DropAll = true;
        Advance(time, TimeSpan.FromSeconds(10));
        await AsyncWait.Until(() => relay.Count(PacketType.Ping) == 17); // at 2 s, then overdue every 500 ms; all dropped
        relay.DropAll = false;
        Advance(time, TimeSpan.FromSeconds(0.5));
        await AsyncWait.Until(() => relay.PassedBack == 2); // the Hello's answer and now an overdue ping's
        Advance(time, TimeSpan.FromSeconds(5.5)); // past the old 15 s mark
        Assert.True(va.Reachable);
    }

    /// <summary>Package 126: once reachable, only pings, one per ping interval (Package 133: 2 s).</summary>
    [Fact]
    public async Task Reachable_SendsOnlyPings()
    {
        await using var server = await TestServer.StartAsync();
        await using var a = await Connect(server, "a");
        using var relay = new LossyUdpRelay(server.VoiceEndPoint);
        var time = new ManualTimeProvider();
        using var va = Voice(a, relay.EndPoint, time);
        va.Start();
        await AsyncWait.Until(() => va.Reachable);

        for (int i = 1; i <= 6; i++)
        {
            Advance(time, TimeSpan.FromSeconds(1.5));
            Assert.Equal(i - 1, relay.Count(PacketType.Ping));
            Advance(time, TimeSpan.FromSeconds(0.5));
            await AsyncWait.Until(() => relay.PassedBack == i + 1); // the answer arrives before the clock moves on, as in real time
        }
        Assert.Equal(6, relay.Count(PacketType.Ping));
        Assert.Equal(1, relay.Count(PacketType.Hello));
        Assert.True(va.Reachable);
    }

    /// <summary>Package 133 (AC1): the ping carries the client's time and the server echoes it; the relay waits 50 ms each way.</summary>
    [Fact]
    public async Task VoiceRoundTrip_Measured()
    {
        await using var server = await TestServer.StartAsync();
        await using var a = await Connect(server, "a");
        var time = new ManualTimeProvider();
        using var relay = new LossyUdpRelay(server.VoiceEndPoint, time) { Delay = TimeSpan.FromMilliseconds(50) };
        using var va = Voice(a, relay.EndPoint, time);
        Assert.Null(va.RoundTrip);
        va.Start(); // the Hello carries the time as well

        async Task OneWay(int held, int ms = 50)
        {
            await AsyncWait.Until(() => relay.Held == held); // the relay holds the packet before the clock moves on
            time.Advance(TimeSpan.FromMilliseconds(ms));
        }
        await OneWay(1);
        await OneWay(2);
        await AsyncWait.Until(() => va.RoundTrip is not null);
        Assert.InRange(va.RoundTrip!.Value.TotalMilliseconds, 90, 110);
        Assert.Equal(1, va.RoundTripSample.Count);

        time.Advance(TimeSpan.FromMilliseconds(1900)); // the next ping, 2 s after the Hello
        await OneWay(3);
        await OneWay(4, 80); // a slower answer this time
        await AsyncWait.Until(() => va.RoundTrip!.Value.TotalMilliseconds > 110);
        Assert.InRange(va.RoundTrip!.Value.TotalMilliseconds, 120, 140);
        Assert.Equal(2, va.RoundTripSample.Count); // one per answer, so the baseline takes each once
    }

    /// <summary>
    /// Package 133 (AC2): 10 % of the voice is lost each way. The server counts the gaps in a's frame numbers and tells a in
    /// the ping answer (uplink); a counts the gaps in b's and tells the server in its ping (downlink). AC6: a talk pause
    /// is no loss.
    /// </summary>
    [Fact]
    public async Task DownlinkLoss_ReportedBothSides()
    {
        var time = new ManualTimeProvider(); // server and clients on one clock: 20 ms per frame, 5 s windows
        await using var server = await TestServer.StartAsync(time: time);
        await using var a = await Connect(server, "a");
        await using var b = await Connect(server, "b");
        using var relay = new LossyUdpRelay(server.VoiceEndPoint) { DropUpVoicePercent = 10, DropDownVoicePercent = 10 };
        using var va = Voice(a, relay.EndPoint, time);
        using var vb = Voice(b, server.VoiceEndPoint, time);
        int atA = 0, atB = 0;
        va.VoiceReceived += (_, _, _, _, _) => Interlocked.Increment(ref atA);
        vb.VoiceReceived += (_, _, _, _, _) => Interlocked.Increment(ref atB);
        va.Start();
        vb.Start();
        await AsyncWait.Until(() => va.Reachable && vb.Reachable);

        int sent = 0;
        async Task Talk(int frames)
        {
            for (int i = 1; i <= frames; i++)
            {
                va.SendVoice([1, 2, 3], VoiceHeader.TargetChannel);
                vb.SendVoice([4, 5, 6], VoiceHeader.TargetChannel);
                sent++;
                time.Advance(TimeSpan.FromMilliseconds(20));
                // every 5 frames everything sent has arrived, so the clock never runs ahead of the network (the server's rate
                // limit would see a pile of frames at one instant)
                if (i % 5 == 0)
                    await AsyncWait.Until(() => relay.Count(PacketType.Voice) == sent && relay.SeenDownVoice == sent
                        && Volatile.Read(ref atB) == relay.PassedUpVoice && Volatile.Read(ref atA) == relay.PassedDownVoice);
            }
        }
        await Talk(295); // about 6 s; the last frame lost is the 290th, so the end of this spurt hides no gap
        // one timer tick each (one ping), as many ticks at one instant of the server's clock would hit its rate limit
        time.Advance(TimeSpan.FromSeconds(2)); // a ping after the first full window
        var session = server.State.FindSession(a.Welcome.SessionId)!;
        await AsyncWait.Until(() => va.UplinkLoss.Expected >= 200 && session.Voice.DownlinkLoss.Expected >= 200);
        Assert.InRange(va.UplinkLoss.Percent, 7, 13);
        Assert.InRange(va.DownlinkLoss.Percent, 7, 13);
        Assert.InRange(session.Voice.DownlinkLoss.Percent, 7, 13);
        Assert.Equal(0u, vb.UplinkLoss.Missed); // b talks straight to the server, but hears what a lost on the way up
        Assert.InRange(vb.DownlinkLoss.Percent, 7, 13);

        // AC6: 10 s of silence, then talking again without loss; the frame numbers go on, so nothing counts as missed
        relay.DropUpVoicePercent = relay.DropDownVoicePercent = 0;
        time.Advance(TimeSpan.FromSeconds(10));
        await Talk(500); // 10 s, so one whole window lies inside
        time.Advance(TimeSpan.FromSeconds(2));
        await AsyncWait.Until(() => va.UplinkLoss is { Expected: >= 200, Missed: 0 } && session.Voice.DownlinkLoss is { Expected: >= 200, Missed: 0 });
        Assert.Equal(new LossReport(va.DownlinkLoss.Expected, 0), va.DownlinkLoss);
    }

    /// <summary>
    /// Package 133 (review): while I am deafened the server relays nothing to me, but the speaker's frame numbers go on.
    /// Those frames were never meant for me, so they are no downlink loss, at the client nor in its report to the server.
    /// </summary>
    [Fact]
    public async Task Deafened_FramesKeptAway_NotDownlinkLoss()
    {
        var time = new ManualTimeProvider();
        await using var server = await TestServer.StartAsync(time: time);
        await using var a = await Connect(server, "a");
        await using var b = await Connect(server, "b");
        using var va = Voice(a, server.VoiceEndPoint, time);
        using var vb = Voice(b, server.VoiceEndPoint, time);
        int atA = 0, sent = 0;
        va.VoiceReceived += (_, _, _, _, _) => Interlocked.Increment(ref atA);
        va.Start();
        vb.Start();
        await AsyncWait.Until(() => va.Reachable && vb.Reachable);
        var session = server.State.FindSession(a.Welcome.SessionId)!;

        async Task Talk(int frames, bool heard)
        {
            for (int i = 1; i <= frames; i++)
            {
                vb.SendVoice([4, 5, 6], VoiceHeader.TargetChannel);
                sent++;
                time.Advance(TimeSpan.FromMilliseconds(20));
                if (i % 5 != 0) continue;
                if (heard) await AsyncWait.Until(() => Volatile.Read(ref atA) == sent);
                else await Task.Delay(10); // nothing arrives to wait for; keeps the server's clock from running ahead
            }
        }
        async Task Deafen(bool on)
        {
            await a.SendAsync(new SetSelfState(false, on));
            await AsyncWait.Until(() => session.SelfDeafened == on);
        }

        await Talk(50, heard: true); // 1 s
        await Deafen(true);
        int before = atA;
        await Talk(100, heard: false); // 2 s that b's frames go to nobody
        Assert.Equal(before, Volatile.Read(ref atA));
        await Deafen(false);
        sent = atA; // what a can hear from here on
        await Talk(110, heard: true); // past 5 s: the window with the jump in the frame numbers is complete
        time.Advance(TimeSpan.FromSeconds(2)); // a ping with that window
        await AsyncWait.Until(() => session.Voice.DownlinkLoss.Expected >= 100);
        Assert.Equal(0u, session.Voice.DownlinkLoss.Missed);
        Assert.Equal(0u, va.DownlinkLoss.Missed);
        Assert.True(va.DownlinkLoss.Expected >= 100);
    }

    // ---- Package 141 (A150): voice over the TLS connection ----

    static VoiceClient TcpCapable(ClientConnection c, IPEndPoint to, TimeProvider time) =>
        new(to, c.Welcome.SessionId, Convert.FromBase64String(c.Welcome.VoiceKey), time, c);

    /// <summary>Package 141 (AC1, AC6): with all UDP blocked, voice goes over the control connection 5 s after the start.</summary>
    [Fact]
    public async Task UdpBlocked_VoiceOverTcp()
    {
        Assert.Equal(15, ProtocolInfo.Version);
        await using var server = await TestServer.StartAsync();
        await using var a = await Connect(server, "a");
        await using var b = await Connect(server, "b");
        using var relayA = new LossyUdpRelay(server.VoiceEndPoint) { DropAll = true };
        using var relayB = new LossyUdpRelay(server.VoiceEndPoint) { DropAll = true };
        var time = new ManualTimeProvider();
        using var va = TcpCapable(a, relayA.EndPoint, time);
        using var vb = TcpCapable(b, relayB.EndPoint, time);
        var atA = new TaskCompletionSource<(uint, uint, byte[])>();
        var atB = new TaskCompletionSource<(uint, uint, byte[])>();
        va.VoiceReceived += (speaker, seq, _, opus, _) => atA.TrySetResult((speaker, seq, opus));
        vb.VoiceReceived += (speaker, seq, _, opus, _) => atB.TrySetResult((speaker, seq, opus));
        va.Start();
        vb.Start();

        Advance(time, TimeSpan.FromSeconds(4.5));
        Assert.False(va.OverTcp);
        Advance(time, TimeSpan.FromSeconds(0.5));
        Assert.True(va.OverTcp && vb.OverTcp);
        Assert.False(va.Reachable);
        await AsyncWait.Until(() => server.State.FindSession(a.Welcome.SessionId)!.Voice.TcpVoice && server.State.FindSession(b.Welcome.SessionId)!.Voice.TcpVoice);

        // Package 141 (review): a lost switch (dropped, or overtaken by an older UDP ping) is repeated with the next Hello
        var sessionB = server.State.FindSession(b.Welcome.SessionId)!;
        sessionB.Voice.TcpVoice = false;
        Advance(time, TimeSpan.FromSeconds(2));
        await AsyncWait.Until(() => sessionB.Voice.TcpVoice);

        va.SendVoice([1, 2, 3], VoiceHeader.TargetChannel);
        vb.SendVoice([4, 5, 6], VoiceHeader.TargetChannel);
        var gotB = await atB.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var gotA = await atA.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(a.Welcome.SessionId, gotB.Item1);
        Assert.Equal([1, 2, 3], gotB.Item3);
        Assert.Equal(b.Welcome.SessionId, gotA.Item1);
        Assert.Equal([4, 5, 6], gotA.Item3);
        Assert.Equal(0, relayA.Count(PacketType.Voice)); // nothing went into the blocked UDP path
        Assert.Equal(default, va.UplinkLoss); // a reliable path loses nothing
    }

    /// <summary>
    /// Package 141 (AC5): pings unanswered for 15 s move voice to TCP at once; when UDP answers again (tried every 2 s, well
    /// within 30 s) voice comes back to UDP, the next ping moves the server's side back, and no frame arrives twice.
    /// </summary>
    [Fact]
    public async Task UdpBack_SwitchesBack()
    {
        await using var server = await TestServer.StartAsync();
        await using var a = await Connect(server, "a");
        await using var b = await Connect(server, "b");
        using var relay = new LossyUdpRelay(server.VoiceEndPoint);
        var time = new ManualTimeProvider();
        using var va = TcpCapable(a, relay.EndPoint, time);
        using var vb = Voice(b, server.VoiceEndPoint);
        int atA = 0, atB = 0;
        va.VoiceReceived += (_, _, _, _, _) => Interlocked.Increment(ref atA);
        vb.VoiceReceived += (_, _, _, _, _) => Interlocked.Increment(ref atB);
        va.Start();
        vb.Start();
        await AsyncWait.Until(() => va.Reachable && vb.Reachable);
        var session = server.State.FindSession(a.Welcome.SessionId)!;

        relay.DropAll = true;
        Advance(time, TimeSpan.FromSeconds(14.5));
        Assert.False(va.OverTcp);
        Advance(time, TimeSpan.FromSeconds(0.5));
        Assert.True(va.OverTcp);
        await AsyncWait.Until(() => session.Voice.TcpVoice);
        va.SendVoice([1, 2, 3], VoiceHeader.TargetChannel);
        await AsyncWait.Until(() => Volatile.Read(ref atB) == 1);
        vb.SendVoice([4, 5, 6], VoiceHeader.TargetChannel);
        await AsyncWait.Until(() => Volatile.Read(ref atA) == 1);

        relay.DropAll = false;
        var waited = TimeSpan.Zero;
        while (!va.Reachable && waited < TimeSpan.FromSeconds(30))
        {
            Advance(time, TimeSpan.FromSeconds(0.5));
            waited += TimeSpan.FromSeconds(0.5);
            await Task.Delay(20);
        }
        Assert.True(va.Reachable);
        Assert.False(va.OverTcp);
        Advance(time, TimeSpan.FromSeconds(2)); // the next ping tells the server
        await AsyncWait.Until(() => !session.Voice.TcpVoice);

        vb.SendVoice([7, 8, 9], VoiceHeader.TargetChannel);
        await AsyncWait.Until(() => relay.PassedDownVoice == 1);
        await AsyncWait.Until(() => Volatile.Read(ref atA) == 2);
        va.SendVoice([1, 2, 3], VoiceHeader.TargetChannel);
        await AsyncWait.Until(() => relay.Count(PacketType.Voice) == 1 && Volatile.Read(ref atB) == 2);
        await Task.Delay(300);
        Assert.Equal(2, Volatile.Read(ref atA)); // once, on UDP only
    }
}
