using System.Globalization;
using Microsoft.EntityFrameworkCore;
using OVS.Manager.Audit;
using OVS.Manager.Data;
using OVS.Manager.Docker;
using OVS.Manager.Exceptions;
using OVS.Manager.Localization;
using OVS.Shared;

namespace OVS.Manager.Servers;

/// <summary>
/// Package M29 (D14, A202): takes over an OVS container the manager did not create. Its data is used as it is: the new
/// managed container mounts the old volume (no copy), gets the manager's variables and identity, and the old container is
/// removed only after the health check; on a failed check the new one goes and the old one starts again.
/// </summary>
internal sealed class ServerAdoption(ManagerDbContext database, IDockerEngine docker, ServerIdentityStore identities, ServerHealth health, StatusWatcher watcher,
    AuditLog audit, ManagerOptions options, TimeProvider time)
{
    /// <summary>AC1: the foreign OVS servers.</summary>
    public Task<IReadOnlyList<ForeignServer>> ListAsync(CancellationToken cancellation = default) => docker.ListForeignServersAsync(cancellation);

    /// <summary>AC2, AC4: adopts <paramref name="containerId"/> on <paramref name="port"/>; returns null, or why it did not (the old server runs again then).</summary>
    /// <exception cref="ServerRequestException">The container is gone or has no data, or the port cannot be had.</exception>
    /// <exception cref="DockerCallException">Docker did not do it.</exception>
    public async Task<string?> AdoptAsync(string containerId, int port, string actor, CancellationToken cancellation = default)
    {
        var foreign = (await docker.ListForeignServersAsync(cancellation)).FirstOrDefault(container => container.Id == containerId)
                      ?? throw new ServerRequestException(ServerProblem.NotFound);
        if (foreign.DataSource is null) throw new ServerRequestException(ServerProblem.NoDataVolume);
        var taken = await database.Servers.Select(server => server.Port).ToListAsync(cancellation);
        switch (PortPool.Check(port, taken))
        {
            case PortProblem.OutOfRange: throw new ServerRequestException(ServerProblem.PortOutOfRange);
            case PortProblem.Taken: throw new ServerRequestException(ServerProblem.PortTaken);
        }

        var server = await FromAsync(foreign, port, cancellation);
        var environment = ServerFactory.Environment(server, foreign.Environment.GetValueOrDefault("OVS_PASSWORD"));

        await docker.StopForeignAsync(foreign.Id, cancellation);
        server.ContainerId = await docker.CreateAsync(new ContainerSpec(server.Id, options.ServerImage, server.Version, port, environment, ServerFactory.Network, DataSource: foreign.DataSource), cancellation);
        database.Servers.Add(server);
        await database.SaveChangesAsync(cancellation);
        await docker.StartAsync(server.ContainerId, cancellation);
        if (await health.WaitAsync(server.Id, server.Version, port, cancellation))
        {
            await docker.RemoveForeignAsync(foreign.Id, cancellation);
            await audit.WriteAsync(actor, AuditActions.ServerAdopted, server.Name, server.Id, $"{Short(foreign.Id)} -> {Short(server.ContainerId)}", cancellation);
            await watcher.RefreshAsync(cancellation);
            return null;
        }

        await UndoAsync(server, server.ContainerId, foreign, cancellation);
        await audit.WriteAsync(actor, AuditActions.ServerAdoptFailed, server.Name, detail: $"{Short(foreign.Id)}: {Strings.Update_HealthFailed}", cancellation: cancellation);
        return TextFormat.Of(Strings.Adopt_Failed, Strings.Update_HealthFailed);
    }

    /// <summary>The managed server for <paramref name="foreign"/>: its name, user limit and time zone from the old variables, its version from the image tag, a new identity.</summary>
    async Task<ManagedServer> FromAsync(ForeignServer foreign, int port, CancellationToken cancellation)
    {
        var variables = foreign.Environment;
        var server = new ManagedServer
        {
            Id = await NewIdAsync(cancellation), Name = variables.GetValueOrDefault("OVS_SERVER_NAME") is { Length: > 0 } name ? name : foreign.Name, Port = port,
            TimeZone = variables.GetValueOrDefault("TZ") is { Length: > 0 } zone && TimeZoneInfo.TryFindSystemTimeZoneById(zone, out _) ? zone : options.TimeZone,
            MaxUsers = int.TryParse(variables.GetValueOrDefault("OVS_MAX_USERS"), CultureInfo.InvariantCulture, out var max) && max is > 0 and <= ServerFactory.MaxUsersLimit ? max : 50,
            Version = foreign.Tag == "latest" ? BuildInfo.Current.Version : foreign.Tag, DataSource = foreign.DataSource, CreatedAtUtc = time.GetUtcNow().UtcDateTime,
        };
        using (var identity = identities.Create(server.Id)) server.AdminFingerprint = identity.Fingerprint;
        return server;
    }

    /// <summary>AC2: after a failed check the new container, its identity and row go, and the old container starts again.</summary>
    async Task UndoAsync(ManagedServer server, string containerId, ForeignServer foreign, CancellationToken cancellation)
    {
        await docker.StopAsync(containerId, cancellation);
        await docker.RemoveAsync(containerId, cancellation);
        await database.ServerIdentities.Where(row => row.ServerId == server.Id).ExecuteDeleteAsync(cancellation);
        database.Servers.Remove(server);
        await database.SaveChangesAsync(cancellation);
        await docker.StartForeignAsync(foreign.Id, cancellation);
    }

    static string Short(string containerId) => containerId.Length > 12 ? containerId[..12] : containerId;

    async Task<string> NewIdAsync(CancellationToken cancellation)
    {
        while (true)
        {
            var id = Guid.NewGuid().ToString("N")[..8];
            if (!await database.Servers.AnyAsync(server => server.Id == id, cancellation)) return id;
        }
    }
}
