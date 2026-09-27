using System.Net;
using System.Net.Sockets;
using OVS.Shared.Voice;

namespace OVS.Server.Voice;

/// <summary>Receives encrypted voice over UDP and re-seals it for every recipient. Invalid packets are dropped silently.</summary>
public sealed class UdpVoiceServer : IDisposable
{
    readonly ServerState state;
    readonly Socket socket;
    readonly CancellationTokenSource cts = new();
    Task? loop;

    public UdpVoiceServer(ServerState state, IPEndPoint endpoint)
    {
        this.state = state;
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

    public void Start() => loop = Task.Run(ReceiveLoopAsync);

    async Task ReceiveLoopAsync()
    {
        var buffer = new byte[2048];
        EndPoint any = new IPEndPoint(IPAddress.Any, 0);
        while (!cts.IsCancellationRequested)
        {
            SocketReceiveFromResult result;
            try
            {
                result = await socket.ReceiveFromAsync(buffer, SocketFlags.None, any, cts.Token);
            }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                continue; // e.g. ICMP port unreachable reported on Windows
            }

            try
            {
                await HandleAsync(buffer.AsMemory(0, result.ReceivedBytes), (IPEndPoint)result.RemoteEndPoint);
            }
            catch (Exception e) when (e is SocketException or OverflowException or ObjectDisposedException)
            {
            }
        }
    }

    async Task HandleAsync(ReadOnlyMemory<byte> packet, IPEndPoint from)
    {
        if (!VoiceHeader.TryRead(packet.Span, out var header)) return;
        var sender = state.FindSession(header.SessionId);
        if (sender is null) return;
        if (!sender.Crypto.TryOpen(Direction.ClientToServer, packet.Span, out header, out var plain)) return;
        if (!sender.Replay.Accept(header.Seq) || !sender.Limiter.TryTake()) return;

        if (header.Type == PacketType.Hello || sender.UdpEndpoint is not null) sender.UdpEndpoint = from;

        switch (header.Type)
        {
            case PacketType.Hello or PacketType.Ping:
                await SendAsync(sender, new VoiceHeader(PacketType.Ping, sender.Id, sender.OutSeq.Next(), 0), []);
                break;
            // Voice plaintext is [frameSeq][opus] in both directions, so it is relayed as is.
            case PacketType.Voice when RelayPayload.TryParse(plain, out _, out var opus) && opus.Length is > 0 and <= VoiceHeader.MaxOpusSize:
                var (recipients, target) = state.VoiceRecipients(sender, header.Target);
                foreach (var r in recipients)
                    await SendAsync(r, new VoiceHeader(PacketType.Voice, sender.Id, r.OutSeq.Next(), target), plain);
                break;
        }
    }

    async Task SendAsync(Session to, VoiceHeader header, byte[] payload)
    {
        if (to.UdpEndpoint is not { } endpoint) return;
        await socket.SendToAsync(to.Crypto.Seal(Direction.ServerToClient, header, payload), SocketFlags.None, endpoint);
    }

    public void Dispose()
    {
        if (cts.IsCancellationRequested) return;
        cts.Cancel();
        socket.Dispose();
        try
        {
            loop?.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
        }
        cts.Dispose();
    }
}
