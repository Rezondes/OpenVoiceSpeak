using Microsoft.EntityFrameworkCore;
using OVS.Manager.Audit;
using OVS.Manager.Data;
using OVS.Manager.Docker;
using OVS.Manager.Exceptions;

namespace OVS.Manager.Servers;

/// <summary>
/// Package M8 (AC2, AC5): starts, stops and restarts a server's container, found by its label, writes the audit line
/// and has the watcher check again. While an action runs, the server's buttons are off.
/// </summary>
internal sealed class ServerLifecycle(ManagerDbContext database, IDockerEngine docker, StatusWatcher watcher, AuditLog audit)
{
    /// <summary>Starts the server; false when another action on it runs.</summary>
    /// <exception cref="PortInUseException">Its port is taken on the host.</exception>
    public Task<bool> StartAsync(string serverId, string actor, CancellationToken cancellation = default) =>
        RunAsync(serverId, actor, AuditActions.ServerStarted, docker.StartAsync, cancellation);

    /// <summary>Stops the server; false when another action on it runs.</summary>
    public Task<bool> StopAsync(string serverId, string actor, CancellationToken cancellation = default) =>
        RunAsync(serverId, actor, AuditActions.ServerStopped, docker.StopAsync, cancellation);

    /// <summary>Restarts the server; false when another action on it runs.</summary>
    public Task<bool> RestartAsync(string serverId, string actor, CancellationToken cancellation = default) =>
        RunAsync(serverId, actor, AuditActions.ServerRestarted, docker.RestartAsync, cancellation);

    /// <exception cref="ServerRequestException">The server or its container is gone, or it is in the trash (M9).</exception>
    /// <exception cref="DockerCallException">Docker did not do it.</exception>
    async Task<bool> RunAsync(string serverId, string actor, string action, Func<string, CancellationToken, Task> call, CancellationToken cancellation)
    {
        if (!watcher.TryBeginAction(serverId)) return false;
        try
        {
            var server = await database.Servers.AsNoTracking().SingleOrDefaultAsync(row => row.Id == serverId && row.DeletedAtUtc == null, cancellation)
                         ?? throw new ServerRequestException(ServerProblem.NotFound);
            var container = (await docker.ListAsync(cancellation)).FirstOrDefault(container => container.ServerId == serverId)
                            ?? throw new ServerRequestException(ServerProblem.NotFound);
            await call(container.Id, cancellation);
            await audit.WriteAsync(actor, action, server.Name, server.Id, cancellation: cancellation);
        }
        finally
        {
            watcher.EndAction(serverId);
        }
        await watcher.RefreshAsync(cancellation);
        return true;
    }
}
