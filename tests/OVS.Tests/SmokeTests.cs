using OVS.Shared.Protocol;

namespace OVS.Tests;

public class SmokeTests
{
    [Fact]
    public void ProtocolInfo_Defaults_AreStable()
    {
        Assert.Equal(2, ProtocolInfo.Version); // 2: server logo (Package 30); raise it deliberately, old clients get a clear error
        Assert.Equal(7000, ProtocolInfo.DefaultPort);
    }
}
