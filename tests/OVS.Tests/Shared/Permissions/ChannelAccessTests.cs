using OVS.Shared.Permissions;
using OVS.Shared.Protocol.Messages.Channels;
using P = OVS.Shared.Permissions.Permission;

namespace OVS.Tests.Shared.Permissions;

public class ChannelAccessTests
{
    static readonly Guid Team = Guid.NewGuid();

    /// <summary>"admin", "guest" and "team" separated by commas; "" is no group, null no list.</summary>
    static Guid[]? Groups(string? names) => names?.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(n => n switch
    {
        "admin" => WellKnownGroups.Admin,
        "guest" => WellKnownGroups.Guest,
        _ => Team,
    }).ToArray();

    [Theory]
    [InlineData(ChannelKind.Voice, true)]
    [InlineData(ChannelKind.Separator, false)]
    public void IsJoinable_Kind_OnlyVoiceJoinable(ChannelKind kind, bool expected) =>
        Assert.Equal(expected, ChannelAccess.IsJoinable(kind));

    [Theory]
    [InlineData(null, "guest", true)] // no lock
    [InlineData("team", "guest,team", true)]
    [InlineData("", "admin", true)] // an empty list: admins only
    [InlineData("team", "admin", true)]
    [InlineData("team", "guest", false)]
    [InlineData("", "guest", false)]
    [InlineData("team", "", false)]
    public void GroupsAllow_LockAndOwnGroups_OnlyNoLockMembersOrAdmins(string? allowed, string own, bool expected) =>
        Assert.Equal(expected, ChannelAccess.GroupsAllow(Groups(allowed), Groups(own)!));

    [Theory]
    [InlineData(false, "guest", P.Speak, false)]
    [InlineData(true, "guest", P.Speak, true)]
    [InlineData(true, "guest", P.ChannelPasswordBypass, false)]
    [InlineData(true, "admin", P.None, false)]
    public void NeedsPassword_Joining_RequiredUnlessAdminOrBypass(bool hasPassword, string groups, P actor, bool expected) =>
        Assert.Equal(expected, ChannelAccess.NeedsPassword(hasPassword, Groups(groups)!, actor, moving: false));

    [Theory]
    [InlineData(false, "guest", P.Speak, false)]
    [InlineData(true, "guest", P.All, true)] // no bypass for moving others
    [InlineData(true, "admin", P.None, false)]
    public void NeedsPassword_Moving_RequiredUnlessAdminBypassIgnored(bool hasPassword, string groups, P actor, bool expected) =>
        Assert.Equal(expected, ChannelAccess.NeedsPassword(hasPassword, Groups(groups)!, actor, moving: true));
}
