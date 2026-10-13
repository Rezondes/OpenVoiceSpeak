using OVS.Shared.Protocol;

namespace OVS.Tests;

public class SmokeTests
{
    [Fact]
    public void VersionAndPort_Defaults_AreStable()
    {
        Assert.Equal(16, WireProtocol.Version); // 2: server logo, 3: chat, 4: muted channels, 5: channel slots, 6: reorder channels, 7: reorder groups, 8: link matrix, 9: create with options, 10: server settings and user statistics, 11: separator channels, 12: bitrate per channel, 13: connection stats in the ping, 14: redundant frames, 15: voice over TCP, 16: error id; raise it deliberately, old clients get a clear error
        Assert.Equal(7000, ProtocolDefaults.Port);
    }

    [Fact]
    public void LimitedDefaults_Values_InsideTheirRanges()
    {
        Assert.Equal(64, ProtocolDefaults.Bitrate);
        Assert.Equal(50, ProtocolDefaults.MaxUsers);
        Assert.Equal(30, ProtocolDefaults.LogDays);
        Assert.InRange(ProtocolDefaults.Bitrate, ProtocolLimits.MinBitrate, ProtocolLimits.MaxBitrate);
        Assert.InRange(ProtocolDefaults.MaxUsers, ProtocolLimits.MaxUsersMin, ProtocolLimits.MaxUsersMax);
        Assert.InRange(ProtocolDefaults.LogDays, 0, ProtocolLimits.LogDaysMax);
    }
}
