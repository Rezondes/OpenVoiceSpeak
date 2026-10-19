using Microsoft.EntityFrameworkCore;
using OVS.Manager.Audit;
using OVS.Manager.Data;
using OVS.Manager.Docker;
using OVS.Manager.Exceptions;

namespace OVS.Manager.Servers;

/// <summary>Package M25 (AC2): changes a server's CPU and memory limits; the container is made again with them, like an update without a new tag.</summary>
internal sealed class ServerResources(ManagerDbContext database, IDockerEngine docker, StatusWatcher watcher, AuditLog audit, ManagerOptions options)
{
    /// <summary>Sets <paramref name="limits"/> and recreates the container; false when another action on the server runs.</summary>
    /// <exception cref="ServerRequestException">The limits are out of range, or the server or its container is gone.</exception>
    /// <exception cref="DockerCallException">Docker did not do it.</exception>
    public async Task<bool> SetLimitsAsync(string serverId, ResourceLimits limits, string actor, CancellationToken cancellation = default)
    {
        if (!limits.IsValid) throw new ServerRequestException(ServerProblem.InvalidLimits);
        if (!watcher.TryBeginAction(serverId)) return false;
        try
        {
            var server = await database.Servers.SingleOrDefaultAsync(row => row.Id == serverId && row.DeletedAtUtc == null, cancellation)
                         ?? throw new ServerRequestException(ServerProblem.NotFound);
            var old = (await docker.ListAsync(cancellation)).FirstOrDefault(container => container.ServerId == serverId)
                      ?? throw new ServerRequestException(ServerProblem.NotFound);
            var inspected = await docker.InspectAsync(old.Id, cancellation);
            var environment = inspected.Environment ?? throw new ServerRequestException(ServerProblem.NotFound);
            bool running = inspected.State == "running";
            await docker.StopAsync(old.Id, cancellation);
            await docker.RemoveAsync(old.Id, cancellation);
            server.CpuCores = limits.CpuCores;
            server.MemoryMb = limits.MemoryMb;
            server.ContainerId = await docker.CreateAsync(new ContainerSpec(serverId, options.ServerImage, server.Version, server.Port, environment, ServerFactory.Network, limits), cancellation);
            await database.SaveChangesAsync(cancellation);
            if (running) await docker.StartAsync(server.ContainerId, cancellation);
            await audit.WriteAsync(actor, AuditActions.ServerLimitsChanged, server.Name, serverId, $"{limits.CpuCores?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"} cores, {limits.MemoryMb?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"} MB", cancellation);
        }
        finally
        {
            watcher.EndAction(serverId);
        }
        await watcher.RefreshAsync(cancellation);
        return true;
    }
}
