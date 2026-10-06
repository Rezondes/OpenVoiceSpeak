using System.Net;
using System.Net.Sockets;

namespace OVS.Shared.Net;

/// <summary>Address handling shared by the server's accept loop and address groups and the client's connection.</summary>
public static class IpAddresses
{
    /// <summary>An IPv4 address that a dual-mode socket reports as ::ffff:a.b.c.d becomes a.b.c.d; any other address stays.</summary>
    public static IPAddress Unmap(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    /// <summary>
    /// The IPv4 address, or the /64 of an IPv6 address (one IPv6 host usually owns a whole /64). Package 88: the one grouping
    /// for password and token throttling, new identities, the connection limit, IP bans and the UDP source check.
    /// </summary>
    public static IPAddress ToAddressGroup(IPAddress address)
    {
        address = Unmap(address);
        if (address.AddressFamily != AddressFamily.InterNetworkV6) return address;
        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new IPAddress(bytes);
    }
}
