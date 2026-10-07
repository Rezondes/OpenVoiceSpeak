using System.Net;
using System.Net.Sockets;
using OVS.Shared.Protocol.Messages.VoiceRelay;
using OVS.Shared.Voice;

namespace OVS.Client.Net;

/// <summary>
/// Encrypted UDP voice. Hello binds our endpoint on the server, pings keep NAT mappings open. Package 141 (A150): with a
/// control connection given, voice goes over it while UDP does not answer (5 s after the start, or once lost), and back
/// as soon as a Hello is answered again; TLS already encrypts it there.
/// </summary>
public sealed class VoiceClient : IDisposable
{
    static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(2); // Package 133 (A142): 5 s before, now as often as the stats refresh
    // Package 126: Hello is retried until the server answers, fast at first; silence this long counts as lost
    static readonly TimeSpan HelloRetry = TimeSpan.FromMilliseconds(500);
    static readonly TimeSpan FastHellos = TimeSpan.FromSeconds(5);
    static readonly TimeSpan LostAfter = TimeSpan.FromSeconds(15);

    readonly UdpClient udp;
    readonly TimeProvider time;
    readonly ClientConnection? control; // Package 141: the TCP path, null when voice stays on UDP
    readonly object stateGate = new();
    DateTimeOffset lastAnswer, lastSent, helloSince, startedAt;
    bool everReachable;
    volatile bool overTcp;
    readonly long origin; // Package 133: the ping's clock counts from here
    readonly LossWindow downlink;
    TimeSpan? roundTrip;
    int roundTrips;
    LossReport uplink;
    readonly uint sessionId;
    readonly VoiceCrypto sendCrypto, receiveCrypto; // AesGcm per thread
    readonly SeqCounter seq = new();
    readonly ReplayWindow replay = new();
    readonly object sendGate = new();
    uint nextFrame;
    byte[]? lastFrame; // Package 136: the copy for the next frame, with its target and send time; under sendGate
    byte lastTarget;
    long lastSentAt;
    volatile bool sendCopies; // Package 136 (A145): the server reported at least 2 % of my frames missing
    readonly CancellationTokenSource cts = new();
    Task? receiveLoop;
    ITimer? pingTimer;

    /// <param name="time">Package 126: drives the Hello retries and the loss detection (tests pass a manual clock).</param>
    /// <param name="control">Package 141: the connection voice falls back to while UDP does not work.</param>
    public VoiceClient(IPEndPoint server, uint sessionId, byte[] key, TimeProvider? time = null, ClientConnection? control = null)
    {
        this.time = time ?? TimeProvider.System;
        this.control = control;
        if (control is not null) control.VoiceDownReceived += OnVoiceDown;
        origin = this.time.GetTimestamp();
        downlink = new LossWindow(this.time);
        udp = new UdpClient(server.AddressFamily);
        udp.Connect(server);
        this.sessionId = sessionId;
        sendCrypto = new VoiceCrypto(key);
        receiveCrypto = new VoiceCrypto(key);
    }

    /// <summary>speakerId, speakerSeq, target, opus, the copy of the frame before it or null (Package 136). Raised on the receive thread.</summary>
    public event Action<uint, uint, byte, byte[], byte[]?>? VoiceReceived;

    /// <summary>True while the server answers on UDP; false again after 15 s without any answer (Package 126).</summary>
    public bool Reachable
    {
        get
        {
            lock (stateGate) return reachable;
        }
    }

    bool reachable;

    /// <summary>Package 141 (A150): voice goes over the control connection right now.</summary>
    public bool OverTcp => overTcp;

    /// <summary>Package 133 (A142): the voice round trip of the last answered Hello or ping; null before the first.</summary>
    public TimeSpan? RoundTrip
    {
        get
        {
            lock (stateGate) return roundTrip;
        }
    }

    /// <summary>Package 133: the last round trip with the number of answers so far, so each sample is taken once.</summary>
    public (TimeSpan? Value, int Count) RoundTripSample
    {
        get
        {
            lock (stateGate) return (roundTrip, roundTrips);
        }
    }

    /// <summary>
    /// Package 133: the share of my frames the server missed in its last 5 s window, from its last ping answer. Package 141:
    /// nothing over TCP, which loses no frame (and takes no copies).
    /// </summary>
    public LossReport UplinkLoss
    {
        get
        {
            lock (stateGate) return overTcp ? default : uplink;
        }
    }

    /// <summary>Package 133: the gaps in the frame numbers of everyone I hear, last 5 s window; the server gets it in every ping.</summary>
    public LossReport DownlinkLoss
    {
        get
        {
            lock (stateGate) return downlink.Report();
        }
    }

    long NowMs() => (long)time.GetElapsedTime(origin).TotalMilliseconds;

    /// <summary>Package 133: Hello and ping carry the time and the downlink loss. Under stateGate.</summary>
    byte[] PingBody() => new PingPayload(NowMs(), downlink.Report()).Build();

    public void Start()
    {
        byte[] body;
        lock (stateGate)
        {
            helloSince = lastSent = startedAt = time.GetUtcNow();
            body = PingBody();
        }
        Send(PacketType.Hello, body, 0);
        receiveLoop = Task.Run(ReceiveLoopAsync);
        pingTimer = time.CreateTimer(_ => Tick(), null, HelloRetry, HelloRetry);
    }

    /// <summary>
    /// Package 126: while unreachable a Hello every 500 ms for 5 s, then one per ping interval, as the server binds our
    /// endpoint only on a Hello; while reachable a ping per interval, and every 500 ms while an answer is overdue.
    /// Ticks every 500 ms in both states. Package 141: voice moves to TCP 5 s after the start without an answer, or at
    /// once when lost; the Hellos go on as before (every 2 s, so UDP is tried again well within A150's 30 s).
    /// </summary>
    void Tick()
    {
        PacketType type;
        byte[] body;
        bool toTcp = false;
        lock (stateGate)
        {
            var now = time.GetUtcNow();
            if (reachable && now - lastAnswer >= LostAfter)
            {
                reachable = false;
                helloSince = now;
            }
            if (control is not null && !reachable && !overTcp && (everReachable || now - startedAt >= FastHellos)) overTcp = toTcp = true;
        }
        if (toTcp) AnnounceTcp();
        lock (stateGate)
        {
            var now = time.GetUtcNow();
            // an answer overdue by more than a ping interval is asked for every tick, so two lost pings are not a loss
            bool fast = reachable ? now - lastAnswer > PingInterval : now - helloSince < FastHellos;
            if (!fast && now - lastSent < PingInterval) return;
            lastSent = now;
            type = reachable ? PacketType.Ping : PacketType.Hello;
            body = PingBody();
        }
        Send(type, body, 0);
        // Package 141 (review): repeated with every Hello while on TCP, as one switch can be dropped (budget, full queue)
        // or overtaken by an older UDP ping that moved the server's side back
        if (overTcp && !toTcp) AnnounceTcp();
    }

    /// <summary>
    /// Package 141: an empty VoiceUp moves the server's side to TCP, also for a client that only listens. It carries the next
    /// frame number, as the server ignores anything not newer than the last frame it got over UDP.
    /// </summary>
    void AnnounceTcp()
    {
        lock (sendGate) control!.SendVoice(new VoiceUp(nextFrame, VoiceHeader.TargetChannel, ""));
    }

    /// <summary>
    /// The frame number travels inside the payload: the packet sequence is the GCM nonce and is also
    /// used by pings, so it has gaps the receiver's jitter buffer must not see. Package 136 (A145): while the uplink
    /// loses frames, the previous frame rides along (the encoder's own array, kept, so nothing extra is allocated), but
    /// only to the same target (it travels under this frame's routing) and only right after it (not the end of the last
    /// talk spurt). Package 141: over TCP the frame alone, with the same frame numbers.
    /// </summary>
    public void SendVoice(byte[] opus, byte target)
    {
        lock (sendGate)
        {
            long now = time.GetTimestamp();
            if (overTcp && control is not null)
            {
                control.SendVoice(new VoiceUp(nextFrame++, target, Convert.ToBase64String(opus)));
                (lastFrame, lastTarget, lastSentAt) = (opus, target, now);
                return;
            }
            bool follows = sendCopies && target == lastTarget && time.GetElapsedTime(lastSentAt, now) <= RelayPayload.CopyMaxAge;
            Send(PacketType.Voice, RelayPayload.Build(nextFrame++, opus, follows ? lastFrame : default), target);
            (lastFrame, lastTarget, lastSentAt) = (opus, target, now);
        }
    }

    public void SendPing()
    {
        byte[] body;
        lock (stateGate) body = PingBody();
        Send(PacketType.Ping, body, 0);
    }

    void Send(PacketType type, byte[] payload, byte target)
    {
        try
        {
            lock (sendGate)
            {
                var packet = sendCrypto.Seal(PacketDirection.ClientToServer, new VoiceHeader(type, sessionId, seq.Next(), target), payload);
                udp.Send(packet, packet.Length);
            }
        }
        catch (Exception e) when (e is SocketException or ObjectDisposedException)
        {
        }
    }

    async Task ReceiveLoopAsync()
    {
        while (!cts.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await udp.ReceiveAsync(cts.Token);
            }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                continue;
            }

            if (!receiveCrypto.TryOpen(PacketDirection.ServerToClient, result.Buffer, out var header, out var plain)) continue;
            if (!replay.Accept(header.Seq)) continue;
            uint speakerSeq = 0;
            byte[] opus = [];
            byte[]? copy = null;
            bool voice = header.Type == PacketType.Voice && RelayPayload.TryReadArrays(plain, out speakerSeq, out opus, out copy);
            bool back;
            lock (stateGate)
            {
                back = overTcp;
                reachable = everReachable = true;
                overTcp = false; // Package 141: back to UDP; the next ping moves the server's side back
                lastAnswer = time.GetUtcNow();
                // Package 133 (A142): the echoed time gives the round trip, the server adds how many of my frames it missed
                if (header.Type == PacketType.Ping && PingPayload.TryParse(plain, out var ping))
                {
                    roundTrip = TimeSpan.FromMilliseconds(NowMs() - ping.TimeMs);
                    roundTrips++;
                    uplink = ping.Loss;
                    // Package 136: a figure with nothing expected (a talk pause) keeps the last decision
                    if (ping.Loss.Expected > 0) sendCopies = ping.Loss.Percent >= RelayPayload.CopyFromLossPercent;
                }
                if (voice) downlink.Count(header.SessionId, speakerSeq);
            }
            if (back) control!.DropPendingVoice(); // Package 141: no frame queued for TCP goes after the switch
            if (voice) VoiceReceived?.Invoke(header.SessionId, speakerSeq, header.Target, opus, copy);
        }
    }

    /// <summary>
    /// Package 141: a frame over TCP, checked like a UDP payload (target, 1 to MaxOpusSize bytes), counted in the downlink
    /// window and handed on like one from UDP. Raised on the connection's receive thread.
    /// </summary>
    void OnVoiceDown(VoiceDown down)
    {
        var opus = new byte[VoiceHeader.MaxOpusSize];
        if (down.Target > VoiceHeader.TargetLinked || down.Opus is null
            || !Convert.TryFromBase64String(down.Opus, opus, out int length) || length == 0) return;
        lock (stateGate) downlink.Count(down.SessionId, down.Seq);
        VoiceReceived?.Invoke(down.SessionId, down.Seq, down.Target, opus[..length], null);
    }

    public void Dispose()
    {
        if (control is not null) control.VoiceDownReceived -= OnVoiceDown;
        cts.Cancel();
        pingTimer?.Dispose();
        udp.Dispose();
        try
        {
            receiveLoop?.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
        }
        sendCrypto.Dispose();
        receiveCrypto.Dispose();
        cts.Dispose();
    }
}
