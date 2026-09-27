using System.Net;
using System.Net.Sockets;
using OVS.Shared.Voice;

namespace OVS.Client.Net;

/// <summary>Encrypted UDP voice. Hello binds our endpoint on the server, pings keep NAT mappings open.</summary>
public sealed class VoiceClient : IDisposable
{
    static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(5);

    readonly UdpClient udp;
    readonly uint sessionId;
    readonly VoiceCrypto sendCrypto, receiveCrypto; // AesGcm per thread
    readonly SeqCounter seq = new();
    readonly ReplayWindow replay = new();
    readonly object sendGate = new();
    uint nextFrame;
    readonly CancellationTokenSource cts = new();
    Task? receiveLoop;
    Timer? pingTimer;

    public VoiceClient(IPEndPoint server, uint sessionId, byte[] key)
    {
        udp = new UdpClient(server.AddressFamily);
        udp.Connect(server);
        this.sessionId = sessionId;
        sendCrypto = new VoiceCrypto(key);
        receiveCrypto = new VoiceCrypto(key);
    }

    /// <summary>speakerId, speakerSeq, target, opus. Raised on the receive thread.</summary>
    public event Action<uint, uint, byte, byte[]>? VoiceReceived;

    /// <summary>True once the server answered on UDP.</summary>
    public bool Reachable { get; private set; }

    public void Start()
    {
        Send(PacketType.Hello, [], 0);
        receiveLoop = Task.Run(ReceiveLoopAsync);
        pingTimer = new Timer(_ => SendPing(), null, PingInterval, PingInterval);
    }

    /// <summary>
    /// The frame number travels inside the payload: the packet sequence is the GCM nonce and is also
    /// used by pings, so it has gaps the receiver's jitter buffer must not see.
    /// </summary>
    public void SendVoice(byte[] opus, byte target)
    {
        lock (sendGate) Send(PacketType.Voice, RelayPayload.Build(nextFrame++, opus), target);
    }

    public void SendPing() => Send(PacketType.Ping, [], 0);

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
            Reachable = true;
            if (header.Type == PacketType.Voice && RelayPayload.TryParse(plain, out var speakerSeq, out var opus))
                VoiceReceived?.Invoke(header.SessionId, speakerSeq, header.Target, opus);
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
