namespace OVS.Shared.Permissions;

/// <summary>Who may save, delete and assign a group, and the last admin; the server enforces them, the client offers the same.</summary>
public static class GroupRules
{
    /// <summary>Guest, Admin and Server-Admin can be neither deleted nor replaced.</summary>
    public static bool IsProtected(Guid groupId) => groupId == WellKnownGroups.Guest || IsFixed(groupId);

    /// <summary>Package 181: Admin and Server-Admin always hold every right; their rights cannot be edited.</summary>
    public static bool IsFixed(Guid groupId) => groupId == WellKnownGroups.Admin || groupId == WellKnownGroups.ServerAdmin;

    /// <summary>Needs the create or manage right, never the admin or server admin group, and no escalation: the new and the current permissions must be within the actor's.</summary>
    /// <param name="groupId">Null when creating.</param>
    /// <param name="existing">Current permissions when editing, null when creating.</param>
    /// <param name="actor">The permissions of the one saving.</param>
    /// <param name="newPermissions">The permissions the group would get.</param>
    public static bool CanSave(Permission actor, Guid? groupId, Permission? existing, Permission newPermissions) =>
        actor.Has(existing is null ? Permission.GroupsCreate : Permission.GroupsManage) // Package 76
        && !(groupId is { } id && IsFixed(id))
        && newPermissions.IsSubsetOf(actor)
        && (existing is null || existing.Value.IsSubsetOf(actor));

    /// <summary>Needs the delete right, an unprotected group, and permissions within the actor's.</summary>
    public static bool CanDelete(Permission actor, Guid groupId, Permission groupPermissions) =>
        actor.Has(Permission.GroupsDelete) && !IsProtected(groupId) && groupPermissions.IsSubsetOf(actor);

    /// <summary>The admin group counts as All, whatever flags it has stored; Server-Admin only the token gives (Package 181).</summary>
    public static bool CanAssign(Permission actor, Guid groupId, Permission groupPermissions) =>
        groupId != WellKnownGroups.ServerAdmin
        && actor.Has(Permission.GroupsAssign)
        && (groupId == WellKnownGroups.Admin ? Permission.All : groupPermissions).IsSubsetOf(actor);

    /// <summary>The user is the only one in the admin group; removing, banning or deleting them would leave nobody.</summary>
    public static bool IsLastAdmin(IReadOnlyCollection<Guid> groupIds, int adminCount) =>
        adminCount == 1 && groupIds.Contains(WellKnownGroups.Admin);
}
