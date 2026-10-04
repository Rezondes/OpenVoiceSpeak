using System.Net;
using System.Net.Sockets;
using OVS.Shared.Voice;

namespace OVS.Tests.TestSupport;

/// <summary>
/// Package 126: sits between one voice client and the server and loses packets on request. Counts the client's
/// packets by type (also the dropped ones), so a test sees what the client sent.
/// </summary>
public sealed class LossyUdpRelay : IDisposable
{
    readonly UdpClient front = new(new IPEndPoint(IPAddress.Loopback, 0));
    readonly UdpClient back;
    readonly CancellationTokenSource cts = new();
    readonly List<PacketType> sent = [];
    readonly Task frontLoop, backLoop;
    volatile IPEndPoint? client;
    int dropNext;

    public LossyUdpRelay(IPEndPoint server)
    {
        back = new UdpClient(server.AddressFamily);
        back.Connect(server);
        frontLoop = Task.Run(FrontLoopAsync);
        backLoop = Task.Run(BackLoopAsync);
    }

    /// <summary>Where the client sends to.</summary>
    public IPEndPoint EndPoint => (IPEndPoint)front.Client.LocalEndPoint!;

    /// <summary>Drops this many of the client's next packets.</summary>
    public int DropNext
    {
        set
        {
            lock (sent) dropNext = value;
        }
    }

    /// <summary>Drops everything, both ways.</summary>
    public volatile bool DropAll;

    /// <summary>How many packets of this type the client sent so far.</summary>
    public int Count(PacketType type)
    {
        lock (sent) return sent.Count(t => t == type);
    }

    async Task FrontLoopAsync()
    {
        while (await ReceiveAsync(front) is { } result)
        {
            client = result.RemoteEndPoint;
            bool drop;
            lock (sent)
            {
                if (VoiceHeader.TryRead(result.Buffer, out var header)) sent.Add(header.Type);
                drop = DropAll || dropNext-- > 0;
            }
            if (!drop) await SendAsync(() => back.SendAsync(result.Buffer));
        }
    }

    async Task BackLoopAsync()
    {
        while (await ReceiveAsync(back) is { } result)
            if (!DropAll && client is { } to)
            {
                await SendAsync(() => front.SendAsync(result.Buffer, to));
                Interlocked.Increment(ref passedBack);
            }
    }

    int passedBack;

    /// <summary>How many server packets went on to the client.</summary>
    public int PassedBack => Volatile.Read(ref passedBack);

    async Task<UdpReceiveResult?> ReceiveAsync(UdpClient udp)
    {
        while (true)
        {
            try
            {
                return await udp.ReceiveAsync(cts.Token);
            }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException)
            {
                return null;
            }
            catch (SocketException)
            {
                // e.g. ICMP port unreachable reported on Windows
            }
        }
    }

    static async Task SendAsync(Func<ValueTask<int>> send)
    {
        try
        {
            await send();
        }
        catch (Exception e) when (e is SocketException or ObjectDisposedException)
        {
        }
    }

    public void Dispose()
    {
        cts.Cancel();
        front.Dispose();
        back.Dispose();
        Task.WaitAll([frontLoop, backLoop], TimeSpan.FromSeconds(1));
        cts.Dispose();
    }
}
