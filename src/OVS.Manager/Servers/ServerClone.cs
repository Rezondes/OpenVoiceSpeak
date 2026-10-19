using Microsoft.EntityFrameworkCore;
using OVS.Manager.Audit;
using OVS.Manager.Control;
using OVS.Manager.Data;
using OVS.Manager.Localization;

namespace OVS.Manager.Servers;

/// <summary>
/// Package M30: fills a new server with the contents of another. A fresh backup of each is downloaded through the service
/// sessions, merged (<see cref="CloneArchive"/>), uploaded to the clone and restored there, so the clone keeps its own
/// port, identity, certificate and name.
/// </summary>
internal sealed class ServerClone(ManagerDbContext database, BackupCommands backups, ServerConnections connections, StatusWatcher watcher,
    AuditLog audit, ManagerOptions options, TimeProvider time)
{
    /// <summary>AC1 to AC3: copies <paramref name="sourceId"/> into the new server <paramref name="cloneId"/>; returns null or why it did not.</summary>
    public async Task<string?> CopyAsync(string sourceId, string cloneId, bool keepUsers, bool keepBans, string actor, CancellationToken cancellation = default)
    {
        var source = await database.Servers.AsNoTracking().SingleAsync(server => server.Id == sourceId, cancellation);
        var clone = await database.Servers.AsNoTracking().SingleAsync(server => server.Id == cloneId, cancellation);
        if (!await ConnectedAsync(cloneId, cancellation)) return Strings.Link_NotConnected;
        using var sourceArchive = new MemoryStream();
        using var cloneArchive = new MemoryStream();
        if ((await FreshBackupAsync(sourceId, sourceArchive, actor, cancellation) ?? await FreshBackupAsync(cloneId, cloneArchive, actor, cancellation)) is { } failed)
            return failed;

        using var merged = new MemoryStream(CloneArchive.Merge(sourceArchive.ToArray(), cloneArchive.ToArray(), clone.Name, keepUsers, keepBans));
        var uploaded = await backups.UploadAsync(cloneId, merged, actor, cancellation);
        if (uploaded.Error is not null) return uploaded.Error;
        if (await backups.RestoreAsync(cloneId, uploaded.FileName!, actor, cancellation) is { } refused) return refused;
        await audit.WriteAsync(actor, AuditActions.ServerCloned, clone.Name, cloneId, $"{source.Name} ({sourceId}) -> {clone.Name} ({cloneId})", cancellation);
        return null;
    }

    /// <summary>Makes a backup of <paramref name="serverId"/> and downloads it into <paramref name="output"/>; null or why not.</summary>
    async Task<string?> FreshBackupAsync(string serverId, Stream output, string actor, CancellationToken cancellation)
    {
        var made = await backups.CreateAsync(serverId, actor, cancellation);
        return made.Error ?? await backups.DownloadAsync(serverId, made.FileName!, output, actor, cancellation);
    }

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
