using System.Net;
using System.Net.Sockets;
using OVS.Server.State;
using OVS.Shared.Net;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages.VoiceRelay;
using OVS.Shared.Voice;

namespace OVS.Server.Voice;

/// <summary>
/// Receives encrypted voice over UDP and re-seals it for every recipient. Invalid packets are dropped silently. Package 141
/// (A150): voice over the TLS connection takes the same way in and out, each recipient on its own path.
/// </summary>
public sealed class UdpVoiceServer : IDisposable
{
    readonly ServerState state;
    readonly TimeProvider time;
    readonly Socket socket;
    readonly CancellationTokenSource stopping = new();
    // Package 141: the sessions' voice state is touched by the UDP loop and by VoiceUp from the control connections, one at a time
    readonly SemaphoreSlim gate = new(1, 1);
    Task? loop;

    /// <param name="time">The clock the state runs on: it counts the seconds of the bad packets' budget.</param>
    public UdpVoiceServer(ServerState state, IPEndPoint endpoint, TimeProvider time)
    {
        this.state = state;
        this.time = time;
        socket = new Socket(endpoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            socket.Bind(endpoint);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public IPEndPoint LocalEndPoint => (IPEndPoint)socket.LocalEndPoint!;

    long foreignSourceDrops, preFilterDrops;
    // Package 88: bad packets per source group in the current second; only the receive loop touches these
    readonly Dictionary<IPAddress, int> badPackets = [];
    long badSecond;

    /// <summary>Package 88: packets for a session from an address other than its control connection's.</summary>
    public long ForeignSourceDrops => Interlocked.Read(ref foreignSourceDrops);

    /// <summary>Package 88: packets dropped undecrypted because their source sent too many bad ones this second.</summary>
    public long PreFilterDrops => Interlocked.Read(ref preFilterDrops);

    public void Start()
    {
        state.VoiceOverTcp = VoiceUpAsync; // Package 141
        loop = Task.Run(ReceiveLoopAsync);
    }

    async Task ReceiveLoopAsync()
    {
        var buffer = new byte[2048];
        EndPoint any = new IPEndPoint(IPAddress.Any, 0);
        while (!stopping.IsCancellationRequested)
        {
            SocketReceiveFromResult result;
            try
            {
                result = await socket.ReceiveFromAsync(buffer, SocketFlags.None, any, stopping.Token);
            }
            catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                continue; // e.g. ICMP port unreachable reported on Windows
            }

            await gate.WaitAsync();
            try
            {
                await HandleAsync(buffer.AsMemory(0, result.ReceivedBytes), (IPEndPoint)result.RemoteEndPoint);
            }
            catch (Exception exception) when (exception is SocketException or OverflowException or ObjectDisposedException)
            {
            }
            finally
            {
                gate.Release();
            }
        }
    }

    async Task HandleAsync(ReadOnlyMemory<byte> packet, IPEndPoint from)
    {
        var source = IpAddresses.ToAddressGroup(from.Address);
        if (!VoiceHeader.TryRead(packet.Span, out var header))
        {
            CountBad(source);
            return;
        }
        var sender = state.FindSession(header.SessionId);
        // Package 88: a source over its budget of bad packets costs no decryption for the rest of the second;
        // a client's own bound endpoint is exempt, so a flood from behind the same NAT does not cut it off
        if (!from.Equals(sender?.Voice.UdpEndpoint) && OverBudget(source))
        {
            Interlocked.Increment(ref preFilterDrops);
            return;
        }
        if (sender is null)
        {
            CountBad(source);
            return;
        }
        // Package 88: voice goes only to the address of the own control connection (no reflection to a spoofed victim)
        if (!source.Equals(IpAddresses.ToAddressGroup(sender.Ip)))
        {
            Interlocked.Increment(ref foreignSourceDrops);
            CountBad(source);
            return;
        }
        if (!sender.Voice.Crypto.TryOpen(PacketDirection.ClientToServer, packet.Span, out header, out var plain))
        {
            CountBad(source);
            return;
        }
        if (!sender.Voice.Replay.Accept(header.Seq) || !sender.Voice.Limiter.TryTake()) return;

        if (header.Type == PacketType.Hello || sender.Voice.UdpEndpoint is not null) sender.Voice.UdpEndpoint = from;
        // Package 141: a ping or voice means the client uses UDP again; a Hello is only its probe, so voice stays on TCP
        // until then and no frame goes both ways
        if (header.Type != PacketType.Hello) sender.Voice.TcpVoice = false;

        switch (header.Type)
        {
            case PacketType.Hello or PacketType.Ping:
                // Package 133 (A142): the client's time comes back with the loss of its frames; its own figure stays on the session
                byte[] answer = [];
                if (PingPayload.TryParse(plain, out var ping))
                {
                    sender.Voice.DownlinkLoss = ping.Loss;
                    answer = new PingPayload(ping.TimeMs, sender.Voice.Uplink.Report()).Build();
                }
                await SendAsync(sender.Voice, new VoiceHeader(PacketType.Ping, sender.Id, sender.Voice.OutSeq.Next(), 0), answer);
                break;
            // Voice plaintext has the same format in both directions (RelayPayload), so it is relayed as is where it fits.
            case PacketType.Voice when RelayPayload.TryReadRanges(plain, out var frame, out var current, out var copy):
                if (frame > sender.Voice.LastUdpFrame) sender.Voice.LastUdpFrame = frame; // Package 141: VoiceUp older than this is stale
                await VoiceAsync(sender, header.Target, plain, frame, current, copy);
                break;
        }
    }

    /// <summary>
    /// Package 141 (A150): voice from the control connection. Checked like a UDP payload (target, at most MaxOpusSize bytes,
    /// the base64 length before decoding), then the sender's voice budget (ServerTuning.VoicePerSecond, shared with its UDP
    /// packets, never the request budget) and the same relay as UDP voice. Over the budget it is dropped quietly like UDP
    /// (TCP delivers in bursts after a stall, so an honest talker does that). An invalid one is dropped as well, but invalid
    /// ones without a pause end the session like a request flood (Package 86); unlike UDP the sender cannot be spoofed here. A frame not newer than the last one on UDP is stale (sent before the client switched
    /// back) and ignored, so it cannot move the session to TCP again; the empty switch carries the next frame number.
    /// </summary>
    async Task VoiceUpAsync(Session sender, VoiceUp up)
    {
        var opus = new byte[VoiceHeader.MaxOpusSize];
        int length = 0;
        bool valid = up.Target <= VoiceHeader.TargetLinked && up.Opus is { } text && text.Length <= MaxOpusBase64
            && Convert.TryFromBase64String(text, opus, out length);
        if (stopping.IsCancellationRequested) return;
        if (valid && length == 0 && sender.Voice.TcpVoice) return; // a repeated switch changes nothing and need not wait for the gate
        await gate.WaitAsync();
        try
        {
            if (!valid)
            {
                if (sender.Throttle.RefuseVoice()) state.DisconnectFlood(sender);
                return;
            }
            // the empty switch relays nothing and costs no more than a Ping, so it takes no token: repeated with every
            // Hello (review), it must not eat the budget the client's UDP Hellos and voice need
            if (length > 0 && !sender.Voice.Limiter.TryTake()) return;
            if (up.Seq <= sender.Voice.LastUdpFrame) return;
            sender.Voice.TcpVoice = true;
            if (length == 0) return; // only the switch: a listener on TCP
            var plain = RelayPayload.Build(up.Seq, opus.AsSpan(0, length));
            await VoiceAsync(sender, up.Target, plain, up.Seq, RelayPayload.HeaderSize..plain.Length, plain.Length..);
        }
        catch (Exception exception) when (exception is SocketException or OverflowException or ObjectDisposedException)
        {
        }
        finally
        {
            gate.Release();
        }
    }

    static readonly int MaxOpusBase64 = Base64Text.Encoded(VoiceHeader.MaxOpusSize);

    /// <summary>Package 141: one voice frame of a UDP or a TCP sender, under the gate; everything after the way in is shared.</summary>
    async Task VoiceAsync(Session sender, byte requestedTarget, byte[] plain, uint frame, Range current, Range copy)
    {
        sender.Voice.Uplink.Count(0, frame); // Package 133: one stream per sender; a copy is no frame received
        var (recipients, target, channel) = state.VoiceRecipients(sender, requestedTarget);
        if (recipients.Count > 0) sender.CountVoiceFrame(); // Package 70: only relayed speech counts
        await RelayAsync(sender, recipients, target, channel, plain, frame, current, copy);
    }

    /// <summary>
    /// Package 136 (A145): per recipient, the frame alone or with the one before it (the sender's copy, else the frame
    /// kept from last time) for those reporting a lossy downlink. At most three payloads are built per packet and each is
    /// sealed once per recipient as before, so a copy costs no extra crypto. A frame lost on the way in that arrives as the
    /// sender's copy goes on its own to those without copies, before the frame; those with copies get it inside it. The
    /// frame before only goes along when <see cref="SessionVoice.TrackFrame"/> allows it (same channel and target, right before, relayed or
    /// never seen), and the extra packet costs the sender a token of its rate limit like any packet. Package 141: a listener on
    /// TCP gets VoiceDown, encoded once per packet, never with a copy (a reliable path loses nothing), but like those without
    /// copies the recovered frame on its own first.
    /// </summary>
    async Task RelayAsync(Session sender, List<Session> recipients, byte target, Guid channel, byte[] plain, uint frame, Range current, Range copy)
    {
        if (sender.Voice.RecoveredFrame == frame) return; // Package 136 (review): sent already, from the copy in the packet after it
        var (kept, unseen) = sender.Voice.TrackFrame(frame, channel, target, plain.AsMemory(current), recipients.Count > 0);
        bool sentCopy = plain.Length > copy.Start.Value;
        bool usable = !kept.IsEmpty || unseen; // otherwise the sender's copy is stripped
        ReadOnlyMemory<byte> previous = !usable ? default : sentCopy ? plain.AsMemory(copy) : kept;
        bool recovered = sentCopy && unseen && recipients.Count > 0 && sender.Voice.Limiter.TryTake();
        if (recovered) sender.Voice.RecoveredFrame = frame - 1;
        byte[]? single = sentCopy ? null : plain, withCopy = sentCopy && usable ? plain : null, alone = null, tcpAlone = null, tcpSingle = null;
        foreach (var session in recipients)
        {
            if (session.Voice.TcpVoice)
            {
                if (recovered) session.Outbox.SendVoice(tcpAlone ??= SessionOutbox.Encode(new VoiceDown(sender.Id, frame - 1, target, Convert.ToBase64String(previous.Span))));
                session.Outbox.SendVoice(tcpSingle ??= SessionOutbox.Encode(new VoiceDown(sender.Id, frame, target, Convert.ToBase64String(plain.AsSpan(current)))));
                continue;
            }
            byte[] payload;
            if (session.Voice.WantsCopies && !previous.IsEmpty) payload = withCopy ??= RelayPayload.Build(frame, plain.AsSpan(current), previous.Span);
            else
            {
                if (recovered)
                    await SendAsync(session.Voice, new VoiceHeader(PacketType.Voice, sender.Id, session.Voice.OutSeq.Next(), target), alone ??= RelayPayload.Build(frame - 1, previous.Span));
                payload = single ??= RelayPayload.Build(frame, plain.AsSpan(current));
            }
            await SendAsync(session.Voice, new VoiceHeader(PacketType.Voice, sender.Id, session.Voice.OutSeq.Next(), target), payload);
        }
    }

    bool OverBudget(IPAddress source)
    {
        long second = time.GetTimestamp() / time.TimestampFrequency;
        if (second != badSecond)
        {
            badPackets.Clear(); // also bounds the table to the sources of one second
            badSecond = second;
        }
        return badPackets.GetValueOrDefault(source) >= ServerTuning.BadVoicePacketsPerSecond;
    }

    void CountBad(IPAddress source)
    {
        OverBudget(source); // starts a new second if one began
        badPackets[source] = badPackets.GetValueOrDefault(source) + 1;
    }

    async Task SendAsync(SessionVoice to, VoiceHeader header, byte[] payload)
    {
        if (to.UdpEndpoint is not { } endpoint) return;
        await socket.SendToAsync(to.Crypto.Seal(PacketDirection.ServerToClient, header, payload), SocketFlags.None, endpoint);
    }

    public void Dispose()
    {
        if (stopping.IsCancellationRequested) return;
        stopping.Cancel();
        socket.Dispose();
        try
        {
            loop?.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
        }
        stopping.Dispose();
    }
}
