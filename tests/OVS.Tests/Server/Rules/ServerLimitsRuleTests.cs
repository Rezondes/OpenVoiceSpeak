using OVS.Server.Rules;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages.ServerInfo;

namespace OVS.Tests.Server.Rules;

public class ServerLimitsRuleTests
{
    [Theory]
    [InlineData(1, 0, null)]
    [InlineData(100_000, 3650, null)]
    [InlineData(0, 30, "Max users: 1 to 100000.")]
    [InlineData(100_001, 30, "Max users: 1 to 100000.")]
    [InlineData(50, -1, "Keep logs: 0 to 3650 days.")]
    [InlineData(50, 3651, "Keep logs: 0 to 3650 days.")]
    [InlineData(0, -1, "Max users: 1 to 100000.")] // the users first
    public void Check_UsersAndLogDays_RefusedOutsideTheRanges(int maxUsers, int logDays, string? detail) =>
        Assert.Equal(detail is null ? null : new Refusal(Codes.InvalidValue, detail),
            ServerLimitsRule.Check(new ServerLimits(maxUsers, logDays, true, false, new TimeOnly(4, 0))));
}
