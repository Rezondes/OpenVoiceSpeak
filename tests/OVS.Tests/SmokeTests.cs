using OVS.Shared.Protocol;

namespace OVS.Tests;

public class SmokeTests
{
    [Fact]
    public void ProtocolInfo_Defaults_AreStable()
    {
        Assert.Equal(11, ProtocolInfo.Version); // 2: server logo, 3: chat, 4: muted channels, 5: channel slots, 6: reorder channels, 7: reorder groups, 8: link matrix, 9: create with options, 10: server settings and user statistics, 11: separator channels; raise it deliberately, old clients get a clear error
        Assert.Equal(7000, ProtocolInfo.DefaultPort);
    }
}
