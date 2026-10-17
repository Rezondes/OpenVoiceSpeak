using OVS.Shared.Permissions;
using P = OVS.Shared.Permissions.Permission;

namespace OVS.Tests.Shared.Permissions;

public class GroupRulesTests
{
    static readonly Guid Custom = Guid.NewGuid();
    const P Moderator = P.Speak | P.UserKick | P.UserBan;

    /// <summary>"serveradmin", "admin", "guest" or "custom" (a group of its own).</summary>
    static Guid Group(string name) => name switch
    {
        "serveradmin" => WellKnownGroups.ServerAdmin,
        "admin" => WellKnownGroups.Admin,
        "guest" => WellKnownGroups.Guest,
        _ => Custom,
    };

    [Theory]
    [InlineData("guest", true)]
    [InlineData("admin", true)]
    [InlineData("serveradmin", true)]
    [InlineData("custom", false)]
    public void IsProtected_Group_OnlyGuestAdminAndServerAdmin(string group, bool expected) =>
        Assert.Equal(expected, GroupRules.IsProtected(Group(group)));

    [Fact]
    public void CanSave_AdminGroup_Never() =>
        Assert.False(GroupRules.CanSave(P.All, WellKnownGroups.Admin, P.All, P.All));

    /// <summary>Package 181 (A190): not even the server admin may save, delete or assign the Server-Admin group.</summary>
    [Fact]
    public void ServerAdmin_IsProtected_NeverSavedDeletedOrAssigned()
    {
        const P serverAdmin = P.All | P.ServerAdminRank;
        Assert.True(GroupRules.IsProtected(WellKnownGroups.ServerAdmin));
        Assert.False(GroupRules.CanSave(serverAdmin, WellKnownGroups.ServerAdmin, P.All, P.All));
        Assert.False(GroupRules.CanDelete(serverAdmin, WellKnownGroups.ServerAdmin, P.All));
        Assert.False(GroupRules.CanAssign(serverAdmin, WellKnownGroups.ServerAdmin, P.All));
        Assert.True(GroupRules.CanAssign(serverAdmin, WellKnownGroups.Admin, P.All));
    }

    public static TheoryData<P, P?, P, bool> SaveCases => new()
    {
        // actor, existing (null = creating), new, expected
        { P.GroupsCreate | P.Speak, null, P.Speak, true },
        { P.GroupsManage | P.Speak, null, P.Speak, false },              // creating needs GroupsCreate
        { P.GroupsManage | P.Speak, P.Speak, P.None, true },
        { P.GroupsManage | P.Speak, P.Speak, P.Speak | P.UserBan, false }, // escalation
        { P.GroupsManage | P.Speak, P.Speak | P.UserBan, P.Speak, false }, // stronger group
    };

    [Theory]
    [MemberData(nameof(SaveCases))]
    public void CanSave_CreateOrEdit_NeedsRightAndNoEscalation(P actor, P? existing, P newPermissions, bool expected) =>
        Assert.Equal(expected, GroupRules.CanSave(actor, existing is null ? null : Custom, existing, newPermissions));

    [Theory]
    [InlineData(P.GroupsDelete | Moderator, "custom", Moderator, true)]
    [InlineData(P.All, "guest", P.Speak, false)]
    [InlineData(P.All, "admin", P.All, false)]
    [InlineData(P.GroupsDelete | P.Speak, "custom", Moderator, false)] // stronger than the actor
    [InlineData(P.All & ~P.GroupsDelete, "custom", Moderator, false)] // no GroupsDelete
    public void CanDelete_GroupAndActor_OnlyUnprotectedNotStrongerWithRight(P actor, string group, P groupPermissions, bool expected) =>
        Assert.Equal(expected, GroupRules.CanDelete(actor, Group(group), groupPermissions));

    [Theory]
    [InlineData(P.GroupsAssign | Moderator, "custom", Moderator, true)]
    [InlineData(Moderator, "custom", Moderator, false)] // no GroupsAssign
    [InlineData(P.GroupsAssign | P.Speak, "custom", Moderator, false)] // stronger than the actor
    [InlineData(P.All, "admin", P.None, true)]
    [InlineData(P.All & ~P.ServerConfig, "admin", P.None, false)] // the admin group counts as All, stored flags do not count
    public void CanAssign_GroupAndActor_NeedsRightAdminGroupCountsAsAll(P actor, string group, P groupPermissions, bool expected) =>
        Assert.Equal(expected, GroupRules.CanAssign(actor, Group(group), groupPermissions));

    [Theory]
    [InlineData("admin", 1, true)]
    [InlineData("admin", 2, false)]
    [InlineData("guest", 1, false)] // the one admin is someone else
    [InlineData("", 0, false)]
    public void IsLastAdmin_GroupsAndAdminCount_OnlyTheOneAdmin(string groups, int adminCount, bool expected) =>
        Assert.Equal(expected, GroupRules.IsLastAdmin(groups.Length == 0 ? [] : [Group(groups)], adminCount));
}
