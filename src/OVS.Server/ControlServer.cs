using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using OVS.Shared.Identity;
using OVS.Shared.Protocol;

namespace OVS.Server;

/// <summary>TLS control channel: accepts connections, runs the handshake, then pumps messages.</summary>
public sealed class ControlServer(ServerState state, X509Certificate2 certificate, IPEndPoint endpoint) : IAsyncDisposable
{
    readonly TcpListener listener = new(endpoint);
    readonly byte[] certHash = CertFingerprint.Hash(certificate);
    readonly CancellationTokenSource stopping = new();
    readonly ConcurrentDictionary<Task, byte> connections = new();
    SemaphoreSlim? handshakes; // Package 86: created on Start, after MaxPendingHandshakes is set
    Task? acceptLoop;

    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan WriteTimeout { get; init; } = Limits.WriteTimeout;
    public int MaxPendingHandshakes { get; init; } = Limits.MaxPendingHandshakes;

    public IPEndPoint LocalEndPoint => (IPEndPoint)listener.LocalEndpoint;

    public void Start()
    {
        handshakes = new SemaphoreSlim(MaxPendingHandshakes);
        listener.Start();
        acceptLoop = Task.Run(AcceptLoopAsync);
    }

    /// <summary>Tells every client the server is going down (or restarting), then waits up to 2 s for connections to close.</summary>
    public async Task StopAsync(bool restart = false)
    {
        if (stopping.IsCancellationRequested) return;
        listener.Stop(); // first, so nobody new connects to a server that is going away
        state.CloseAll(new Disconnected(restart ? Codes.ServerRestart : Codes.ServerShutdown));
        await Task.WhenAny(Task.WhenAll(connections.Keys), Task.Delay(TimeSpan.FromSeconds(2)));
        stopping.Cancel();
        if (acceptLoop is not null) await acceptLoop;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        stopping.Dispose();
    }

    async Task AcceptLoopAsync()
    {
        while (!stopping.IsCancellationRequested)
        {
            TcpClient tcp;
            try
            {
                tcp = await listener.AcceptTcpClientAsync(stopping.Token);
            }
            // InvalidOperationException: stopped before this loop got to its first accept
            catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }
            // Package 86: refused before TLS, so neither a crowd from one IP nor a handshake flood costs crypto work
            IPAddress ip;
            try
            {
                ip = ((IPEndPoint)tcp.Client.RemoteEndPoint!).Address;
            }
            catch (Exception e) when (e is SocketException or ObjectDisposedException)
            {
                tcp.Dispose();
                continue;
            }
            if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
            if (!state.TryAddConnection(ip))
            {
                state.ReleaseConnection(ip);
                state.LogRejected(ip, Codes.TooManyConnections, null, null);
                tcp.Dispose();
                continue;
            }
            if (!handshakes!.Wait(0))
            {
                state.ReleaseConnection(ip);
                tcp.Dispose();
                continue;
            }
            var task = Task.Run(() => HandleAsync(tcp, ip));
            connections.TryAdd(task, 0);
            _ = task.ContinueWith(t => connections.TryRemove(t, out _), TaskScheduler.Default);
        }
    }

    /// <summary>Owns one connection slot of ip and one handshake slot, both taken by the accept loop.</summary>
    async Task HandleAsync(TcpClient tcp, IPAddress ip)
    {
        bool handshaking = true;
        Session? session = null;
        string reason = "Verbindung abgebrochen";
        Task? writeLoop = null;
        try
        {
            using var handshake = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
            handshake.CancelAfter(HandshakeTimeout);

            var ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false);
            await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = certificate }, handshake.Token);
            var reader = new FrameReader(ssl);
            var writer = new FrameWriter(ssl);

            session = await HandshakeAsync(reader, writer, ip, handshake.Token);
            handshakes!.Release();
            handshaking = false;
            if (session is null) return;

            // Package 86: an overflowing outbox or a stuck write closes the socket at once, which ends both loops
            using var abort = session.Aborted.Register(tcp.Close);
            writeLoop = WriteLoopAsync(session, writer, tcp);
            try
            {
                await ReadLoopAsync(session, reader);
                reason = "vom Client beendet";
            }
            catch (OperationCanceledException) when (!stopping.IsCancellationRequested)
            {
                reason = "Zeitüberschreitung";
            }
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ProtocolException
                                      or AuthenticationException or SocketException or ObjectDisposedException)
        {
        }
        finally
        {
            if (handshaking) handshakes!.Release();
            if (session is not null) state.Remove(session, session.AbortReason ?? reason);
            state.ReleaseConnection(ip); // Package 86: before the write loop, which may still wait for its timeout
            if (writeLoop is not null) await writeLoop;
            session?.Dispose();
            tcp.Dispose();
        }
    }

    async Task<Session?> HandshakeAsync(FrameReader reader, FrameWriter writer, IPAddress ip, CancellationToken ct)
    {
        // Only validated values go into the log: a raw nickname could carry line breaks.
        string? logName = null, logFingerprint = null;

        async Task<Session?> Reject(string code, string? detail = null)
        {
            state.LogRejected(ip, code, logName, logFingerprint);
            await writer.WriteAsync(new Rejected(code, detail), ct);
            return null;
        }

        if (await reader.ReadAsync(ct) is not ClientHello hello) return await Reject(Codes.ProtocolError);
        if (hello.ProtocolVersion != ProtocolInfo.Version)
            return await Reject(Codes.VersionMismatch, $"Server spricht Protokollversion {ProtocolInfo.Version}");
        var nickname = ServerState.ValidName(hello.Nickname, 32);
        if (nickname is null) return await Reject(Codes.NicknameInvalid);
        logName = nickname;
        if (!TryBase64(hello.PublicKey, out var publicKey)) return await Reject(Codes.ProtocolError);

        var nonce = RandomNumberGenerator.GetBytes(32);
        await writer.WriteAsync(new Challenge(Convert.ToBase64String(nonce)), ct);

        if (await reader.ReadAsync(ct) is not ClientProof proof || !TryBase64(proof.Signature, out var signature))
            return await Reject(Codes.ProtocolError);
        if (!ClientIdentity.Verify(publicKey, ClientIdentity.ProofData(nonce, certHash), signature))
            return await Reject(Codes.BadSignature);

        logFingerprint = ClientIdentity.ComputeFingerprint(publicKey);
        var (session, rejection) = state.Admit(logFingerprint, nickname, ip, hello.Password);
        return rejection is null ? session : await Reject(rejection.Code, rejection.Detail);
    }

    async Task ReadLoopAsync(Session session, FrameReader reader)
    {
        while (true)
        {
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
            idle.CancelAfter(IdleTimeout);
            var message = await reader.ReadAsync(idle.Token);
            if (message is null) return;
            if (message is Ping) session.Send(new Pong());
            else state.Handle(session, message);
        }
    }

    async Task WriteLoopAsync(Session session, FrameWriter writer, TcpClient tcp)
    {
        try
        {
            await foreach (var frame in session.Outgoing.ReadAllAsync())
            {
                var write = writer.WriteFrameAsync(frame);
                try
                {
                    await write.WaitAsync(WriteTimeout);
                }
                catch (TimeoutException)
                {
                    _ = write.ContinueWith(t => t.Exception, TaskContinuationOptions.OnlyOnFaulted); // fails once the socket is closed
                    session.Abort("Schreiben zu langsam");
                    return;
                }
                session.Written(frame);
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or ProtocolException or SocketException)
        {
        }
        finally
        {
            tcp.Close(); // ends the read loop
        }
    }

    static bool TryBase64(string? text, out byte[] bytes)
    {
        bytes = [];
        if (text is null) return false;
        try
        {
            bytes = Convert.FromBase64String(text);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
