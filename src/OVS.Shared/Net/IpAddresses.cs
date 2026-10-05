using System.Net;

namespace OVS.Shared.Net;

/// <summary>Address handling shared by the server's accept loop and address groups and the client's connection.</summary>
public static class IpAddresses
{
    /// <summary>An IPv4 address that a dual-mode socket reports as ::ffff:a.b.c.d becomes a.b.c.d; any other address stays.</summary>
    public static IPAddress Unmap(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}
