using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using OVS.Server.Tls;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol.Framing;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Connection;

namespace OVS.Tests.TestSupport;

/// <summary>
/// A server end of the handshake only, on loopback TLS with the certificate <see cref="TestServer"/> uses: it takes one client,
/// reads its <see cref="ClientHello"/>, answers with the scripted message (a <see cref="Challenge"/> or a <see cref="Welcome"/>)
/// and then reads on until the client closes the socket (<see cref="ClientClosed"/>). Used where a real server never sends
/// what the test needs (a malformed nonce or voice key).
/// </summary>
sealed class HandshakeStub : IAsyncDisposable
{
    readonly TcpListener listener = new(IPAddress.Loopback, 0);
    readonly CancellationTokenSource stop = new();
    readonly TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly Task serving;

    HandshakeStub(string dataDir, Message reply)
    {
        listener.Start();
        serving = ServeAsync(dataDir, reply);
    }

    /// <summary>Listens at once; <paramref name="dataDir"/> holds the certificate.</summary>
    public static HandshakeStub Start(string dataDir, Message reply) => new(dataDir, reply);

    /// <summary>The welcome of session 1 ("anna" alone on a server named "Stub") with that voice key.</summary>
    public static Welcome WelcomeWith(string voiceKey) =>
        new(1, voiceKey, Snapshots.Snapshot("Stub", "", Guid.Empty, [], [], [Snapshots.User(1, "anna", Guid.Empty, Permission.Speak)]));

    public int Port =>((IPEndPoint)listener.LocalEndpoint).Port;

    /// <summary>Completes once the client closed the socket after the scripted answer; wrap it in <see cref="HangGuard.Bound(Task)"/>.</summary>
    public Task ClientClosed => closed.Task;

    async Task ServeAsync(string dataDir, Message reply)
    {
        try
        {
            using var certificate = ServerCertificate.LoadOrCreate(dataDir);
            using var tcp = await listener.AcceptTcpClientAsync(stop.Token);
            await using var ssl = new SslStream(tcp.GetStream());
            await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = certificate }, stop.Token);
            var reader = new FrameReader(ssl);
            if (await reader.ReadAsync(stop.Token) is not ClientHello) return;
            await new FrameWriter(ssl).WriteAsync(reply, stop.Token);
            while (await reader.ReadAsync(stop.Token) is not null)
            {
                // the client's proof or pings: only its end counts
            }
            closed.TrySetResult();
        }
        catch (IOException)
        {
            closed.TrySetResult(); // a reset is a close too
        }
        catch (OperationCanceledException)
        {
            // disposed before the client went
        }
    }

    public async ValueTask DisposeAsync()
    {
        await stop.CancelAsync();
        listener.Stop();
        try
        {
            await serving;
        }
        catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
        {
            // the listener stopped under the accept
        }
        stop.Dispose();
    }
}
