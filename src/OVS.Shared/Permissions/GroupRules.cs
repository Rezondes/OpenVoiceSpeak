namespace OVS.Shared.Permissions;

/// <summary>Who may save, delete and assign a group, and the last admin; the server enforces them, the client offers the same.</summary>
public static class GroupRules
{
    /// <summary>Guest and Admin can be neither deleted nor replaced.</summary>
    public static bool IsProtected(Guid groupId) => groupId == WellKnownGroups.Guest || groupId == WellKnownGroups.Admin;

    /// <summary>Needs the create or manage right, never the admin group, and no escalation: the new and the current permissions must be within the actor's.</summary>
    /// <param name="groupId">Null when creating.</param>
    /// <param name="existing">Current permissions when editing, null when creating.</param>
    public static bool CanSave(Permission actor, Guid? groupId, Permission? existing, Permission newPermissions) =>
        actor.Has(existing is null ? Permission.GroupsCreate : Permission.GroupsManage) // Package 76
        && groupId != WellKnownGroups.Admin
        && newPermissions.IsSubsetOf(actor)
        && (existing is null || existing.Value.IsSubsetOf(actor));

    /// <summary>Needs the delete right, an unprotected group, and permissions within the actor's.</summary>
    public static bool CanDelete(Permission actor, Guid groupId, Permission groupPermissions) =>
        actor.Has(Permission.GroupsDelete) && !IsProtected(groupId) && groupPermissions.IsSubsetOf(actor);

    /// <summary>The admin group counts as All, whatever flags it has stored.</summary>
    public static bool CanAssign(Permission actor, Guid groupId, Permission groupPermissions) =>
        actor.Has(Permission.GroupsAssign)
        && (groupId == WellKnownGroups.Admin ? Permission.All : groupPermissions).IsSubsetOf(actor);

    /// <summary>The user is the only one in the admin group; removing, banning or deleting them would leave nobody.</summary>
    public static bool IsLastAdmin(IReadOnlyCollection<Guid> groupIds, int adminCount) =>
        adminCount == 1 && groupIds.Contains(WellKnownGroups.Admin);
}
