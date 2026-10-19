using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using OVS.Shared.Exceptions;
using OVS.Shared.Identity;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Framing;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Connection;

namespace OVS.Shared.Net;

/// <summary>
/// Package 194 (A205): the TLS control connection with the challenge handshake, the ping and the message pump, shared
/// by the client (which adds voice on top, <c>ClientConnection</c>) and the web manager. The server's certificate is
/// self-signed, so the caller checks its fingerprint (the client's TOFU prompt, the manager's pinned fingerprint).
/// </summary>
public sealed class ControlConnection : IAsyncDisposable
{
    static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(5);
    static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    readonly TcpClient tcp;
    readonly SslStream ssl;
    readonly FrameReader reader;
    readonly FrameWriter writer;
    readonly CancellationTokenSource stopping = new();
    readonly CancellationTokenSource pingCts = new(); // stopped first on dispose, before the socket closes
    readonly TimeProvider time;
    long pingSentAt;
    int disconnectRaised, disposed;
    Task? receiveLoop, pingLoop;

    ControlConnection(TcpClient tcp, SslStream ssl, FrameReader reader, FrameWriter writer, Welcome welcome, TimeProvider time)
    {
        this.tcp = tcp;
        this.ssl = ssl;
        this.reader = reader;
        this.writer = writer;
        this.time = time;
        Welcome = welcome;
    }

    /// <summary>The server's welcome: the session id, the voice key and the server's state.</summary>
    public Welcome Welcome { get; }

    /// <summary>The server's address. Dual-mode TCP sockets report IPv4 servers as ::ffff:a.b.c.d, which UDP cannot use.</summary>
    public IPAddress RemoteAddress => IpAddresses.Unmap(((IPEndPoint)tcp.Client.RemoteEndPoint!).Address);

    /// <summary>The last ping's round trip, null before the first answer.</summary>
    public TimeSpan? LastRoundTrip { get; private set; }

    /// <summary>The clock of the ping's round trip.</summary>
    public TimeProvider Time => time;

    /// <summary>Raised on the receive thread for every message after Welcome but Disconnected and Pong.</summary>
    public event EventHandler<MessageReceivedEventArgs>? MessageReceived;

    /// <summary>Raised once: the server's Disconnected reason and detail, or ConnectionLost; never after a deliberate close.</summary>
    public event EventHandler<DisconnectedEventArgs>? Disconnected;

    /// <param name="host">The server's host.</param>
    /// <param name="port">The server's port.</param>
    /// <param name="identity">The own key pair.</param>
    /// <param name="nickname">The nickname to log in with.</param>
    /// <param name="password">The server password, null for none.</param>
    /// <param name="service">Package 191: the server admin's invisible session (the web manager); the client passes false.</param>
    /// <param name="checkCertificate">
    /// Gets the server certificate's SHA-256 fingerprint in lower-case hex and throws to refuse it; the connect then ends
    /// with that exception. It may take its time (a prompt): the connect timeout does not run meanwhile.
    /// </param>
    /// <param name="time">The clock of the ping's round trip; the ping itself goes every 5 s of real time against the server's idle timeout.</param>
    /// <param name="cancellation">Ends the attempt.</param>
    /// <exception cref="ConnectionRejectedException">The server refused the handshake.</exception>
    public static async Task<ControlConnection> ConnectAsync(string host, int port, ClientIdentity identity, string nickname, string? password,
        bool service, Func<string, Task> checkCertificate, TimeProvider time, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(checkCertificate);
        #pragma warning disable CA2000 // the catch disposes it on every failure, the connection owns it on success
        var tcp = new TcpClient();
        #pragma warning restore CA2000
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(ConnectTimeout); // clock: real time
            await tcp.ConnectAsync(host, port, timeout.Token);

            // Self-signed server certs are pinned by fingerprint (TOFU) instead of a CA chain.
            #pragma warning disable CA2000 // it wraps the TCP stream, which the catch closes with the client; on success the connection owns it
            #pragma warning disable CA5359 // the certificate is checked by its fingerprint right below, through checkCertificate
            var ssl = new SslStream(tcp.GetStream(), false, (_, _, _, _) => true);
            #pragma warning restore CA5359
            #pragma warning restore CA2000
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = host }, timeout.Token);

            timeout.CancelAfter(Timeout.InfiniteTimeSpan); // clock: real time, a prompt may take its time
            await checkCertificate(CertFingerprint.ComputeHex(ssl.RemoteCertificate!));
            timeout.CancelAfter(ConnectTimeout); // clock: real time

            var reader = new FrameReader(ssl);
            var writer = new FrameWriter(ssl);
            await writer.WriteAsync(new ClientHello(WireProtocol.Version, nickname, Convert.ToBase64String(identity.PublicKey), password, service), timeout.Token);
            var reply = await reader.ReadAsync(timeout.Token);
            if (reply is Challenge challenge)
            {
                var proof = identity.Sign(ClientIdentity.ProofData(ReadNonce(challenge), CertFingerprint.Compute(ssl.RemoteCertificate!)));
                await writer.WriteAsync(new ClientProof(Convert.ToBase64String(proof)), timeout.Token);
                reply = await reader.ReadAsync(timeout.Token);
            }

            switch (reply)
            {
                case Welcome welcome:
                    var connection = new ControlConnection(tcp, ssl, reader, writer, welcome, time);
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

    /// <summary>The challenge's nonce; any length decodes (the server's proof check decides), text that is not base64 fails the connect.</summary>
    /// <exception cref="ProtocolException">The nonce is no base64.</exception>
    public static byte[] ReadNonce(Challenge challenge)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        return Base64Text.TryDecode(challenge.Nonce, out var nonce) ? nonce : throw new ProtocolException("Server sent a malformed challenge");
    }

    void Start()
    {
        receiveLoop = Task.Run(ReceiveLoopAsync);
        pingLoop = Task.Run(PingLoopAsync);
    }

    /// <summary>Sends one message; <paramref name="cancellation"/> replaces the connection's own end (voice stops before the close).</summary>
    public Task SendAsync(Message message, CancellationToken cancellation = default) =>
        writer.WriteAsync(message, cancellation.CanBeCanceled ? cancellation : stopping.Token);

    async Task ReceiveLoopAsync()
    {
        string reason = ReasonCodes.ConnectionLost;
        string? detail = null;
        try
        {
            while (await reader.ReadAsync(stopping.Token) is { } message)
            {
                switch (message)
                {
                    case Disconnected disconnected:
                        (reason, detail) = (disconnected.Reason, disconnected.Detail);
                        break;
                    case Pong:
                        LastRoundTrip = time.GetElapsedTime(Interlocked.Read(ref pingSentAt));
                        break;
                    default:
                        MessageReceived?.Invoke(this, new MessageReceivedEventArgs(message));
                        break;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException or ProtocolException)
        {
        }
        finally
        {
            if (Interlocked.Exchange(ref disconnectRaised, 1) == 0) Disconnected?.Invoke(this, new DisconnectedEventArgs(reason, detail));
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
        catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException)
        {
        }
    }

    /// <summary>
    /// Ends the sending side with a TCP FIN, so the server reads the end of the stream and sees an orderly end ("ended by
    /// the client") and not a dropped connection. Not the TLS close_notify (<c>SslStream.ShutdownAsync</c>): it changes the
    /// TLS context while the receive loop may be decrypting on another thread, which crashed the test host inside SSPI's
    /// DecryptMessage on CI (0xC0000005, 18 October 2026).
    /// </summary>
    public static void CloseGracefully(TcpClient tcp)
    {
        ArgumentNullException.ThrowIfNull(tcp);
        try
        {
            tcp.Client?.Shutdown(SocketShutdown.Send); // null once the client was disposed (a second dispose)
        }
        catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
        {
            // already broken or closed: the socket close below still ends the connection
        }
    }

    /// <summary>Closes the connection; a deliberate close raises no <see cref="Disconnected"/>, a second call does nothing.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 1) return;
        Interlocked.Exchange(ref disconnectRaised, 1); // a deliberate disconnect is not an event
        // No more pings: nothing is written after the close.
        await pingCts.CancelAsync();
        if (pingLoop is not null) await pingLoop;
        CloseGracefully(tcp);
        // Wait until the server has read the end of the stream and closed its side. Closing the socket while its last
        // messages are still unread makes Windows send a reset, and the server logs "Verbindung abgebrochen".
        if (receiveLoop is not null) await Task.WhenAny(receiveLoop, Task.Delay(TimeSpan.FromSeconds(1))); // clock: real time
        await stopping.CancelAsync();
        tcp.Dispose();
        if (receiveLoop is not null) await receiveLoop;
        await ssl.DisposeAsync();
        stopping.Dispose();
        pingCts.Dispose();
    }
}
