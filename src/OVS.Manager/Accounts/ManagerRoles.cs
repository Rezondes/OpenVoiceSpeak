namespace OVS.Manager.Accounts;

/// <summary>Package M3 (D4): the two roles; only the admin creates and deletes servers and accounts.</summary>
internal static class ManagerRoles
{
    /// <summary>The highest authority of the manager.</summary>
    public const string Admin = "Admin";

    /// <summary>A server manager, who operates the servers assigned to them.</summary>
    public const string Manager = "Manager";

    /// <summary>Both roles, created at the first start.</summary>
    public static readonly string[] All = [Admin, Manager];
}
