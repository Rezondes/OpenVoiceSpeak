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
    Task? acceptLoop;

    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromSeconds(15);

    public IPEndPoint LocalEndPoint => (IPEndPoint)listener.LocalEndpoint;

    public void Start()
    {
        listener.Start();
        acceptLoop = Task.Run(AcceptLoopAsync);
    }

    /// <summary>Tells every client the server is going down, then waits up to 2 s for connections to close.</summary>
    public async Task StopAsync()
    {
        if (stopping.IsCancellationRequested) return;
        state.CloseAll(new Disconnected(Codes.ServerShutdown));
        listener.Stop();
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
            var task = Task.Run(() => HandleAsync(tcp));
            connections.TryAdd(task, 0);
            _ = task.ContinueWith(t => connections.TryRemove(t, out _), TaskScheduler.Default);
        }
    }

    async Task HandleAsync(TcpClient tcp)
    {
        var ip = ((IPEndPoint)tcp.Client.RemoteEndPoint!).Address;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        bool allowed = state.TryAddConnection(ip);
        Session? session = null;
        Task? writeLoop = null;
        try
        {
            using var handshake = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
            handshake.CancelAfter(HandshakeTimeout);

            var ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false);
            await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = certificate }, handshake.Token);
            var reader = new FrameReader(ssl);
            var writer = new FrameWriter(ssl);

            if (!allowed)
            {
                await writer.WriteAsync(new Rejected(Codes.TooManyConnections), handshake.Token);
                return;
            }

            session = await HandshakeAsync(reader, writer, ip, handshake.Token);
            if (session is null) return;

            writeLoop = WriteLoopAsync(session, writer, tcp);
            await ReadLoopAsync(session, reader);
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ProtocolException
                                      or AuthenticationException or SocketException or ObjectDisposedException)
        {
        }
        finally
        {
            if (session is not null) state.Remove(session);
            if (writeLoop is not null) await writeLoop;
            state.ReleaseConnection(ip);
            session?.Dispose();
            tcp.Dispose();
        }
    }

    async Task<Session?> HandshakeAsync(FrameReader reader, FrameWriter writer, IPAddress ip, CancellationToken ct)
    {
        async Task<Session?> Reject(string code, string? detail = null)
        {
            await writer.WriteAsync(new Rejected(code, detail), ct);
            return null;
        }

        if (await reader.ReadAsync(ct) is not ClientHello hello) return await Reject(Codes.ProtocolError);
        if (hello.ProtocolVersion != ProtocolInfo.Version)
            return await Reject(Codes.VersionMismatch, $"Server spricht Protokollversion {ProtocolInfo.Version}");
        var nickname = ServerState.ValidName(hello.Nickname, 32);
        if (nickname is null) return await Reject(Codes.NicknameInvalid);
        if (!TryBase64(hello.PublicKey, out var publicKey)) return await Reject(Codes.ProtocolError);

        var nonce = RandomNumberGenerator.GetBytes(32);
        await writer.WriteAsync(new Challenge(Convert.ToBase64String(nonce)), ct);

        if (await reader.ReadAsync(ct) is not ClientProof proof || !TryBase64(proof.Signature, out var signature))
            return await Reject(Codes.ProtocolError);
        if (!ClientIdentity.Verify(publicKey, ClientIdentity.ProofData(nonce, certHash), signature))
            return await Reject(Codes.BadSignature);

        var (session, rejection) = state.Admit(ClientIdentity.ComputeFingerprint(publicKey), nickname, ip, hello.Password);
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

    static async Task WriteLoopAsync(Session session, FrameWriter writer, TcpClient tcp)
    {
        try
        {
            await foreach (var message in session.Outgoing.ReadAllAsync())
                await writer.WriteAsync(message);
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
