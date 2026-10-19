using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OVS.Manager.Accounts;
using OVS.Manager.Data;

namespace OVS.Manager.Servers;

/// <summary>Package M10 (AC2): which servers a user sees and operates: the admin all, a manager the assigned ones.</summary>
internal sealed class ServerAccess(ManagerDbContext database)
{
    /// <summary>The servers <paramref name="user"/> may see; null means every server (the admin).</summary>
    public async Task<IReadOnlySet<string>?> VisibleAsync(ClaimsPrincipal user, CancellationToken cancellation = default)
    {
        if (user.IsInRole(ManagerRoles.Admin)) return null;
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return new HashSet<string>();
        return (await database.ServerAssignments.Where(row => row.UserId == userId).Select(row => row.ServerId).ToListAsync(cancellation)).ToHashSet();
    }

    /// <summary>True when <paramref name="user"/> may see and operate <paramref name="serverId"/>.</summary>
    public async Task<bool> MayOperateAsync(ClaimsPrincipal user, string serverId, CancellationToken cancellation = default) =>
        await VisibleAsync(user, cancellation) is not { } visible || visible.Contains(serverId);
}
