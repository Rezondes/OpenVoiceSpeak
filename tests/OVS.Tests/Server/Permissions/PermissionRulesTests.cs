using OVS.Server.Data.Records;
using OVS.Server.Permissions;
using OVS.Shared.Permissions;
using static OVS.Server.Permissions.PermissionRules;
using P = OVS.Shared.Permissions.Permission;

namespace OVS.Tests.Server.Permissions;

public class PermissionRulesTests
{
    static readonly List<GroupRecord> Groups = DefaultGroups();
    static GroupRecord Guest => Groups.Single(groupRecord => groupRecord.Name == "Gast");
    static GroupRecord Moderator => Groups.Single(groupRecord => groupRecord.Name == "Moderator");
    static GroupRecord Admin => Groups.Single(groupRecord => groupRecord.Name == "Admin");

    [Fact]
    public void Effective_GroupSets_UnionOfRightsAdminAlwaysAll()
    {
        Assert.Equal(P.None, Effective([], Groups));
        Assert.Equal(GuestPermissions, Effective([Guest.Id], Groups));
        Assert.Equal(ModeratorPermissions, Effective([Guest.Id, Moderator.Id], Groups));
        Assert.Equal(GuestPermissions, Effective([Guest.Id, Guid.NewGuid()], Groups));
        Assert.Equal(P.All, Effective([Admin.Id], Groups));
        // Admin is always All, even if the stored group lost flags
        Assert.Equal(P.All, Effective([AdminGroupId], [new GroupRecord(AdminGroupId, "Admin", P.None)]));
    }

    /// <summary>Package 181 (A190): the rank flag lies outside All and only Effective gives it, to members of Server-Admin.</summary>
    [Fact]
    public void Effective_ServerAdmin_AllPlusRank()
    {
        Assert.False(P.ServerAdminRank.IsSubsetOf(P.All));
        Assert.Equal(P.All | P.ServerAdminRank, Effective([ServerAdminGroupId], Groups));
        Assert.Equal(P.All | P.ServerAdminRank, Effective([ServerAdminGroupId, AdminGroupId, Guest.Id], Groups));
        Assert.Equal(P.All | P.ServerAdminRank, Effective([ServerAdminGroupId], [new GroupRecord(ServerAdminGroupId, "Server-Admin", P.None)]));
        // a stored group never carries the rank into Effective
        Assert.False(Effective([Guest.Id], [new GroupRecord(Guest.Id, "Gast", P.Speak | P.ServerAdminRank)]).Has(P.ServerAdminRank));
    }

    [Fact]
    public void DefaultGroups_Created_ContainServerAdmin()
    {
        var serverAdmin = Groups.Single(groupRecord => groupRecord.Id == ServerAdminGroupId);
        Assert.Equal("Server-Admin", serverAdmin.Name);
        Assert.Equal(P.All, serverAdmin.Permissions);
        Assert.Equal(WellKnownGroups.ServerAdmin, ServerAdminGroupId);
    }

    [Fact]
    public void CanActOn_ServerAdminOverAdmin_TrueAndNotBack()
    {
        Assert.True((P.All | P.ServerAdminRank).CanActOn(P.All));
        Assert.False(P.All.CanActOn(P.All | P.ServerAdminRank));
        Assert.True((P.All | P.ServerAdminRank).CanModerate("a", P.All, "b"));
        Assert.False(P.All.CanModerate("b", P.All | P.ServerAdminRank, "a"));
    }

    [Fact]
    public void DefaultGroups_Created_HaveDocumentedPermissions()
    {
        // Package 31 (A28): guests chat in the channel and privately, moderators also server-wide.
        Assert.Equal(P.Speak | P.ChatChannel | P.ChatPrivate, Guest.Permissions);
        Assert.Equal(P.Speak | P.SpeakLinked | P.ChannelLink | P.UserMove | P.UserMute | P.UserKick | P.UserBan |
            P.ChatServer | P.ChatChannel | P.ChatPrivate | P.BansView, Moderator.Permissions);
        Assert.Equal(P.All, Admin.Permissions);
        Assert.True(P.All.Has(P.ChannelJoinFull)); // Package 35: admins enter full channels
        Assert.Equal(GuestGroupId, Guest.Id);
        Assert.Equal(AdminGroupId, Admin.Id);
    }

    public static TheoryData<P, P?, P, bool> SaveCases => new()
    {
        // actor, existing, new, expected
        { P.All, null, P.Speak, true },
        { P.Speak, null, P.Speak, false },                                           // no GroupsManage
        { P.GroupsManage | P.Speak, null, P.Speak, false },                          // Package 76: creating needs GroupsCreate
        { P.GroupsCreate | P.Speak, null, P.Speak, true },
        { P.GroupsCreate | P.Speak, null, P.Speak | P.UserBan, false },              // escalation
        { P.GroupsCreate | P.UserBan, P.UserBan, P.UserBan, false },                 // editing needs GroupsManage
        { P.GroupsManage | P.UserBan, P.UserBan | P.ServerConfig, P.UserBan, false }, // editing a stronger group
        { P.GroupsManage | P.UserBan, P.UserBan, P.None, true },
    };

    [Theory]
    [MemberData(nameof(SaveCases))]
    public void CanSave_ActorAndGroupRights_OnlyWithinTheActorsOwn(P actor, P? existing, P newPerms, bool expected) =>
        Assert.Equal(expected, GroupRules.CanSave(actor, existing is null ? null : Guid.NewGuid(), existing, newPerms));

    [Fact]
    public void AdminGroup_EditOrDelete_Refused()
    {
        Assert.False(GroupRules.CanSave(P.All, AdminGroupId, P.All, P.All));
        Assert.False(CanDeleteGroup(P.All, Admin));
    }

    [Fact]
    public void GuestGroup_NotDeletable_OtherGroupDeletable()
    {
        Assert.False(CanDeleteGroup(P.All, Guest));
        Assert.True(CanDeleteGroup(P.All, Moderator));
        Assert.False(CanDeleteGroup(P.GroupsManage, Moderator)); // stronger than actor
        Assert.False(CanDeleteGroup(ModeratorPermissions, Moderator)); // no GroupsDelete
        Assert.False(CanDeleteGroup(P.All & ~P.GroupsDelete, Moderator)); // Package 76: GroupsManage alone is not enough
        Assert.True(CanDeleteGroup(ModeratorPermissions | P.GroupsDelete, Moderator));
    }

    /// <summary>Package 76 (A92): seeing is its own right; by default only Admin sees the user overview.</summary>
    [Fact]
    public void DefaultGroups_OnlyAdminSeesUsers_ModeratorSeesBans()
    {
        Assert.True(Moderator.Permissions.Has(P.BansView));
        Assert.False(Moderator.Permissions.HasFlag(P.UsersView));
        Assert.Equal(P.None, Moderator.Permissions & (P.UsersView | P.GroupsView | P.GroupsCreate | P.GroupsDelete | P.UserDelete));
        Assert.Equal(P.Speak | P.ChatChannel | P.ChatPrivate, Guest.Permissions);
        Assert.True(Effective([AdminGroupId], Groups).Has(P.UsersView | P.BansView | P.GroupsView | P.GroupsCreate | P.GroupsDelete | P.UserDelete));
        Assert.Equal((P)((1 << 27) - 1), P.All);
        // Packages 81 and 82 (A98, A99): the logs only for Admin
        Assert.Equal(P.None, (Moderator.Permissions | Guest.Permissions) & (P.LogsView | P.LogsDownload));
        Assert.True(Effective([AdminGroupId], Groups).Has(P.LogsView | P.LogsDownload));
        // Package 89 (A101): new servers give the backups only to Admin
        Assert.Equal(P.None, (Moderator.Permissions | Guest.Permissions) & P.BackupsManage);
        Assert.True(Effective([AdminGroupId], Groups).Has(P.BackupsManage));
        // Package 94: skipping channel passwords only for Admin
        Assert.Equal(P.None, (Moderator.Permissions | Guest.Permissions) & P.ChannelPasswordBypass);
        Assert.True(Effective([AdminGroupId], Groups).Has(P.ChannelPasswordBypass));
    }

    [Fact]
    public void CanAssign_ActorRights_NeedGroupsAssignAndTheGroupsRights()
    {
        Assert.True(CanAssign(P.All, Admin));
        Assert.True(CanAssign(P.GroupsAssign | ModeratorPermissions, Moderator));
        Assert.False(CanAssign(P.GroupsAssign | P.Speak, Moderator));
        Assert.False(CanAssign(ModeratorPermissions, Guest)); // no GroupsAssign
        Assert.False(CanAssign(P.All & ~P.ServerConfig, Admin)); // Admin counts as All
    }

    [Fact]
    public void CanActOn_ModeratorOnSpeakerOrAll_OnlyTheWeaker()
    {
        Assert.True(ModeratorPermissions.CanActOn(P.Speak));
        Assert.False(ModeratorPermissions.CanActOn(P.All));
    }

    [Fact]
    public void CanActOn_TargetRights_OnlyAStrictSubset()
    {
        // Package 84 (A102): equal rights, two admins included, cannot act on each other
        Assert.False(P.All.CanActOn(P.All));
        Assert.False(ModeratorPermissions.CanActOn(ModeratorPermissions));
        Assert.False(P.None.CanActOn(P.None));
        Assert.True(P.All.CanActOn(ModeratorPermissions));
        Assert.True(P.Speak.CanActOn(P.None));
        Assert.False(P.Speak.CanActOn(P.ChatServer)); // neither contains the other

        // never oneself, even with more rights than the target
        Assert.True(P.All.CanModerate("a", P.Speak, "b"));
        Assert.False(P.All.CanModerate("a", P.Speak, "a"));
        Assert.False(P.All.CanModerate("a", P.All, "b"));
    }

    [Fact]
    public void WouldRemoveLastAdmin_UsersAndGroup_OnlyForTheLastAdmin()
    {
        var user = new UserRecord { Fingerprint = "a", GroupIds = [AdminGroupId] };
        var userRecord = new UserRecord { Fingerprint = "b", GroupIds = [AdminGroupId] };
        var otherUser = new UserRecord { Fingerprint = "c", GroupIds = [GuestGroupId] };

        Assert.True(WouldRemoveLastAdmin([user, otherUser], "a", AdminGroupId));
        Assert.False(WouldRemoveLastAdmin([user, userRecord], "a", AdminGroupId));
        Assert.False(WouldRemoveLastAdmin([user, otherUser], "a", GuestGroupId));
        Assert.False(WouldRemoveLastAdmin([user, otherUser], "c", AdminGroupId));
    }
}
