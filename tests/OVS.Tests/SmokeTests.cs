using OVS.Shared.Protocol;

namespace OVS.Tests;

public class SmokeTests
{
    [Fact]
    public void ProtocolInfo_Defaults_AreStable()
    {
        Assert.Equal(1, ProtocolInfo.Version);
        Assert.Equal(7000, ProtocolInfo.DefaultPort);
    }
}
