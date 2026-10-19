using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;

namespace OVS.Manager.Servers;

/// <summary>
/// Package M8 (AC4): a TLS handshake with the server's control port and nothing more; the server drops such a
/// connection without a log line. The certificate is not checked: nothing is sent, the probe only asks "does it answer".
/// </summary>
internal sealed class TlsServerProbe : IServerProbe
{
    static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(2);

    /// <inheritdoc/>
    public async Task<bool> AnswersAsync(string host, int port, CancellationToken cancellation = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(HandshakeTimeout); // clock: real time, a socket call
        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(host, port, timeout.Token);
            await using var ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false);
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = host,
#pragma warning disable CA5359 // the server's certificate is self-signed (clients pin it on first use); the probe sends no data
                RemoteCertificateValidationCallback = (_, _, _, _) => true,
#pragma warning restore CA5359
            }, timeout.Token);
            return true;
        }
        catch (Exception exception) when (exception is IOException or SocketException or AuthenticationException or OperationCanceledException)
        {
            return false;
        }
    }
}
