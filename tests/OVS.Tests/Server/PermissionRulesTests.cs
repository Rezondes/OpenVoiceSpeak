using OVS.Server.Permissions;
using OVS.Shared.Permissions;
using static OVS.Server.Permissions.PermissionRules;
using P = OVS.Shared.Permissions.Permission;

namespace OVS.Tests.Server;

public class PermissionRulesTests
{
    static readonly List<Group> Groups = DefaultGroups();
    static Group Guest => Groups.Single(g => g.Name == "Gast");
    static Group Moderator => Groups.Single(g => g.Name == "Moderator");
    static Group Admin => Groups.Single(g => g.Name == "Admin");

    [Fact]
    public void Effective_Cases()
    {
        Assert.Equal(P.None, Effective([], Groups));
        Assert.Equal(GuestPermissions, Effective([Guest.Id], Groups));
        Assert.Equal(ModeratorPermissions, Effective([Guest.Id, Moderator.Id], Groups));
        Assert.Equal(GuestPermissions, Effective([Guest.Id, Guid.NewGuid()], Groups));
        Assert.Equal(P.All, Effective([Admin.Id], Groups));
        // Admin is always All, even if the stored group lost flags
        Assert.Equal(P.All, Effective([AdminGroupId], [new Group(AdminGroupId, "Admin", P.None)]));
    }

    [Fact]
    public void DefaultGroups_HaveDocumentedPermissions()
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
    public void CanSaveGroup_Cases(P actor, P? existing, P newPerms, bool expected) =>
        Assert.Equal(expected, CanSaveGroup(actor, existing is null ? null : Guid.NewGuid(), existing, newPerms));

    [Fact]
    public void AdminGroup_NotEditableNotDeletable()
    {
        Assert.False(CanSaveGroup(P.All, AdminGroupId, P.All, P.All));
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
        Assert.Equal((P)((1 << 24) - 1), P.All);
        // Package 81 (A98): the logs only for Admin
        Assert.Equal(P.None, (Moderator.Permissions | Guest.Permissions) & P.LogsView);
        Assert.True(Effective([AdminGroupId], Groups).Has(P.LogsView));
    }

    [Fact]
    public void CanAssign_Cases()
    {
        Assert.True(CanAssign(P.All, Admin));
        Assert.True(CanAssign(P.GroupsAssign | ModeratorPermissions, Moderator));
        Assert.False(CanAssign(P.GroupsAssign | P.Speak, Moderator));
        Assert.False(CanAssign(ModeratorPermissions, Guest)); // no GroupsAssign
        Assert.False(CanAssign(P.All & ~P.ServerConfig, Admin)); // Admin counts as All
    }

    [Fact]
    public void CanActOn_Cases()
    {
        Assert.True(ModeratorPermissions.CanActOn(P.Speak));
        Assert.False(ModeratorPermissions.CanActOn(P.All));
        Assert.True(P.All.CanActOn(P.All));
    }

    [Fact]
    public void WouldRemoveLastAdmin_Cases()
    {
        (string, IReadOnlyCollection<Guid>) a = ("a", [AdminGroupId]);
        (string, IReadOnlyCollection<Guid>) b = ("b", [AdminGroupId]);
        (string, IReadOnlyCollection<Guid>) c = ("c", [GuestGroupId]);

        Assert.True(WouldRemoveLastAdmin([a, c], "a", AdminGroupId));
        Assert.False(WouldRemoveLastAdmin([a, b], "a", AdminGroupId));
        Assert.False(WouldRemoveLastAdmin([a, c], "a", GuestGroupId));
        Assert.False(WouldRemoveLastAdmin([a, c], "c", AdminGroupId));
    }
}
