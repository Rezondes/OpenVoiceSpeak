using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using OVS.Server.State;
using OVS.Shared.Exceptions;
using OVS.Shared.Identity;
using OVS.Shared.Net;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Framing;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.VoiceRelay;

namespace OVS.Server;

/// <summary>TLS control channel: accepts connections, runs the handshake, then pumps messages.</summary>
internal sealed class ControlServer(ServerState state, X509Certificate2 certificate, IPEndPoint endpoint) : IAsyncDisposable
{
    readonly TcpListener listener = new(endpoint);
    readonly byte[] certHash = CertFingerprint.Compute(certificate);
    readonly CancellationTokenSource stopping = new();
    readonly ConcurrentDictionary<Task, byte> connections = new();
    #pragma warning disable CA2213 // handshakes still in flight release it after the server stopped; it hands out no wait handle
    SemaphoreSlim? handshakes; // Package 86: created on Start, after MaxPendingHandshakes is set
    #pragma warning restore CA2213
    Task? acceptLoop;

    /// <summary>How long a client may take from connecting to the end of the handshake.</summary>
    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(10);
    /// <summary>A session that sends nothing (not even a ping) this long is closed.</summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromSeconds(15);
    /// <summary>How long writing one frame may take before the client is dropped.</summary>
    public TimeSpan WriteTimeout { get; init; } = ServerTuning.WriteTimeout;
    /// <summary>Handshakes running at the same time, server-wide.</summary>
    public int MaxPendingHandshakes { get; init; } = ServerTuning.MaxPendingHandshakes;

    /// <summary>The address the listener is bound to (with the real port when 0 was asked for).</summary>
    public IPEndPoint LocalEndPoint => (IPEndPoint)listener.LocalEndpoint;

    /// <summary>Starts listening and accepting.</summary>
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
        state.CloseAll(new Disconnected(restart ? ReasonCodes.ServerRestart : ReasonCodes.ServerShutdown));
        await Task.WhenAny(Task.WhenAll(connections.Keys), Task.Delay(TimeSpan.FromSeconds(2))); // clock: real time, the bound of the wait for real sockets to close
        await stopping.CancelAsync();
        if (acceptLoop is not null) await acceptLoop;
    }

    /// <summary>Stops and releases the listener.</summary>
    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        stopping.Dispose();
        listener.Dispose();
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
            catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }
            // Package 86: refused before TLS, so neither a crowd from one IP nor a handshake flood costs crypto work
            IPAddress ip;
            try
            {
                ip = ((IPEndPoint)tcp.Client.RemoteEndPoint!).Address;
            }
            catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
            {
                tcp.Dispose();
                continue;
            }
            ip = IpAddresses.Unmap(ip);
            if (!state.TryAddConnection(ip))
            {
                state.ReleaseConnection(ip);
                state.LogRejected(ip, ReasonCodes.TooManyConnections, null, null);
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
            _ = task.ContinueWith(otherTask => connections.TryRemove(otherTask, out _), TaskScheduler.Default);
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
            using var abort = session.Outbox.Aborted.Register(tcp.Close);
            writeLoop = WriteLoopAsync(session, writer, tcp);
            try
            {
                await ReadLoopAsync(session, reader);
                reason = "ended by the client";
            }
            catch (OperationCanceledException) when (!stopping.IsCancellationRequested)
            {
                reason = "timed out";
            }
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or ProtocolException
                                      or AuthenticationException or SocketException or ObjectDisposedException)
        {
        }
        finally
        {
            if (handshaking) handshakes!.Release();
            if (session is not null) state.Remove(session, session.Outbox.AbortReason ?? reason);
            state.ReleaseConnection(ip); // Package 86: before the write loop, which may still wait for its timeout
            if (writeLoop is not null) await writeLoop;
            session?.Dispose();
            tcp.Dispose();
        }
    }

    async Task<Session?> HandshakeAsync(FrameReader reader, FrameWriter writer, IPAddress ip, CancellationToken cancellation)
    {
        // Only validated values go into the log: a raw nickname could carry line breaks.
        string? logName = null, logFingerprint = null;

        async Task<Session?> RejectAsync(string code, string? detail = null)
        {
            state.LogRejected(ip, code, logName, logFingerprint);
            await writer.WriteAsync(new Rejected(code, detail), cancellation);
            return null;
        }

        if (await reader.ReadAsync(cancellation) is not ClientHello hello) return await RejectAsync(ReasonCodes.ProtocolError);
        if (hello.ProtocolVersion != WireProtocol.Version)
            return await RejectAsync(ReasonCodes.VersionMismatch, $"Server spricht Protokollversion {WireProtocol.Version}");
        var nickname = TextRules.NormalizeName(hello.Nickname, ProtocolLimits.MaxNicknameLength);
        if (nickname is null) return await RejectAsync(ReasonCodes.NicknameInvalid);
        logName = nickname;
        if (!Base64Text.TryDecode(hello.PublicKey, out var publicKey)) return await RejectAsync(ReasonCodes.ProtocolError);

        var nonce = RandomNumberGenerator.GetBytes(32);
        await writer.WriteAsync(new Challenge(Convert.ToBase64String(nonce)), cancellation);

        if (await reader.ReadAsync(cancellation) is not ClientProof proof || !Base64Text.TryDecode(proof.Signature, out var signature))
            return await RejectAsync(ReasonCodes.ProtocolError);
        if (!ClientIdentity.Verify(publicKey, ClientIdentity.ProofData(nonce, certHash), signature))
            return await RejectAsync(ReasonCodes.BadSignature);

        logFingerprint = ClientIdentity.ComputeFingerprint(publicKey);
        var (session, rejection) = state.Admit(logFingerprint, nickname, ip, hello.Password);
        return rejection is null ? session : await RejectAsync(rejection.Code, rejection.Detail);
    }

    async Task ReadLoopAsync(Session session, FrameReader reader)
    {
        while (true)
        {
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
            idle.CancelAfter(IdleTimeout); // clock: real time, the bound of a read on a real socket
            Message? message;
            try
            {
                message = await reader.ReadAsync(idle.Token);
            }
            catch (InvalidRequestException exception) // Package 83: a bad request is answered, only a broken frame ends the connection
            {
                state.HandleMalformed(session, exception.RequestId, exception.Message);
                continue;
            }
            if (message is null) return;
            if (message is Ping) session.Outbox.Send(new Pong());
            else if (message is VoiceUp up) await (state.VoiceOverTcp?.Invoke(session, up) ?? Task.CompletedTask); // Package 141: no request
            else state.Handle(session, message);
        }
    }

    async Task WriteLoopAsync(Session session, FrameWriter writer, TcpClient tcp)
    {
        try
        {
            await foreach (var frame in session.Outbox.Outgoing.ReadAllAsync())
            {
                var write = writer.WriteFrameAsync(frame);
                try
                {
                    await write.WaitAsync(WriteTimeout);
                }
                catch (TimeoutException)
                {
                    _ = write.ContinueWith(task => task.Exception, TaskContinuationOptions.OnlyOnFaulted); // fails once the socket is closed
                    session.Outbox.Abort("writing too slow");
                    return;
                }
                session.Outbox.Written(frame);
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or ProtocolException or SocketException)
        {
        }
        finally
        {
            tcp.Close(); // ends the read loop
        }
    }
}
