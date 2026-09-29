using OVS.Shared.Permissions;

namespace OVS.Server.Permissions;

public sealed record Group(Guid Id, string Name, Permission Permissions);

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

    public static List<Group> DefaultGroups() =>
    [
        new(GuestGroupId, "Gast", GuestPermissions),
        new(Guid.NewGuid(), "Moderator", ModeratorPermissions),
        new(AdminGroupId, "Admin", Permission.All),
    ];

    public static bool IsProtected(Guid groupId) => groupId == GuestGroupId || groupId == AdminGroupId;

    public static Permission Effective(IEnumerable<Guid> groupIds, IEnumerable<Group> groups)
    {
        var ids = groupIds.ToHashSet();
        if (ids.Contains(AdminGroupId)) return Permission.All;
        return groups.Where(g => ids.Contains(g.Id)).Aggregate(Permission.None, (acc, g) => acc | g.Permissions);
    }

    /// <param name="existing">Current permissions when editing, null when creating.</param>
    public static bool CanSaveGroup(Permission actor, Guid? groupId, Permission? existing, Permission newPerms) =>
        actor.Has(existing is null ? Permission.GroupsCreate : Permission.GroupsManage) // Package 76
        && groupId != AdminGroupId
        && newPerms.IsSubsetOf(actor)
        && (existing is null || existing.Value.IsSubsetOf(actor));

    public static bool CanDeleteGroup(Permission actor, Group group) =>
        actor.Has(Permission.GroupsDelete) && !IsProtected(group.Id) && group.Permissions.IsSubsetOf(actor);

    public static bool CanAssign(Permission actor, Group group) =>
        actor.Has(Permission.GroupsAssign) && EffectiveOf(group).IsSubsetOf(actor);

    public static bool WouldRemoveLastAdmin(IEnumerable<(string Fingerprint, IReadOnlyCollection<Guid> GroupIds)> users,
        string fingerprint, Guid groupId)
    {
        if (groupId != AdminGroupId) return false;
        var admins = users.Where(u => u.GroupIds.Contains(AdminGroupId)).Select(u => u.Fingerprint).ToList();
        return admins.Count == 1 && admins[0] == fingerprint;
    }

    static Permission EffectiveOf(Group g) => g.Id == AdminGroupId ? Permission.All : g.Permissions;
}
