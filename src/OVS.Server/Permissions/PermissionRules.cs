using OVS.Server.Data.Records;
using OVS.Shared.Permissions;

namespace OVS.Server.Permissions;

/// <summary>All server-wide permission rules. Pure functions, no I/O.</summary>
public static class PermissionRules
{
    public static readonly Guid GuestGroupId = WellKnownGroups.Guest;
    public static readonly Guid AdminGroupId = WellKnownGroups.Admin;

    public const Permission ModeratorPermissions =
        Permission.Speak | Permission.SpeakLinked | Permission.ChannelLink |
        Permission.UserMove | Permission.UserMute | Permission.UserKick | Permission.UserBan |
        Permission.ChatServer | Permission.ChatChannel | Permission.ChatPrivate | Permission.BansView;

    /// <summary>What a guest may do on a new server, and what existing servers add once (A28).</summary>
    public const Permission GuestPermissions = Permission.Speak | Permission.ChatChannel | Permission.ChatPrivate;

    public static List<GroupRecord> DefaultGroups() =>
    [
        new(GuestGroupId, "Gast", GuestPermissions),
        new(Guid.NewGuid(), "Moderator", ModeratorPermissions),
        new(AdminGroupId, "Admin", Permission.All),
    ];

    public static Permission Effective(IEnumerable<Guid> groupIds, IEnumerable<GroupRecord> groups)
    {
        var ids = groupIds.ToHashSet();
        if (ids.Contains(AdminGroupId)) return Permission.All;
        return groups.Where(groupRecord => ids.Contains(groupRecord.Id)).Aggregate(Permission.None, (acc, groupRecord) => acc | groupRecord.Permissions);
    }

    public static bool CanDeleteGroup(Permission actor, GroupRecord group) => GroupRules.CanDelete(actor, group.Id, group.Permissions);

    public static bool CanAssign(Permission actor, GroupRecord group) => GroupRules.CanAssign(actor, group.Id, group.Permissions);

    public static bool WouldRemoveLastAdmin(IEnumerable<UserRecord> users,
        string fingerprint, Guid groupId)
    {
        if (groupId != AdminGroupId) return false;
        var all = users.ToList();
        var admins = all.Count(user => user.GroupIds.Contains(AdminGroupId));
        return all.Any(user => user.Fingerprint == fingerprint && GroupRules.IsLastAdmin(user.GroupIds, admins));
    }
}
