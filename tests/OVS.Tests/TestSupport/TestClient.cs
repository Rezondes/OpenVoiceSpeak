using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading.Channels;
using OVS.Shared.Identity;
using OVS.Shared.Protocol;

namespace OVS.Tests.TestSupport;

/// <summary>Raw protocol client for server tests. Can misbehave on purpose.</summary>
public sealed class TestClient : IAsyncDisposable
{
    readonly TcpClient tcp;
    readonly SslStream ssl;
    readonly FrameReader reader;
    readonly FrameWriter writer;
    readonly Channel<Message> inbox = Channel.CreateUnbounded<Message>();
    Task? pump;

    TestClient(TcpClient tcp, SslStream ssl, ClientIdentity identity)
    {
        this.tcp = tcp;
        this.ssl = ssl;
        Identity = identity;
        reader = new FrameReader(ssl);
        writer = new FrameWriter(ssl);
        ServerCertHash = CertFingerprint.Hash(ssl.RemoteCertificate!);
    }

    public ClientIdentity Identity { get; }
    public byte[] ServerCertHash { get; }
    public Welcome Welcome { get; private set; } = null!;
    public uint Id => Welcome.SessionId;

    /// <summary>TCP + TLS only, no handshake yet.</summary>
    public static async Task<TestClient> OpenAsync(int port, ClientIdentity? identity = null)
    {
        var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, port);
        var ssl = new SslStream(tcp.GetStream(), false, (_, _, _, _) => true);
        await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "localhost" });
        return new TestClient(tcp, ssl, identity ?? ClientIdentity.Create());
    }

    public static async Task<TestClient> ConnectAsync(TestServer server, string? nickname = null,
        ClientIdentity? identity = null, string? password = null)
    {
        var client = await OpenAsync(server.Port, identity);
        var result = await client.HandshakeAsync(nickname ?? "user" + Random.Shared.Next(100_000), password);
        Assert.IsType<Welcome>(result);
        return client;
    }

    /// <returns>Welcome on success, otherwise the Rejected message.</returns>
    public async Task<Message> HandshakeAsync(string nickname, string? password = null, int version = ProtocolInfo.Version,
        bool badSignature = false, byte[]? certHash = null)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await writer.WriteAsync(new ClientHello(version, nickname, Convert.ToBase64String(Identity.PublicKey), password), cts.Token);
        var first = await reader.ReadAsync(cts.Token) ?? throw new EndOfStreamException();
        if (first is not Challenge challenge) return first;

        var signature = Identity.Sign(ClientIdentity.ProofData(Convert.FromBase64String(challenge.Nonce), certHash ?? ServerCertHash));
        if (badSignature) signature[^1] ^= 0xFF;
        await writer.WriteAsync(new ClientProof(Convert.ToBase64String(signature)), cts.Token);

        var result = await reader.ReadAsync(cts.Token) ?? throw new EndOfStreamException();
        if (result is Welcome welcome)
        {
            Welcome = welcome;
            pump = Task.Run(PumpAsync);
        }
        return result;
    }

    /// <summary>Reads one frame without the pump (for pre-handshake tests). Null means closed.</summary>
    public async Task<Message?> ReadRawAsync(int timeoutMs = 3000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            return await reader.ReadAsync(cts.Token);
        }
        catch (IOException)
        {
            return null;
        }
    }

    async Task PumpAsync()
    {
        try
        {
            while (await reader.ReadAsync() is { } message) await inbox.Writer.WriteAsync(message);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or ProtocolException)
        {
        }
        finally
        {
            inbox.Writer.TryComplete();
        }
    }

    public Task SendAsync(Message message) => writer.WriteAsync(message);

    /// <summary>Next message, or null when the connection closed. Throws on timeout.</summary>
    public async Task<Message?> NextAsync(int timeoutMs = 3000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            return await inbox.Reader.ReadAsync(cts.Token);
        }
        catch (ChannelClosedException)
        {
            return null;
        }
    }

    /// <summary>Skips other messages until a matching T arrives.</summary>
    public async Task<T> WaitForAsync<T>(Func<T, bool>? match = null, int timeoutMs = 3000) where T : Message
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (true)
        {
            int left = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
            if (left <= 0) throw new TimeoutException($"No {typeof(T).Name} within {timeoutMs} ms");
            Message? m;
            try
            {
                m = await NextAsync(left);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException($"No {typeof(T).Name} within {timeoutMs} ms");
            }
            if (m is null) throw new EndOfStreamException($"Closed while waiting for {typeof(T).Name}");
            if (m is T t && (match?.Invoke(t) ?? true)) return t;
        }
    }

    public Task<Error> ErrorAsync(string requestId) => WaitForAsync<Error>(e => e.RequestId == requestId);

    public async Task AssertNoMessageAsync<T>(int ms = 300) where T : Message
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(ms);
        while (true)
        {
            int left = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
            if (left <= 0) return;
            Message? m;
            try
            {
                m = await NextAsync(left);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            if (m is null) return;
            Assert.False(m is T, $"Unexpected {m}");
        }
    }

    /// <summary>True once the server closed the connection.</summary>
    public async Task<bool> WaitClosedAsync(int timeoutMs = 3000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (await NextAsync((int)Math.Max(1, (deadline - DateTime.UtcNow).TotalMilliseconds)) is null) return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }
        return false;
    }

    public async ValueTask DisposeAsync()
    {
        await OVS.Client.Net.ClientConnection.CloseGracefullyAsync(ssl);
        tcp.Dispose();
        if (pump is not null) await pump;
        await ssl.DisposeAsync();
    }
}
