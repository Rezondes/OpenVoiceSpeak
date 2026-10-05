namespace OVS.Shared.Permissions;

/// <summary>Fixed ids of the two protected groups, known to server and client.</summary>
public static class WellKnownGroups
{
    public static readonly Guid Guest = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    public static readonly Guid Admin = Guid.Parse("00000000-0000-0000-0000-00000000000b");
}
