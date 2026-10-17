namespace OVS.Shared.Permissions;

/// <summary>Fixed ids of the three protected groups, known to server and client.</summary>
public static class WellKnownGroups
{
    /// <summary>The group every new user is in.</summary>
    public static readonly Guid Guest = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    /// <summary>The group that holds every right and may restore backups.</summary>
    public static readonly Guid Admin = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    /// <summary>Package 181 (A190): the one server admin's group, above Admin; only the server admin token gives it.</summary>
    public static readonly Guid ServerAdmin = Guid.Parse("00000000-0000-0000-0000-00000000000c");

    /// <summary>Package 181: a member of Admin or Server-Admin, who passes channel locks and may upload and restore backups.</summary>
    public static bool HoldsAdminGroup(IReadOnlyCollection<Guid> groupIds) => groupIds.Contains(Admin) || groupIds.Contains(ServerAdmin);
}
