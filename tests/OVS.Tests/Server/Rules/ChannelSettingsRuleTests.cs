using OVS.Server.Rules;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages.Channels;

namespace OVS.Tests.Server.Rules;

public class ChannelSettingsRuleTests
{
    static readonly Guid Known = Guid.NewGuid();

    [Theory]
    [InlineData(ChannelKind.Voice, true)]
    [InlineData(ChannelKind.Separator, true)]
    [InlineData((ChannelKind)7, false)]
    public void CheckKind_Kind_OnlyKnownKindsAllowed(ChannelKind kind, bool allowed) =>
        Assert.Equal(allowed ? null : new Refusal(Codes.InvalidValue, "Unknown channel kind."), ChannelSettingsRule.CheckKind(kind));

    public static TheoryData<CreateChannel, bool> SeparatorCases => new()
    {
        { new CreateChannel("", "", Kind: ChannelKind.Separator), true },
        { new CreateChannel(null!, null!, AllowedGroupIds: [], Password: "", Kind: ChannelKind.Separator), true },
        { new CreateChannel("Name", "", Kind: ChannelKind.Separator), false },
        { new CreateChannel("", "Text", Kind: ChannelKind.Separator), false },
        { new CreateChannel("", "", IsMuted: true, Kind: ChannelKind.Separator), false },
        { new CreateChannel("", "", MaxUsers: 1, Kind: ChannelKind.Separator), false },
        { new CreateChannel("", "", AllowedGroupIds: [Known], Kind: ChannelKind.Separator), false },
        { new CreateChannel("", "", Password: "p", Kind: ChannelKind.Separator), false },
        { new CreateChannel("", "", Kind: ChannelKind.Separator, Bitrate: 32), false },
    };

    [Theory]
    [MemberData(nameof(SeparatorCases))]
    public void CheckSeparator_Options_RefusedUnlessNone(CreateChannel request, bool allowed) =>
        Assert.Equal(allowed ? null : new Refusal(Codes.InvalidValue, "A separator has no name, description or options."),
            ChannelSettingsRule.CheckSeparator(request));

    [Theory]
    [InlineData(ChannelKind.Voice, true)]
    [InlineData(ChannelKind.Separator, false)]
    public void CheckEditable_Kind_SeparatorRefused(ChannelKind kind, bool allowed) =>
        Assert.Equal(allowed ? null : new Refusal(Codes.InvalidValue, "A separator cannot be edited."), ChannelSettingsRule.CheckEditable(kind));

    [Theory]
    [InlineData(" Raid ", " Text \n zwei ", "Raid", "Text \n zwei")]
    [InlineData("Raid", null, "Raid", "")]
    public void CheckNameAndDescription_ValidTexts_AllowedAndTrimmed(string rawName, string? rawDescription, string name, string description)
    {
        Assert.Null(ChannelSettingsRule.CheckNameAndDescription(rawName, rawDescription, out var checkedName, out var checkedDescription));
        Assert.Equal((name, description), (checkedName, checkedDescription));
    }

    [Theory]
    [InlineData(" ")]
    [InlineData(null)]
    public void CheckNameAndDescription_NoName_InvalidName(string? rawName) =>
        Assert.Equal(new Refusal(Codes.InvalidName), ChannelSettingsRule.CheckNameAndDescription(rawName, "", out _, out _));

    [Theory]
    [InlineData("bidi ‮")]
    [InlineData(null)] // stands for one character over the maximum
    public void CheckNameAndDescription_BadDescription_Refused(string? rawDescription) =>
        Assert.Equal(new Refusal(Codes.InvalidValue, "Description too long or with forbidden characters."),
            ChannelSettingsRule.CheckNameAndDescription("Raid", rawDescription ?? new string('x', ProtocolInfo.MaxTextLength + 1), out _, out _));

    [Theory]
    [InlineData(0, false, null)]
    [InlineData(ProtocolInfo.MaxChannelUsers, false, null)]
    [InlineData(ProtocolInfo.MaxChannelUsers + 1, false, "Max users: 0 (unlimited) to 999.")]
    [InlineData(-1, false, "Max users: 0 (unlimited) to 999.")]
    [InlineData(0, true, null)]
    [InlineData(1, true, "The default channel cannot be limited.")]
    [InlineData(-1, true, "The default channel cannot be limited.")]
    public void CheckLimit_UsersAndDefault_RangeAndDefaultUnlimited(int maxUsers, bool isDefault, string? detail) =>
        Assert.Equal(detail is null ? null : new Refusal(Codes.InvalidValue, detail), ChannelSettingsRule.CheckLimit(maxUsers, isDefault));

    [Theory]
    [InlineData(ProtocolInfo.MinBitrate, true)]
    [InlineData(ProtocolInfo.MaxBitrate, true)]
    [InlineData(ProtocolInfo.MinBitrate - 1, false)]
    [InlineData(ProtocolInfo.MaxBitrate + 1, false)]
    public void CheckBitrate_Kbps_OnlyInRange(int bitrate, bool allowed) =>
        Assert.Equal(allowed ? null : new Refusal(Codes.InvalidValue, "Bitrate: 16 to 128 kbps."), ChannelSettingsRule.CheckBitrate(bitrate));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CheckGroupLock_NullOrEmpty_NoLockAlsoOnTheDefault(bool empty)
    {
        Assert.Null(ChannelSettingsRule.CheckGroupLock(empty ? [] : null, isDefault: true, [], out var allowed));
        Assert.Null(allowed);
    }

    [Fact]
    public void CheckGroupLock_KnownGroupsTwice_AllowedOnce()
    {
        Assert.Null(ChannelSettingsRule.CheckGroupLock([Known, Known], isDefault: false, [Known], out var allowed));
        Assert.Equal([Known], allowed);
    }

    [Theory]
    [InlineData(true, true, "The default channel cannot be locked to groups.")]
    [InlineData(true, false, "The default channel cannot be locked to groups.")]
    [InlineData(false, false, "Unknown group in the group lock.")]
    public void CheckGroupLock_DefaultOrUnknownGroup_Refused(bool isDefault, bool known, string detail) =>
        Assert.Equal(new Refusal(Codes.InvalidValue, detail),
            ChannelSettingsRule.CheckGroupLock([known ? Known : Guid.NewGuid()], isDefault, [Known], out _));

    [Theory]
    [InlineData(null, true, true)]
    [InlineData("", true, true)]
    [InlineData("geheim", false, true)]
    [InlineData("geheim", true, false)]
    public void CheckPasswordAllowed_Default_NoPasswordOnTheDefaultChannel(string? password, bool isDefault, bool allowed) =>
        Assert.Equal(allowed ? null : new Refusal(Codes.InvalidValue, "The default channel cannot be locked with a password."),
            ChannelSettingsRule.CheckPasswordAllowed(password, isDefault));
}
