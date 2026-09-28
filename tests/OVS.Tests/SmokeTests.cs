using OVS.Shared.Protocol;

namespace OVS.Tests;

public class SmokeTests
{
    [Fact]
    public void ProtocolInfo_Defaults_AreStable()
    {
        Assert.Equal(6, ProtocolInfo.Version); // 2: server logo, 3: chat, 4: muted channels, 5: channel slots, 6: reorder channels; raise it deliberately, old clients get a clear error
        Assert.Equal(7000, ProtocolInfo.DefaultPort);
    }
}
