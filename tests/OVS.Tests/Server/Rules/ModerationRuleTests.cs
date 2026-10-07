using OVS.Server.Rules;
using OVS.Shared.Protocol;

namespace OVS.Tests.Server.Rules;

public class ModerationRuleTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("Spam im Chat", true)]
    [InlineData("a\nb", false)]
    [InlineData("a\u0007b", false)]
    [InlineData("bidi ‮", false)]
    public void CheckReason_Text_OneLineWithoutControlCharacters(string? reason, bool allowed) =>
        Assert.Equal(allowed ? null : new Refusal(ReasonCodes.InvalidValue, "A reason has at most 200 characters and no line breaks or control characters."),
            ModerationRule.CheckReason(reason));

    [Theory]
    [InlineData(ProtocolLimits.MaxReasonLength, true)]
    [InlineData(ProtocolLimits.MaxReasonLength + 1, false)]
    public void CheckReason_Length_AtMostMax(int length, bool allowed) =>
        Assert.Equal(allowed, ModerationRule.CheckReason(new string('x', length)) is null);

    [Theory]
    [InlineData(null, true)] // permanent
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(-5, false)]
    public void CheckDuration_Minutes_PositiveOrPermanent(int? minutes, bool allowed) =>
        Assert.Equal(allowed ? null : new Refusal(ReasonCodes.InvalidValue, "The duration must be positive."), ModerationRule.CheckDuration(minutes));
}
