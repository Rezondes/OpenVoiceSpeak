using System.Net;
using System.Net.Sockets;
using OVS.Shared.Voice;

namespace OVS.Client.Net;

/// <summary>Encrypted UDP voice. Hello binds our endpoint on the server, pings keep NAT mappings open.</summary>
public sealed class VoiceClient : IDisposable
{
    static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(2); // Package 133 (A142): 5 s before, now as often as the stats refresh
    // Package 126: Hello is retried until the server answers, fast at first; silence this long counts as lost
    static readonly TimeSpan HelloRetry = TimeSpan.FromMilliseconds(500);
    static readonly TimeSpan FastHellos = TimeSpan.FromSeconds(5);
    static readonly TimeSpan LostAfter = TimeSpan.FromSeconds(15);

    readonly UdpClient udp;
    readonly TimeProvider time;
    readonly object stateGate = new();
    DateTimeOffset lastAnswer, lastSent, helloSince;
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
    readonly CancellationTokenSource cts = new();
    Task? receiveLoop;
    ITimer? pingTimer;

    /// <param name="time">Package 126: drives the Hello retries and the loss detection (tests pass a manual clock).</param>
    public VoiceClient(IPEndPoint server, uint sessionId, byte[] key, TimeProvider? time = null)
    {
        this.time = time ?? TimeProvider.System;
        origin = this.time.GetTimestamp();
        downlink = new LossWindow(this.time);
        udp = new UdpClient(server.AddressFamily);
        udp.Connect(server);
        this.sessionId = sessionId;
        sendCrypto = new VoiceCrypto(key);
        receiveCrypto = new VoiceCrypto(key);
    }

    /// <summary>speakerId, speakerSeq, target, opus. Raised on the receive thread.</summary>
    public event Action<uint, uint, byte, byte[]>? VoiceReceived;

    /// <summary>True while the server answers on UDP; false again after 15 s without any answer (Package 126).</summary>
    public bool Reachable
    {
        get
        {
            lock (stateGate) return reachable;
        }
    }

    bool reachable;

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

    /// <summary>Package 133: the share of my frames the server missed in its last 5 s window, from its last ping answer.</summary>
    public LossReport UplinkLoss
    {
        get
        {
            lock (stateGate) return uplink;
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
            helloSince = lastSent = time.GetUtcNow();
            body = PingBody();
        }
        Send(PacketType.Hello, body, 0);
        receiveLoop = Task.Run(ReceiveLoopAsync);
        pingTimer = time.CreateTimer(_ => Tick(), null, HelloRetry, HelloRetry);
    }

    /// <summary>
    /// Package 126: while unreachable a Hello every 500 ms for 5 s, then one per ping interval, as the server binds our
    /// endpoint only on a Hello; while reachable a ping per interval, and every 500 ms while an answer is overdue.
    /// Ticks every 500 ms in both states.
    /// </summary>
    void Tick()
    {
        PacketType type;
        byte[] body;
        lock (stateGate)
        {
            var now = time.GetUtcNow();
            if (reachable && now - lastAnswer >= LostAfter)
            {
                reachable = false;
                helloSince = now;
            }
            // an answer overdue by more than a ping interval is asked for every tick, so two lost pings are not a loss
            bool fast = reachable ? now - lastAnswer > PingInterval : now - helloSince < FastHellos;
            if (!fast && now - lastSent < PingInterval) return;
            lastSent = now;
            type = reachable ? PacketType.Ping : PacketType.Hello;
            body = PingBody();
        }
        Send(type, body, 0);
    }

    /// <summary>
    /// The frame number travels inside the payload: the packet sequence is the GCM nonce and is also
    /// used by pings, so it has gaps the receiver's jitter buffer must not see.
    /// </summary>
    public void SendVoice(byte[] opus, byte target)
    {
        lock (sendGate) Send(PacketType.Voice, RelayPayload.Build(nextFrame++, opus), target);
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
                var packet = sendCrypto.Seal(Direction.ClientToServer, new VoiceHeader(type, sessionId, seq.Next(), target), payload);
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

            if (!receiveCrypto.TryOpen(Direction.ServerToClient, result.Buffer, out var header, out var plain)) continue;
            if (!replay.Accept(header.Seq)) continue;
            uint speakerSeq = 0;
            byte[] opus = [];
            bool voice = header.Type == PacketType.Voice && RelayPayload.TryParse(plain, out speakerSeq, out opus);
            lock (stateGate)
            {
                reachable = true;
                lastAnswer = time.GetUtcNow();
                // Package 133 (A142): the echoed time gives the round trip, the server adds how many of my frames it missed
                if (header.Type == PacketType.Ping && PingPayload.TryParse(plain, out var ping))
                {
                    roundTrip = TimeSpan.FromMilliseconds(NowMs() - ping.TimeMs);
                    roundTrips++;
                    uplink = ping.Loss;
                }
                if (voice) downlink.Count(header.SessionId, speakerSeq);
            }
            if (voice) VoiceReceived?.Invoke(header.SessionId, speakerSeq, header.Target, opus);
        }
    }

    public void Dispose()
    {
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
