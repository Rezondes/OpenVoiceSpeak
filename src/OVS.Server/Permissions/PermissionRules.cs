using OVS.Server.Data.Records;
using OVS.Shared.Permissions;

namespace OVS.Server.Permissions;

/// <summary>All server-wide permission rules. Pure functions, no I/O.</summary>
internal static class PermissionRules
{
    /// <summary>The group every new user is in.</summary>
    public static readonly Guid GuestGroupId = WellKnownGroups.Guest;
    /// <summary>The group that holds every right.</summary>
    public static readonly Guid AdminGroupId = WellKnownGroups.Admin;

    /// <summary>The rights of the default Moderator group.</summary>
    public const Permission ModeratorPermissions =
        Permission.Speak | Permission.SpeakLinked | Permission.ChannelLink |
        Permission.UserMove | Permission.UserMute | Permission.UserKick | Permission.UserBan |
        Permission.ChatServer | Permission.ChatChannel | Permission.ChatPrivate | Permission.BansView;

    /// <summary>What a guest may do on a new server, and what existing servers add once (A28).</summary>
    public const Permission GuestPermissions = Permission.Speak | Permission.ChatChannel | Permission.ChatPrivate;

    /// <summary>The groups of a new server: Gast, Moderator and Admin.</summary>
    public static List<GroupRecord> DefaultGroups() =>
    [
        new(GuestGroupId, "Gast", GuestPermissions),
        new(Guid.NewGuid(), "Moderator", ModeratorPermissions),
        new(AdminGroupId, "Admin", Permission.All),
    ];

    /// <summary>The union of the groups' rights; every right for a member of Admin.</summary>
    public static Permission Effective(IEnumerable<Guid> groupIds, IEnumerable<GroupRecord> groups)
    {
        var ids = groupIds.ToHashSet();
        if (ids.Contains(AdminGroupId)) return Permission.All;
        return groups.Where(groupRecord => ids.Contains(groupRecord.Id)).Aggregate(Permission.None, (acc, groupRecord) => acc | groupRecord.Permissions);
    }

    /// <summary>True when actor may delete group (<see cref="GroupRules.CanDelete"/>).</summary>
    public static bool CanDeleteGroup(Permission actor, GroupRecord group) => GroupRules.CanDelete(actor, group.Id, group.Permissions);

    /// <summary>True when actor may give and take group (<see cref="GroupRules.CanAssign"/>).</summary>
    public static bool CanAssign(Permission actor, GroupRecord group) => GroupRules.CanAssign(actor, group.Id, group.Permissions);

    /// <summary>True when taking groupId from fingerprint would leave Admin without members.</summary>
    public static bool WouldRemoveLastAdmin(IEnumerable<UserRecord> users,
        string fingerprint, Guid groupId)
    {
        if (groupId != AdminGroupId) return false;
        var all = users.ToList();
        var admins = all.Count(user => user.GroupIds.Contains(AdminGroupId));
        return all.Any(user => user.Fingerprint == fingerprint && GroupRules.IsLastAdmin(user.GroupIds, admins));
    }
}
