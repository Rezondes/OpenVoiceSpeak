using OVS.Server.Data.Records;
using OVS.Server.Rules;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages.Channels;

namespace OVS.Tests.Server.Rules;

public class ChannelLinkRuleTests
{
    static readonly Guid ChannelA = Guid.NewGuid(), ChannelB = Guid.NewGuid(), ChannelC = Guid.NewGuid();

    [Theory]
    [InlineData(false, ChannelKind.Voice, ChannelKind.Voice, null)]
    [InlineData(true, ChannelKind.Voice, ChannelKind.Voice, ReasonCodes.InvalidLink)] // a channel with itself
    [InlineData(true, null, null, ReasonCodes.InvalidLink)] // the same id wins over a missing channel
    [InlineData(false, ChannelKind.Voice, null, ReasonCodes.NotFound)]
    [InlineData(false, null, ChannelKind.Voice, ReasonCodes.NotFound)]
    [InlineData(false, ChannelKind.Separator, ChannelKind.Voice, ReasonCodes.InvalidLink)]
    [InlineData(false, ChannelKind.Voice, ChannelKind.Separator, ReasonCodes.InvalidLink)]
    public void Check_ChannelsAndKinds_OnlyTwoVoiceChannels(bool same, ChannelKind? kindA, ChannelKind? kindB, string? code) =>
        Assert.Equal(code is null ? null : new Refusal(code), ChannelLinkRule.Check(ChannelA, same ? ChannelA : ChannelB, kindA, kindB));

    [Fact]
    public void CheckChanges_OneLinkSetAndRemoved_Refused() =>
        Assert.Equal(new Refusal(ReasonCodes.InvalidValue, "A link cannot be set and removed at once."),
            ChannelLinkRule.CheckChanges([ChannelLinkRecord.Of(ChannelA, ChannelB)], [ChannelLinkRecord.Of(ChannelC, ChannelA), ChannelLinkRecord.Of(ChannelB, ChannelA)]));

    [Fact]
    public void CheckChanges_Disjoint_Allowed() =>
        Assert.Null(ChannelLinkRule.CheckChanges([ChannelLinkRecord.Of(ChannelA, ChannelB)], [ChannelLinkRecord.Of(ChannelA, ChannelC)]));
}
