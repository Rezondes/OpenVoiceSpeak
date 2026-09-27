using OVS.Shared.Protocol;

namespace OVS.Tests;

public class SmokeTests
{
    [Fact]
    public void ProtocolInfo_Defaults_AreStable()
    {
        Assert.Equal(3, ProtocolInfo.Version); // 2: server logo, 3: chat; raise it deliberately, old clients get a clear error
        Assert.Equal(7000, ProtocolInfo.DefaultPort);
    }
}
