using OVS.Shared.Net;

namespace OVS.Tests.Shared.Net;

/// <summary>Package 170: a server link carries a host and a port, nothing else.</summary>
public sealed class JoinLinkTests
{
    [Theory]
    [InlineData("ovs://gilde.example.org:7000", "gilde.example.org", 7000)]
    [InlineData("OVS://Gilde.Example.org:7000/", "Gilde.Example.org", 7000)] // browsers add the slash
    [InlineData("ovs://192.0.2.10:1", "192.0.2.10", 1)]
    [InlineData("ovs://localhost:65535", "localhost", 65535)]
    public void Parse_HostAndPort_Accepted(string text, string host, int port)
    {
        Assert.True(JoinLink.TryParse(text, out var link));
        Assert.Equal((host, port), (link.Host, link.Port));
    }

    [Fact]
    public void Parse_Ipv6InBrackets_Accepted()
    {
        Assert.True(JoinLink.TryParse("ovs://[2001:db8::1]:7000", out var link));
        Assert.Equal(("2001:db8::1", 7000, "[2001:db8::1]:7000"), (link.Host, link.Port, link.Address));
        Assert.False(JoinLink.TryParse("ovs://2001:db8::1:7000", out _)); // without brackets the port is not clear
    }

    [Theory]
    [InlineData("ovs://host:7000/path")]
    [InlineData("ovs://host:7000?x=1")]
    [InlineData("ovs://host:7000#x")]
    [InlineData("ovs://user@host:7000")]
    [InlineData("ovs://host")]
    [InlineData("ovs://-host:7000")]
    [InlineData("ovs://ho st:7000")]
    [InlineData("ovs://[192.0.2.1]:7000")]
    [InlineData("ovs://[fe80::1%25eth0]:7000")]
    [InlineData("https://host:7000")]
    [InlineData("ovs://host:7000//")]
    [InlineData(null)]
    public void Parse_PathQueryUserInfo_Refused(string? text) => Assert.False(JoinLink.TryParse(text, out _));

    [Theory]
    [InlineData("ovs://host:0")]
    [InlineData("ovs://host:65536")]
    [InlineData("ovs://host:-1")]
    [InlineData("ovs://host:")]
    [InlineData("ovs://host:007000")]
    public void Parse_PortOutOfRange_Refused(string text) => Assert.False(JoinLink.TryParse(text, out _));

    [Fact]
    public void Parse_HostLongerThan253_Refused()
    {
        var label = new string('a', 63);
        var host = string.Join('.', label, label, label, new string('b', 61)); // 253 characters
        Assert.True(JoinLink.TryParse($"ovs://{host}:1", out _));
        Assert.False(JoinLink.TryParse($"ovs://{host}b:1", out _));
        Assert.False(JoinLink.TryParse($"ovs://{new string('a', 64)}:1", out _)); // one label at most 63
    }

    [Fact]
    public void Build_ServerAddress_WebLink()
    {
        Assert.Equal("https://rezondes.github.io/OpenVoiceSpeak/?join=gilde.example.org:7000", JoinLink.For(" gilde.example.org ", 7000)!.WebLink);
        Assert.Equal("ovs://[::1]:7000", JoinLink.For("[::1]", 7000)!.ProtocolLink); // a bookmark may keep the brackets
        Assert.Null(JoinLink.For("host/path", 7000));
    }
}
