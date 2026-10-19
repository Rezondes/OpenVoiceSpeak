using OVS.Shared.Net;

namespace OVS.Manager.Servers;

/// <summary>
/// Package M14 (MA8): a server's join link as the client reads it (<see cref="JoinLink"/>, Package 170): the public host
/// and the port. The link carries nothing else, so neither the password nor the certificate; the fingerprint is shown
/// beside it, for comparing with the client's prompt at the first connect.
/// </summary>
internal static class InviteLink
{
    /// <summary>The link of a server on <paramref name="port"/>: at <c>OVS_MANAGER_PUBLIC_HOST</c>, else at <paramref name="requestHost"/>; null for a host a link cannot carry.</summary>
    public static JoinLink? For(ManagerOptions options, string requestHost, int port) => JoinLink.For(options.PublicHost ?? requestHost, port);
}
