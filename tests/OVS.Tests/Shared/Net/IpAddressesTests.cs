using System.Net;
using System.Net.Sockets;
using OVS.Shared.Net;

namespace OVS.Tests.Shared.Net;

public class IpAddressesTests
{
    [Fact]
    public void Unmap_MappedOrPlain_IPv4OrUnchanged()
    {
        var unmapped = IpAddresses.Unmap(IPAddress.Parse("::ffff:192.0.2.7"));
        Assert.Equal(AddressFamily.InterNetwork, unmapped.AddressFamily);
        Assert.Equal(IPAddress.Parse("192.0.2.7"), unmapped);

        var v4 = IPAddress.Parse("192.0.2.7");
        Assert.Same(v4, IpAddresses.Unmap(v4));
        var v6 = IPAddress.Parse("2001:db8::1");
        Assert.Same(v6, IpAddresses.Unmap(v6));
        Assert.Same(IPAddress.IPv6Loopback, IpAddresses.Unmap(IPAddress.IPv6Loopback));
    }

    [Fact]
    public void ToAddressGroup_IPv4_StaysTheAddressAlsoWhenMapped()
    {
        Assert.Equal(IPAddress.Parse("192.0.2.7"), IpAddresses.ToAddressGroup(IPAddress.Parse("192.0.2.7")));
        Assert.Equal(IPAddress.Parse("192.0.2.7"), IpAddresses.ToAddressGroup(IPAddress.Parse("::ffff:192.0.2.7")));
    }

    [Fact]
    public void ToAddressGroup_IPv6_KeepsThePrefix64()
    {
        var group = IpAddresses.ToAddressGroup(IPAddress.Parse("2001:db8:1:2:aaaa:bbbb:cccc:dddd"));
        Assert.Equal(IPAddress.Parse("2001:db8:1:2::"), group);
        Assert.Equal(group, IpAddresses.ToAddressGroup(IPAddress.Parse("2001:db8:1:2::1")));
        Assert.NotEqual(group, IpAddresses.ToAddressGroup(IPAddress.Parse("2001:db8:1:3::1")));
    }
}
