using System.Security.Claims;
using OVS.Manager.Accounts;

namespace OVS.Manager.Audit;

/// <summary>Package M5 (AC3): which lines a user may read: the admin all, a manager those of their assigned servers (M10).</summary>
internal static class AuditScope
{
    static readonly IReadOnlySet<string> NoServers = new HashSet<string>();

    /// <summary>The servers <paramref name="user"/> may read; null means every line.</summary>
    public static IReadOnlySet<string>? For(ClaimsPrincipal user) => user.IsInRole(ManagerRoles.Admin) ? null : NoServers; // ponytail: no assignments before M10
}
