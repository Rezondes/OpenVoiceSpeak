using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using OVS.Shared.Identity;
using OVS.Shared.Protocol;

namespace OVS.Client.Net;

public sealed record TofuPrompt(string Host, int Port, string Fingerprint, TofuResult Result);

public sealed class ConnectionRejectedException(string code, string? detail) : Exception($"{code}: {detail}")
{
    public string Code { get; } = code;
    public string? Detail { get; } = detail;
}

public sealed class TofuRejectedException() : Exception("Serverzertifikat nicht akzeptiert");

/// <summary>TLS control connection with TOFU pinning and the challenge handshake.</summary>
public sealed class ClientConnection : IAsyncDisposable
{
    static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(5);

    readonly TcpClient tcp;
    readonly SslStream ssl;
    readonly FrameReader reader;
    readonly FrameWriter writer;
    readonly CancellationTokenSource cts = new();
    long pingSentAt;
    int disconnectRaised;
    Task? receiveLoop, pingLoop;

    ClientConnection(TcpClient tcp, SslStream ssl, Welcome welcome)
    {
        this.tcp = tcp;
        this.ssl = ssl;
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
            return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        }
    }
    public TimeSpan? LastRoundTrip { get; private set; }

    /// <summary>Raised on the receive thread for every message after Welcome.</summary>
    public event Action<Message>? MessageReceived;

    /// <summary>Raised once: server Disconnected reason, or ConnectionLost.</summary>
    public event Action<string, string?>? Disconnected;

    public static async Task<ClientConnection> ConnectAsync(string host, int port, ClientIdentity identity, string nickname,
        string? password, KnownServers known, Func<TofuPrompt, Task<bool>> confirm, CancellationToken ct = default)
    {
        var tcp = new TcpClient();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            await tcp.ConnectAsync(host, port, timeout.Token);

            // Self-signed server certs are pinned by fingerprint (TOFU) instead of a CA chain.
            var ssl = new SslStream(tcp.GetStream(), false, (_, _, _, _) => true);
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = host }, timeout.Token);

            var fingerprint = CertFingerprint.Of(ssl.RemoteCertificate!);
            var check = known.Check(host, port, fingerprint);
            if (check != TofuResult.Known)
            {
                timeout.CancelAfter(Timeout.InfiniteTimeSpan); // the user may take their time
                if (!await confirm(new TofuPrompt(host, port, fingerprint, check))) throw new TofuRejectedException();
                known.Trust(host, port, fingerprint);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
            }

            var reader = new FrameReader(ssl);
            var writer = new FrameWriter(ssl);
            await writer.WriteAsync(new ClientHello(ProtocolInfo.Version, nickname, Convert.ToBase64String(identity.PublicKey), password), timeout.Token);
            var reply = await reader.ReadAsync(timeout.Token);
            if (reply is Challenge challenge)
            {
                var proof = identity.Sign(ClientIdentity.ProofData(Convert.FromBase64String(challenge.Nonce), CertFingerprint.Hash(ssl.RemoteCertificate!)));
                await writer.WriteAsync(new ClientProof(Convert.ToBase64String(proof)), timeout.Token);
                reply = await reader.ReadAsync(timeout.Token);
            }

            switch (reply)
            {
                case Welcome welcome:
                    var connection = new ClientConnection(tcp, ssl, welcome);
                    connection.Start();
                    return connection;
                case Rejected rejected:
                    throw new ConnectionRejectedException(rejected.Code, rejected.Detail);
                default:
                    throw new ConnectionRejectedException(Codes.ProtocolError, reply?.GetType().Name);
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
    }

    public Task SendAsync(Message message) => writer.WriteAsync(message, cts.Token);

    async Task ReceiveLoopAsync()
    {
        string reason = Codes.ConnectionLost;
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
                        LastRoundTrip = Stopwatch.GetElapsedTime(Interlocked.Read(ref pingSentAt));
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
            while (!cts.IsCancellationRequested)
            {
                Interlocked.Exchange(ref pingSentAt, Stopwatch.GetTimestamp());
                await writer.WriteAsync(new Ping(), cts.Token);
                await Task.Delay(PingInterval, cts.Token);
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

    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref disconnectRaised, 1); // a deliberate disconnect is not an event
        cts.Cancel();
        tcp.Dispose();
        foreach (var loop in new[] { receiveLoop, pingLoop })
            if (loop is not null) await loop;
        await ssl.DisposeAsync();
        cts.Dispose();
    }
}
