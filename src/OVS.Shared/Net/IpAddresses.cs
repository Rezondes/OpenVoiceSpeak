using System.Net;

namespace OVS.Shared.Net;

public static class IpAddresses
{
    /// <summary>An IPv4 address that a dual-mode socket reports as ::ffff:a.b.c.d becomes a.b.c.d; any other address stays.</summary>
    public static IPAddress Unmap(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}
