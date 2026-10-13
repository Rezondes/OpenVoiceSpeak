using System.Net;
using System.Net.Sockets;
using OVS.Shared.Voice;

namespace OVS.Tests.TestSupport;

/// <summary>
/// Package 126: sits between one voice client and the server and loses packets on request. Counts the client's
/// packets by type (also the dropped ones), so a test sees what the client sent. Package 133: also delays packets on
/// its own clock and drops a share of the voice packets each way.
/// </summary>
internal sealed class LossyUdpRelay : IDisposable
{
    readonly UdpClient front = new(new IPEndPoint(IPAddress.Loopback, 0));
    readonly UdpClient back;
    readonly TimeProvider time;
    readonly CancellationTokenSource stopping = new();
    readonly List<PacketType> sent = [];
    readonly Task frontLoop, backLoop;
    volatile IPEndPoint? client;
    int dropNext;

    /// <param name="time">Package 133: the clock <see cref="Delay"/> waits on (a manual one in tests).</param>
    public LossyUdpRelay(IPEndPoint server, TimeProvider? time = null)
    {
        this.time = time ?? TimeProvider.System;
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

    /// <summary>Package 133: every packet waits this long on the relay's clock, each way.</summary>
    public TimeSpan Delay { get; init; }

    /// <summary>Package 133: drops this share of the voice packets to the server, every n-th, so a count is exact.</summary>
    public int DropUpVoicePercent { get; set; }

    /// <summary>Package 133: the same for the voice packets from the server to the client.</summary>
    public int DropDownVoicePercent { get; set; }

    int voiceUp, voiceDown, passedUpVoice, passedDownVoice, held;

    /// <summary>
    /// Package 133: the voice packets passed on to the server and to the client, counted before they are sent, so a test
    /// that waits for this many to arrive never waits too short.
    /// </summary>
    public int PassedUpVoice => Volatile.Read(ref passedUpVoice);
    public int PassedDownVoice => Volatile.Read(ref passedDownVoice);

    /// <summary>Package 133: the voice packets from the server that reached the relay, also the dropped ones.</summary>
    public int SeenDownVoice => Volatile.Read(ref voiceDown);

    /// <summary>Package 133: how many packets have been held for <see cref="Delay"/> so far, both ways.</summary>
    public int Held => Volatile.Read(ref held);

    /// <summary>How many packets of this type the client sent so far.</summary>
    public int Count(PacketType type)
    {
        lock (sent) return sent.Count(packetType => packetType == type);
    }

    /// <summary>Package 133: the n-th voice packet goes when the share dropped so far would fall below the percentage.</summary>
    static bool DropVoice(ref int count, int percent)
    {
        int packetNumber = Interlocked.Increment(ref count);
        return packetNumber * percent / 100 > (packetNumber - 1) * percent / 100;
    }

    async Task FrontLoopAsync()
    {
        while (await ReceiveAsync(front) is { } result)
        {
            client = result.RemoteEndPoint;
            bool drop, voice = false;
            lock (sent)
            {
                if (VoiceHeader.TryRead(result.Buffer, out var header))
                {
                    sent.Add(header.Type);
                    voice = header.Type == PacketType.Voice;
                }
                drop = DropAll || dropNext-- > 0 || (voice && DropVoice(ref voiceUp, DropUpVoicePercent));
            }
            if (drop) continue;
            if (voice) Interlocked.Increment(ref passedUpVoice);
            await ForwardAsync(() => back.SendAsync(result.Buffer));
        }
    }

    async Task BackLoopAsync()
    {
        while (await ReceiveAsync(back) is { } result)
        {
            bool voice = VoiceHeader.TryRead(result.Buffer, out var header) && header.Type == PacketType.Voice;
            if (DropAll || client is not { } to || (voice && DropVoice(ref voiceDown, DropDownVoicePercent))) continue;
            if (voice) Interlocked.Increment(ref passedDownVoice);
            await ForwardAsync(() => front.SendAsync(result.Buffer, to));
            Interlocked.Increment(ref passedBack);
        }
    }

    /// <summary>Package 133: with a delay the packet waits on its own, so the loop takes the next one meanwhile.</summary>
    Task ForwardAsync(Func<ValueTask<int>> send)
    {
        if (Delay <= TimeSpan.Zero) return SendAsync(send);
        Interlocked.Increment(ref held);
        _ = Task.Delay(Delay, time, stopping.Token).ContinueWith(_ => SendAsync(send), TaskContinuationOptions.OnlyOnRanToCompletion);
        return Task.CompletedTask;
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
                return await udp.ReceiveAsync(stopping.Token);
            }
            catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
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
        catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
        {
        }
    }

    public void Dispose()
    {
        stopping.Cancel();
        front.Dispose();
        back.Dispose();
        Task.WaitAll([frontLoop, backLoop], TimeSpan.FromSeconds(1));
        stopping.Dispose();
    }
}
