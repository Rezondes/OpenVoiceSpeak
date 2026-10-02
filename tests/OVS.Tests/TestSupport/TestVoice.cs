using System.Net;
using System.Net.Sockets;
using OVS.Shared.Voice;

namespace OVS.Tests.TestSupport;

/// <summary>UDP side of a TestClient.</summary>
public sealed class TestVoice : IDisposable
{
    readonly UdpClient udp;
    readonly VoiceCrypto crypto;
    readonly uint sessionId;
    readonly IPEndPoint server;

    /// <param name="bind">Package 88: another loopback address (e.g. 127.0.0.2) stands in for a foreign source.</param>
    public TestVoice(TestClient client, IPEndPoint server, SeqCounter? seq = null, IPAddress? bind = null)
    {
        udp = new UdpClient(new IPEndPoint(bind ?? IPAddress.Loopback, 0));
        crypto = new VoiceCrypto(Convert.FromBase64String(client.Welcome.VoiceKey));
        sessionId = client.Id;
        this.server = server;
        Seq = seq ?? new SeqCounter();
    }

    public SeqCounter Seq { get; }

    /// <summary>Voice payloads get the frame number prefix like the real client ([frameSeq][opus]).</summary>
    public byte[] Seal(PacketType type, byte[] payload, byte target = 0)
    {
        if (type == PacketType.Voice) payload = RelayPayload.Build(nextFrame++, payload);
        return SealRaw(type, payload, target);
    }

    public byte[] SealRaw(PacketType type, byte[] plaintext, byte target = 0) =>
        crypto.Seal(Direction.ClientToServer, new VoiceHeader(type, sessionId, Seq.Next(), target), plaintext);

    uint nextFrame;

    public Task SendRawAsync(byte[] packet) => udp.SendAsync(packet, server).AsTask();

    public Task SendAsync(PacketType type, byte[] payload, byte target = 0) => SendRawAsync(Seal(type, payload, target));

    /// <summary>Hello, then wait for the server's Ping reply so the endpoint is surely bound.</summary>
    public async Task HelloAsync()
    {
        await SendAsync(PacketType.Hello, []);
        var reply = await ReceiveAsync();
        Assert.NotNull(reply);
        Assert.Equal(PacketType.Ping, reply.Value.Header.Type);
    }

    /// <summary>The default wait is generous for a slow CI runner (it returns as soon as a packet arrives); silence checks pass a short one.</summary>
    public async Task<(VoiceHeader Header, byte[] Plain)?> ReceiveAsync(int timeoutMs = 10000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        while (true)
        {
            UdpReceiveResult result;
            try
            {
                result = await udp.ReceiveAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (SocketException)
            {
                continue;
            }
            if (crypto.TryOpen(Direction.ServerToClient, result.Buffer, out var header, out var plain)) return (header, plain);
        }
    }

    public async Task<(VoiceHeader Header, byte[] Plain)?> ReceiveVoiceAsync(int timeoutMs = 10000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (true)
        {
            int left = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
            if (left <= 0) return null;
            var r = await ReceiveAsync(left);
            if (r is null) return null;
            if (r.Value.Header.Type == PacketType.Voice) return r;
        }
    }

    public void Dispose()
    {
        udp.Dispose();
        crypto.Dispose();
    }
}
