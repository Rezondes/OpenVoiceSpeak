using OVS.Server.Data.Records;

namespace OVS.Tests.Server.Data.Records;

public sealed class BanRecordTests
{
    [Fact]
    public void CountBlockedAttempt_Twice_CountsAndKeepsTheLast()
    {
        var ban = new BanRecord();
        var first = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

        ban.CountBlockedAttempt("10.0.0.1", first);
        ban.CountBlockedAttempt("10.0.0.2", first.AddMinutes(5));

        Assert.Equal((2, first.AddMinutes(5), "10.0.0.2"), (ban.BlockedAttempts, ban.LastAttempt, ban.LastAttemptIp));
    }
}
