using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading.Channels;
using OVS.Client.Exceptions;
using OVS.Shared.Exceptions;
using OVS.Shared.Identity;
using OVS.Shared.Net;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Framing;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.VoiceRelay;

namespace OVS.Client.Net;

/// <summary>TLS control connection with TOFU pinning and the challenge handshake.</summary>
public sealed class ClientConnection : IAsyncDisposable
{
    static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(5);

    readonly TcpClient tcp;
    readonly SslStream ssl;
    readonly FrameReader reader;
    readonly FrameWriter writer;
    readonly CancellationTokenSource cts = new();
    readonly CancellationTokenSource pingCts = new(); // stopped first on dispose, before the TLS shutdown (also voice, Package 141)
    readonly TimeProvider time;
    long pingSentAt;
    int disconnectRaised;
    Task? receiveLoop, pingLoop, voiceLoop;

    ClientConnection(TcpClient tcp, SslStream ssl, Welcome welcome, TimeProvider time)
    {
        this.tcp = tcp;
        this.ssl = ssl;
        this.time = time;
        reader = new FrameReader(ssl);
        writer = new FrameWriter(ssl);
        Welcome = welcome;
    }

    public Welcome Welcome { get; }
    /// <summary>Server address for UDP. Dual-mode TCP sockets report IPv4 servers as ::ffff:a.b.c.d, which UDP cannot use.</summary>
    public IPAddress RemoteAddress
    {
        get
        {
            var address = ((IPEndPoint)tcp.Client.RemoteEndPoint!).Address;
            return IpAddresses.Unmap(address);
        }
    }
    public TimeSpan? LastRoundTrip { get; private set; }

    /// <summary>Raised on the receive thread for every message after Welcome.</summary>
    public event Action<Message>? MessageReceived;

    /// <summary>Package 141: voice over TCP, raised on the receive thread and kept out of MessageReceived (the UI thread).</summary>
    public event Action<VoiceDown>? VoiceDownReceived;

    /// <summary>Raised once: server Disconnected reason, or ConnectionLost.</summary>
    public event Action<string, string?>? Disconnected;

    /// <summary>The clock of the ping's round trip.</summary>
    internal TimeProvider Time => time;

    /// <param name="time">
    /// The clock of the ping's round trip (tests pass a manual one). The ping itself goes every 5 s of real time: it keeps
    /// the connection alive against the server's idle timeout, which runs in real time.
    /// </param>
    public static async Task<ClientConnection> ConnectAsync(string host, int port, ClientIdentity identity, string nickname,
        string? password, KnownServers known, Func<TofuPrompt, Task<bool>> confirm, TimeProvider time, CancellationToken ct = default)
    {
        var tcp = new TcpClient();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10)); // clock: real time
            await tcp.ConnectAsync(host, port, timeout.Token);

            // Self-signed server certs are pinned by fingerprint (TOFU) instead of a CA chain.
            var ssl = new SslStream(tcp.GetStream(), false, (_, _, _, _) => true);
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = host }, timeout.Token);

            var fingerprint = CertFingerprint.ComputeHex(ssl.RemoteCertificate!);
            var check = known.Check(host, port, fingerprint);
            if (check != TofuResult.Known)
            {
                timeout.CancelAfter(Timeout.InfiniteTimeSpan); // clock: real time, the user may take their time
                if (!await confirm(new TofuPrompt(host, port, fingerprint, check))) throw new TofuRejectedException();
                known.Trust(host, port, fingerprint);
                timeout.CancelAfter(TimeSpan.FromSeconds(10)); // clock: real time
            }

            var reader = new FrameReader(ssl);
            var writer = new FrameWriter(ssl);
            await writer.WriteAsync(new ClientHello(WireProtocol.Version, nickname, Convert.ToBase64String(identity.PublicKey), password), timeout.Token);
            var reply = await reader.ReadAsync(timeout.Token);
            if (reply is Challenge challenge)
            {
                var proof = identity.Sign(ClientIdentity.ProofData(Convert.FromBase64String(challenge.Nonce), CertFingerprint.Compute(ssl.RemoteCertificate!)));
                await writer.WriteAsync(new ClientProof(Convert.ToBase64String(proof)), timeout.Token);
                reply = await reader.ReadAsync(timeout.Token);
            }

            switch (reply)
            {
                case Welcome welcome:
                    var connection = new ClientConnection(tcp, ssl, welcome, time);
                    connection.Start();
                    return connection;
                case Rejected rejected:
                    throw new ConnectionRejectedException(rejected.Code, rejected.Detail);
                default:
                    throw new ConnectionRejectedException(ReasonCodes.ProtocolError, reply?.GetType().Name);
            }
        }
        catch
        {
            tcp.Dispose();
            throw;
        }
    }

    void Start()
    {
        receiveLoop = Task.Run(ReceiveLoopAsync);
        pingLoop = Task.Run(PingLoopAsync);
        voiceLoop = Task.Run(VoiceLoopAsync); // Package 141
    }

    public Task SendAsync(Message message) => writer.WriteAsync(message, cts.Token);

    // Package 141: at most 10 frames (200 ms) wait for the TLS stream, in order; more is dropped
    readonly Channel<VoiceUp> voiceOut = Channel.CreateBounded<VoiceUp>(
        new BoundedChannelOptions(10) { FullMode = BoundedChannelFullMode.DropWrite }); // two readers: the loop and DropPendingVoice

    /// <summary>Package 141 (A150): voice over the TLS connection from the audio thread; never waits, drops while the stream is behind.</summary>
    public void SendVoice(VoiceUp up) => voiceOut.Writer.TryWrite(up);

    /// <summary>Package 141: voice went back to UDP; what still waits for TCP is dropped, so it cannot arrive after the switch.</summary>
    public void DropPendingVoice()
    {
        while (voiceOut.Reader.TryRead(out _))
        {
        }
    }

    async Task VoiceLoopAsync()
    {
        try
        {
            await foreach (var up in voiceOut.Reader.ReadAllAsync(pingCts.Token)) await writer.WriteAsync(up, pingCts.Token);
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException)
        {
            // the connection is going; its end is reported by the receive loop
        }
    }

    async Task ReceiveLoopAsync()
    {
        string reason = ReasonCodes.ConnectionLost;
        string? detail = null;
        try
        {
            while (await reader.ReadAsync(cts.Token) is { } message)
            {
                switch (message)
                {
                    case Disconnected d:
                        (reason, detail) = (d.Reason, d.Detail);
                        break;
                    case Pong:
                        LastRoundTrip = time.GetElapsedTime(Interlocked.Read(ref pingSentAt));
                        break;
                    case VoiceDown voice:
                        VoiceDownReceived?.Invoke(voice);
                        break;
                    default:
                        MessageReceived?.Invoke(message);
                        break;
                }
            }
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException or ProtocolException)
        {
        }
        finally
        {
            RaiseDisconnected(reason, detail);
        }
    }

    async Task PingLoopAsync()
    {
        try
        {
            while (!pingCts.IsCancellationRequested)
            {
                Interlocked.Exchange(ref pingSentAt, time.GetTimestamp());
                await writer.WriteAsync(new Ping(), pingCts.Token);
                await Task.Delay(PingInterval, pingCts.Token); // clock: real time, keepalive against the server's real-time idle timeout
            }
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException)
        {
        }
    }

    void RaiseDisconnected(string reason, string? detail)
    {
        if (Interlocked.Exchange(ref disconnectRaised, 1) == 0) Disconnected?.Invoke(reason, detail);
    }

    /// <summary>
    /// TLS close_notify, so the server sees an orderly end ("vom Client beendet") and not a dropped connection.
    /// </summary>
    public static async Task CloseGracefullyAsync(SslStream ssl)
    {
        try
        {
            await ssl.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(1)); // clock: real time
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or NotSupportedException
                                      or InvalidOperationException or TimeoutException)
        {
            // already broken or busy: the socket close below still ends the connection
        }
    }

    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref disconnectRaised, 1); // a deliberate disconnect is not an event
        // No more pings: writing after the TLS shutdown throws.
        pingCts.Cancel();
        if (pingLoop is not null) await pingLoop;
        if (voiceLoop is not null) await voiceLoop; // Package 141: like the pings, nothing is written after the TLS shutdown
        await CloseGracefullyAsync(ssl);
        // Wait until the server has read the close_notify and closed its side. Closing the socket while its last
        // messages are still unread makes Windows send a reset, and the server logs "Verbindung abgebrochen".
        if (receiveLoop is not null) await Task.WhenAny(receiveLoop, Task.Delay(TimeSpan.FromSeconds(1))); // clock: real time
        cts.Cancel();
        tcp.Dispose();
        if (receiveLoop is not null) await receiveLoop;
        await ssl.DisposeAsync();
        cts.Dispose();
        pingCts.Dispose();
    }
}
