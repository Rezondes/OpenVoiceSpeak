using Microsoft.EntityFrameworkCore;
using OVS.Manager.Audit;
using OVS.Manager.Data;
using OVS.Manager.Docker;
using OVS.Manager.Exceptions;

namespace OVS.Manager.Servers;

/// <summary>
/// Package M9 (MA9): the admin's trash. A deleted server is stopped at once and kept <see cref="KeepFor"/> with its
/// container, volume, port and assignments, so a restore brings back the same server; after that, or at once on a
/// second confirmation, its container, volume and rows are removed and its port is free again.
/// </summary>
internal sealed class ServerDeletion(ManagerDbContext database, IDockerEngine docker, StatusWatcher watcher, AuditLog audit, TimeProvider time)
{
    /// <summary>The user name of the daily sweep in the audit log.</summary>
    public const string SweepActor = "system";

    /// <summary>How long a deleted server can be restored.</summary>
    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(7);

    /// <summary>The servers in the trash, the oldest deletion first.</summary>
    public Task<List<ManagedServer>> ListDeletedAsync(CancellationToken cancellation = default) =>
        database.Servers.AsNoTracking().Where(server => server.DeletedAtUtc != null).OrderBy(server => server.DeletedAtUtc).ToListAsync(cancellation);

    /// <summary>AC1: stops the server and moves it to the trash when <paramref name="typedName"/> is exactly its name.</summary>
    /// <exception cref="ServerRequestException">The name differs or the server is gone.</exception>
    public async Task DeleteAsync(string serverId, string typedName, string actor, CancellationToken cancellation = default)
    {
        var server = await FindAsync(serverId, deleted: false, cancellation);
        if (typedName != server.Name) throw new ServerRequestException(ServerProblem.NameMismatch);
        if (await ContainerIdAsync(serverId, cancellation) is { } container) await docker.StopAsync(container, cancellation);
        server.DeletedAtUtc = time.GetUtcNow().UtcDateTime;
        await database.SaveChangesAsync(cancellation);
        await audit.WriteAsync(actor, AuditActions.ServerDeleted, server.Name, server.Id, cancellation: cancellation);
        await watcher.RefreshAsync(cancellation);
    }

    /// <summary>AC2: takes the server out of the trash and starts its container again.</summary>
    /// <exception cref="ServerRequestException">It is not in the trash.</exception>
    public async Task RestoreAsync(string serverId, string actor, CancellationToken cancellation = default)
    {
        var server = await FindAsync(serverId, deleted: true, cancellation);
        server.DeletedAtUtc = null;
        await database.SaveChangesAsync(cancellation);
        if (await ContainerIdAsync(serverId, cancellation) is { } container) await docker.StartAsync(container, cancellation);
        await audit.WriteAsync(actor, AuditActions.ServerRestored, server.Name, server.Id, cancellation: cancellation);
        await watcher.RefreshAsync(cancellation);
    }

    /// <summary>AC3: removes a server in the trash for good: container, volume, identity and row; its port is free.</summary>
    /// <exception cref="ServerRequestException">It is not in the trash.</exception>
    public async Task PurgeAsync(string serverId, string actor, CancellationToken cancellation = default)
    {
        var server = await FindAsync(serverId, deleted: true, cancellation);
        if (await ContainerIdAsync(serverId, cancellation) is { } container) await docker.RemoveAsync(container, cancellation);
        await docker.RemoveVolumeAsync(server.Id, cancellation);
        await database.ServerIdentities.Where(row => row.ServerId == server.Id).ExecuteDeleteAsync(cancellation);
        database.Servers.Remove(server);
        await database.SaveChangesAsync(cancellation);
        await audit.WriteAsync(actor, AuditActions.ServerPurged, server.Name, server.Id, cancellation: cancellation);
    }

    /// <summary>AC3: purges every server deleted more than <see cref="KeepFor"/> ago; returns how many.</summary>
    public async Task<int> SweepAsync(CancellationToken cancellation = default)
    {
        var before = (time.GetUtcNow() - KeepFor).UtcDateTime;
        var due = await database.Servers.AsNoTracking().Where(server => server.DeletedAtUtc != null && server.DeletedAtUtc <= before)
            .Select(server => server.Id).ToListAsync(cancellation);
        foreach (var serverId in due) await PurgeAsync(serverId, SweepActor, cancellation);
        return due.Count;
    }

    async Task<ManagedServer> FindAsync(string serverId, bool deleted, CancellationToken cancellation) =>
        await database.Servers.SingleOrDefaultAsync(server => server.Id == serverId && (server.DeletedAtUtc != null) == deleted, cancellation)
        ?? throw new ServerRequestException(ServerProblem.NotFound);

    /// <summary>The server's container, found by its label (M8 AC5); null when it is gone.</summary>
    async Task<string?> ContainerIdAsync(string serverId, CancellationToken cancellation) =>
        (await docker.ListAsync(cancellation)).FirstOrDefault(container => container.ServerId == serverId)?.Id;
}
