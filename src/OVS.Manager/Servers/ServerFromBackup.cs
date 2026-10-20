using Microsoft.EntityFrameworkCore;
using OVS.Manager.Audit;
using OVS.Manager.Control;
using OVS.Manager.Data;
using OVS.Manager.Exceptions;
using OVS.Manager.Localization;

namespace OVS.Manager.Servers;

/// <summary>
/// A new server from a server's backup, also one made before the manager. The backup's version is checked first; then the
/// server is made as any other (a dev manager takes the newest local server image), the backup is uploaded and restored on
/// it, and its certificate is pinned, so the server keeps its identity and clients know it again. As with an adoption the
/// manager's variable makes it server admin at the start, and the old server admin keeps their other groups (PLAN 190, A202).
/// </summary>
internal sealed class ServerFromBackup(ManagerDbContext database, ServerFactory factory, BackupCommands backups, ServerConnections connections, StatusWatcher watcher,
    AuditLog audit, ManagerOptions options, TimeProvider time)
{
    /// <summary>The names of the servers the manager runs with the certificate <paramref name="fingerprint"/>.</summary>
    public async Task<IReadOnlyList<string>> SameIdentityAsync(string fingerprint, CancellationToken cancellation = default) =>
        await database.Servers.AsNoTracking().Where(server => server.DeletedAtUtc == null && server.CertificateFingerprint == fingerprint)
            .OrderBy(server => server.Name).Select(server => server.Name).ToListAsync(cancellation);

    /// <summary>Makes the server <paramref name="name"/> on <paramref name="port"/> and restores <paramref name="backup"/> on it.</summary>
    /// <exception cref="ServerRequestException">The backup's version is unknown (nothing was made), or a value of the new server is invalid.</exception>
    public async Task<BackupCreation> CreateAsync(ServerBackupFile backup, string name, int port, string actor, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(backup);
        if (!backup.IsKnown) throw new ServerRequestException(ServerProblem.BackupVersionUnknown);
        var server = await factory.CreateAsync(new CreateServerRequest(name, Math.Clamp(backup.MaxUsers, 1, ServerFactory.MaxUsersLimit), null, null, null, port), actor, cancellation);
        return new BackupCreation(server.Id, await FillAsync(server.Id, backup, actor, cancellation));
    }

    /// <summary>Uploads <paramref name="backup"/> to the new server <paramref name="serverId"/> and restores it there; null, or why not.</summary>
    public async Task<string?> FillAsync(string serverId, ServerBackupFile backup, string actor, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(backup);
        if (!await ConnectedAsync(serverId, cancellation)) return Strings.Link_NotConnected;
        using var archive = new MemoryStream(backup.Archive);
        var uploaded = await backups.UploadAsync(serverId, archive, actor, cancellation);
        if (uploaded.Error is not null) return uploaded.Error;
        // the restored server shows the backup's certificate: pinned before the link connects again, else it would stop at "Zertifikat geändert"
        var pinned = await database.Servers.Where(row => row.Id == serverId).Select(row => row.CertificateFingerprint).SingleAsync(cancellation);
        await PinAsync(serverId, backup.Fingerprint, cancellation);
        if (await backups.RestoreAsync(serverId, uploaded.FileName!, actor, cancellation) is { } refused)
        {
            await PinAsync(serverId, pinned, cancellation);
            return refused;
        }
        var server = await database.Servers.AsNoTracking().SingleAsync(row => row.Id == serverId, cancellation);
        await audit.WriteAsync(actor, AuditActions.ServerCreatedFromBackup, server.Name, serverId,
            $"{backup.ServerVersion}, {backup.CreatedAt:O}, {backup.Fingerprint}", cancellation);
        return null;
    }

    async Task PinAsync(string serverId, string? fingerprint, CancellationToken cancellation) =>
        await database.Servers.Where(row => row.Id == serverId).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.CertificateFingerprint, fingerprint), cancellation);

    /// <summary>Waits up to the health timeout for the new server's service session.</summary>
    async Task<bool> ConnectedAsync(string serverId, CancellationToken cancellation)
    {
        var deadline = time.GetUtcNow() + options.UpdateHealthTimeout;
        await watcher.RefreshAsync(cancellation);
        while (connections.LinkOf(serverId)?.Status != ServerLinkStatus.Connected)
        {
            if (time.GetUtcNow() >= deadline) return false;
            await Task.Delay(TimeSpan.FromSeconds(1), time, cancellation);
        }
        return true;
    }
}
