using System.Net;
using System.Net.Sockets;
using OVS.Manager.Telemetry;

namespace OVS.Manager.Notifications;

/// <summary>
/// Package M27: webhooks reach only public addresses. Every connection checks the address it resolves to, so a user's URL
/// can never reach the Docker socket proxy, the servers or anything else in the private networks (also after a DNS change).
/// </summary>
internal static class PublicOnly
{
    /// <summary>The <c>SocketsHttpHandler.ConnectCallback</c>: connects to the first public address of the host.</summary>
    /// <exception cref="HttpRequestException">The host has only loopback or private addresses.</exception>
    public static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellation)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellation);
        var address = addresses.FirstOrDefault(candidate => !TelemetryReceiver.IsInternal(candidate))
                      ?? throw new HttpRequestException($"{context.DnsEndPoint.Host} is no public address; webhooks reach only public addresses.");
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellation);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>A URL a user may enter: absolute http or https.</summary>
    public static bool IsWebhookUrl(string text) =>
        Uri.TryCreate(text.Trim(), UriKind.Absolute, out var url) && url.Scheme is "https" or "http" && url.Host.Length > 0;
}
