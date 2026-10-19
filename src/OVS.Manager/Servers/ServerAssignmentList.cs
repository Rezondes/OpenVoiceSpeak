using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OVS.Manager.Accounts;
using OVS.Manager.Audit;
using OVS.Manager.Data;

namespace OVS.Manager.Servers;

/// <summary>Package M10 (AC1, AC4): the admin assigns servers to managers, from either side, and each change is in the audit log.</summary>
internal sealed class ServerAssignmentList(ManagerDbContext database, UserManager<ManagerUser> users, AuditLog audit)
{
    /// <summary>The server managers (not the admin) by name, with their ids.</summary>
    public async Task<IReadOnlyList<AssignmentChoice>> ManagersAsync()
    {
        var managers = await users.GetUsersInRoleAsync(ManagerRoles.Manager);
        return [.. managers.Select(user => new AssignmentChoice(user.Id, user.UserName ?? "")).OrderBy(user => user.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>The servers not in the trash by name, with their ids.</summary>
    public async Task<IReadOnlyList<AssignmentChoice>> ServersAsync(CancellationToken cancellation = default) =>
        [.. (await database.Servers.AsNoTracking().Where(server => server.DeletedAtUtc == null).Select(server => new { server.Id, server.Name }).ToListAsync(cancellation))
            .Select(server => new AssignmentChoice(server.Id, server.Name)).OrderBy(server => server.Name, StringComparer.OrdinalIgnoreCase)];

    /// <summary>The ids of the servers assigned to <paramref name="userId"/>.</summary>
    public async Task<IReadOnlySet<string>> ServersOfAsync(string userId, CancellationToken cancellation = default) =>
        (await database.ServerAssignments.Where(row => row.UserId == userId).Select(row => row.ServerId).ToListAsync(cancellation)).ToHashSet();

    /// <summary>The ids of the managers assigned to <paramref name="serverId"/>.</summary>
    public async Task<IReadOnlySet<string>> ManagersOfAsync(string serverId, CancellationToken cancellation = default) =>
        (await database.ServerAssignments.Where(row => row.ServerId == serverId).Select(row => row.UserId).ToListAsync(cancellation)).ToHashSet();

    /// <summary>Assigns or unassigns; nothing happens when it is so already. Only managers can be assigned (the admin sees all).</summary>
    public async Task SetAsync(string userId, string serverId, bool assigned, string actor, CancellationToken cancellation = default)
    {
        var user = await users.FindByIdAsync(userId);
        var server = await database.Servers.AsNoTracking().SingleOrDefaultAsync(row => row.Id == serverId, cancellation);
        if (user is null || server is null || !await users.IsInRoleAsync(user, ManagerRoles.Manager)) return;
        var row = await database.ServerAssignments.SingleOrDefaultAsync(row => row.UserId == userId && row.ServerId == serverId, cancellation);
        if ((row is not null) == assigned) return;
        if (assigned) database.ServerAssignments.Add(new ServerAssignment { UserId = userId, ServerId = serverId });
        else database.ServerAssignments.Remove(row!);
        await database.SaveChangesAsync(cancellation);
        await audit.WriteAsync(actor, assigned ? AuditActions.ServerAssigned : AuditActions.ServerUnassigned, user.UserName, serverId, server.Name, cancellation);
    }
}
