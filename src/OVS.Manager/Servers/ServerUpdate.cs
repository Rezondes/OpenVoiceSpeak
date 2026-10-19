using Microsoft.EntityFrameworkCore;
using OVS.Manager.Audit;
using OVS.Manager.Control;
using OVS.Manager.Data;
using OVS.Manager.Docker;
using OVS.Manager.Exceptions;
using OVS.Manager.Localization;

namespace OVS.Manager.Servers;

/// <summary>
/// Package M18 (AC1 to AC3, AC5): updates a server to a release of the manager's protocol: pull, backup, stop, the same
/// container with the new tag, start and a health check. A server's data only migrates forward, so a failed check brings
/// back the old image and the backup made before. Every step is reported and written to the audit log.
/// </summary>
internal sealed class ServerUpdate(ManagerDbContext database, IDockerEngine docker, ServerHealth health, BackupCommands backups, ServerConnections connections,
    StatusWatcher watcher, ReleaseCatalog catalog, AuditLog audit, ManagerOptions options, TimeProvider time, Notifications.Notifier notifier)
{
    /// <summary>The user name of the maintenance window in the audit log.</summary>
    public const string WindowActor = "system";

    /// <summary>AC1, AC2: updates the server to <paramref name="version"/>; returns null, or why it failed (after a rollback, the rollback's text).</summary>
    public async Task<string?> UpdateAsync(string serverId, string version, string actor, IProgress<UpdateStep>? progress = null, CancellationToken cancellation = default)
    {
        if (!watcher.TryBeginAction(serverId)) return Strings.Update_Busy;
        try
        {
            return await RunAsync(serverId, version, actor, progress, cancellation);
        }
        catch (DockerCallException)
        {
            return Strings.Docker_CallFailed;
        }
        finally
        {
            watcher.EndAction(serverId);
        }
    }

    /// <summary>AC3: updates every server with an offer, one after the other by port, and stops at the first failure.</summary>
    public async Task<UpdateAllResult> UpdateAllAsync(IReadOnlySet<string>? visible, string actor, IProgress<UpdateStep>? progress = null, CancellationToken cancellation = default)
    {
        var servers = await database.Servers.AsNoTracking().Where(server => server.DeletedAtUtc == null).OrderBy(server => server.Port).ToListAsync(cancellation);
        int updated = 0;
        foreach (var server in servers.Where(server => visible is null || visible.Contains(server.Id)))
        {
            if (catalog.UpdateFor(server.Version) is not { } release) continue;
            if (await UpdateAsync(server.Id, release.Version, actor, progress, cancellation) is { } failed)
                return new UpdateAllResult(updated, TextFormat.Of(Strings.Update_AllStopped, server.Name, failed));
            updated++;
        }
        return new UpdateAllResult(updated, null);
    }

    async Task<string?> RunAsync(string serverId, string version, string actor, IProgress<UpdateStep>? progress, CancellationToken cancellation)
    {
        var server = await database.Servers.SingleOrDefaultAsync(row => row.Id == serverId && row.DeletedAtUtc == null, cancellation);
        var old = (await docker.ListAsync(cancellation)).FirstOrDefault(container => container.ServerId == serverId);
        if (server is null || old is null) return Strings.Servers_NotFound;
        if ((await docker.InspectAsync(old.Id, cancellation)).Environment is not { } environment) return Strings.Update_NoEnvironment;
        var oldVersion = server.Version;
        async Task StepAsync(UpdateStep step)
        {
            progress?.Report(step);
            await audit.WriteAsync(actor, AuditActions.ServerUpdateStep, server.Name, serverId, $"{step} {oldVersion} -> {version}", cancellation);
        }

        await StepAsync(UpdateStep.Pull);
        await docker.PullImageAsync(options.ServerImage, version, cancellation);
        await StepAsync(UpdateStep.Backup);
        var backup = await backups.CreateBeforeUpdateAsync(serverId, version, actor, cancellation);
        if (backup.Error is not null) return TextFormat.Of(Strings.Update_NoBackup, backup.Error);
        await StepAsync(UpdateStep.Stop);
        await docker.StopAsync(old.Id, cancellation);
        await docker.RemoveAsync(old.Id, cancellation);
        await StepAsync(UpdateStep.Recreate);
        var created = await docker.CreateAsync(new ContainerSpec(serverId, options.ServerImage, version, server.Port, environment, ServerFactory.Network, server.Limits, server.DataSource), cancellation);
        await SaveContainerAsync(server, created, cancellation);
        await StepAsync(UpdateStep.Start);
        await docker.StartAsync(created, cancellation);
        await StepAsync(UpdateStep.Health);
        if (await health.WaitAsync(serverId, version, server.Port, cancellation))
        {
            server.Version = version;
            await database.SaveChangesAsync(cancellation);
            await StepAsync(UpdateStep.Done);
            await audit.WriteAsync(actor, AuditActions.ServerUpdated, server.Name, serverId, $"{oldVersion} -> {version}", cancellation);
            await notifier.ReportUpdateAsync(serverId, server.Name, failed: false, cancellation); // Package M27
            return null;
        }

        await StepAsync(UpdateStep.RollBack);
        await docker.StopAsync(created, cancellation);
        await docker.RemoveAsync(created, cancellation);
        var back = await docker.CreateAsync(new ContainerSpec(serverId, options.ServerImage, oldVersion, server.Port, environment, ServerFactory.Network, server.Limits, server.DataSource), cancellation);
        await SaveContainerAsync(server, back, cancellation);
        await docker.StartAsync(back, cancellation);
        var reason = Strings.Update_HealthFailed;
        var restored = await ConnectedAsync(serverId, cancellation) && await backups.RestoreAsync(serverId, backup.FileName!, actor, cancellation) is null;
        await audit.WriteAsync(actor, AuditActions.ServerUpdateFailed, server.Name, serverId, $"{oldVersion} -> {version}: {reason}", cancellation);
        await notifier.ReportUpdateAsync(serverId, server.Name, failed: true, cancellation); // Package M27
        return TextFormat.Of(restored ? Strings.Update_RolledBack : Strings.Update_RolledBackWithoutBackup, reason);
    }

    async Task SaveContainerAsync(ManagedServer server, string containerId, CancellationToken cancellation)
    {
        server.ContainerId = containerId;
        await database.SaveChangesAsync(cancellation);
    }

    /// <summary>AC2: waits for the service session to the old version, so its backup can be restored.</summary>
    async Task<bool> ConnectedAsync(string serverId, CancellationToken cancellation)
    {
        var deadline = time.GetUtcNow() + options.UpdateHealthTimeout;
        while (connections.LinkOf(serverId)?.Status != ServerLinkStatus.Connected)
        {
            if (time.GetUtcNow() >= deadline) return false;
            await watcher.RefreshAsync(cancellation);
            await Task.Delay(TimeSpan.FromSeconds(1), time, cancellation);
        }
        return true;
    }
}
