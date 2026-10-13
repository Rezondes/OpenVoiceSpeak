using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using OVS.Shared.Voice;

namespace OVS.Tests.TestSupport;

/// <summary>
/// The server end of a voice client on loopback UDP, without a TLS server: it records the header of every sealed packet it
/// gets, answers the client's Hello with a ping that echoes the client's time, sends voice to the client and drains the
/// packets already on their way (which relies on the order in which loopback datagrams reach a socket).
/// </summary>
sealed class FakeVoiceServer : IDisposable
{
    public const uint SessionId = 7;

    /// <summary>The share of the client's frames this server says it missed.</summary>
    public static readonly LossReport UplinkLoss = new(Expected: 100, Missed: 10);

    readonly UdpClient udp = new(new IPEndPoint(IPAddress.Loopback, 0));
    readonly UdpClient fence = new(new IPEndPoint(IPAddress.Loopback, 0));
    readonly VoiceCrypto crypto;
    readonly SeqCounter sequence = new();
    readonly List<VoiceHeader> received = [];
    readonly Task receiveLoop;
    IPEndPoint? clientEndPoint;
    long lastTimeMs;
    int fencesSeen;

    public FakeVoiceServer()
    {
        Key = RandomNumberGenerator.GetBytes(32);
        crypto = new VoiceCrypto(Key);
        receiveLoop = Task.Run(ReceiveLoopAsync);
    }

    /// <summary>The voice key the client under test is built with.</summary>
    public byte[] Key { get; }

    public IPEndPoint EndPoint => (IPEndPoint)udp.Client.LocalEndPoint!;

    /// <summary>The headers of the sealed packets that came in, in arrival order.</summary>
    public List<VoiceHeader> Received
    {
        get
        {
            lock (received) return [.. received];
        }
    }

    /// <summary>Waits until <paramref name="count"/> packets of <paramref name="type"/> came in.</summary>
    public Task WaitForAsync(PacketType type, int count = 1) => AsyncWait.UntilAsync(() => Received.Count(header => header.Type == type) >= count);

    /// <summary>
    /// Answers the client's last packet with a ping that echoes the time the client sent and reports <see cref="UplinkLoss"/>,
    /// which makes the client count UDP as reachable. <paramref name="uplink"/> replaces <see cref="UplinkLoss"/> when given.
    /// </summary>
    public Task AnswerAsync(LossReport? uplink = null) => SendAsync(PacketType.Ping, new PingPayload(Volatile.Read(ref lastTimeMs), uplink ?? UplinkLoss).Build());

    /// <summary>Sends frame number <paramref name="frameNumber"/> of voice from <paramref name="speaker"/> to the client.</summary>
    public Task SendVoiceAsync(uint speaker, uint frameNumber, byte[] opusFrame) =>
        SendAsync(PacketType.Voice, RelayPayload.Build(frameNumber, opusFrame), sessionId: speaker);

    /// <summary>
    /// Returns once every packet the clients sent before the call has been recorded: a datagram from another socket queues
    /// behind them on the loopback socket, so seeing it means the earlier ones were taken.
    /// </summary>
    public async Task DrainAsync()
    {
        int before = Volatile.Read(ref fencesSeen);
        await fence.SendAsync(new byte[1], EndPoint);
        await AsyncWait.UntilAsync(() => Volatile.Read(ref fencesSeen) > before);
    }

    Task<int> SendAsync(PacketType type, byte[] payload, uint sessionId = SessionId)
    {
        var target = Volatile.Read(ref clientEndPoint) ?? throw new InvalidOperationException("no client packet yet");
        var packet = crypto.Seal(PacketDirection.ServerToClient, new VoiceHeader(type, sessionId, sequence.Next(), VoiceHeader.TargetChannel), payload);
        return udp.SendAsync(packet, target).AsTask();
    }

    async Task ReceiveLoopAsync()
    {
        while (true)
        {
            UdpReceiveResult result;
            try
            {
                result = await udp.ReceiveAsync();
            }
            catch (SocketException exception) when (exception.SocketErrorCode == SocketError.ConnectionReset)
            {
                continue;
            }
            catch (Exception exception) when (exception is ObjectDisposedException or SocketException)
            {
                return;
            }
            if (!crypto.TryOpen(PacketDirection.ClientToServer, result.Buffer, out var header, out var plain))
            {
                Interlocked.Increment(ref fencesSeen);
                continue;
            }
            if (PingPayload.TryParse(plain, out var ping)) Volatile.Write(ref lastTimeMs, ping.TimeMs);
            Volatile.Write(ref clientEndPoint, result.RemoteEndPoint);
            lock (received) received.Add(header);
        }
    }

    public void Dispose()
    {
        udp.Dispose();
        fence.Dispose();
        receiveLoop.Wait(HangGuard.Limit);
        crypto.Dispose();
    }
}
