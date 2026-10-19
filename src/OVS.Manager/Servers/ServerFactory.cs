using Microsoft.EntityFrameworkCore;
using OVS.Manager.Audit;
using OVS.Manager.Data;
using OVS.Manager.Docker;
using OVS.Manager.Exceptions;
using OVS.Shared;
using OVS.Shared.Protocol;

namespace OVS.Manager.Servers;

/// <summary>
/// Package M7 (D2, D3, D6, MA4, MA6): makes a server: its own identity, which becomes its server admin through
/// <c>OVS_SERVER_ADMIN_FINGERPRINT</c> (PLAN 190), the container with the start values, the telemetry to the manager,
/// the port for TCP and UDP, the volume and the network; then it starts it and writes the audit line.
/// </summary>
internal sealed class ServerFactory(ManagerDbContext database, IDockerEngine docker, ServerIdentityStore identities, AuditLog audit, ManagerOptions options, TimeProvider time)
{
    /// <summary>The highest start value of the user limit.</summary>
    public const int MaxUsersLimit = 1000;

    /// <summary>The network of the manager and its servers (MA6).</summary>
    public const string Network = "ovs-managed";

    /// <summary>Creates and starts the server of <paramref name="request"/> for <paramref name="actor"/>.</summary>
    /// <exception cref="ServerRequestException">A value is invalid or the port cannot be had.</exception>
    public async Task<ManagedServer> CreateAsync(CreateServerRequest request, string actor, CancellationToken cancellation = default)
    {
        var name = TextRules.NormalizeName(request.Name, ProtocolLimits.MaxNameLength) ?? throw new ServerRequestException(ServerProblem.InvalidName);
        if (request.MaxUsers is < 1 or > MaxUsersLimit) throw new ServerRequestException(ServerProblem.InvalidMaxUsers);
        var zone = string.IsNullOrWhiteSpace(request.TimeZone) ? options.TimeZone : request.TimeZone.Trim();
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(zone, out _)) throw new ServerRequestException(ServerProblem.InvalidTimeZone);
        var limits = request.Limits ?? ResourceLimits.None;
        if (!limits.IsValid) throw new ServerRequestException(ServerProblem.InvalidLimits);
        var taken = await database.Servers.Select(server => server.Port).ToListAsync(cancellation);
        int port = request.Port ?? PortPool.FindFree(taken) ?? throw new ServerRequestException(ServerProblem.NoFreePort);
        switch (PortPool.Check(port, taken))
        {
            case PortProblem.OutOfRange: throw new ServerRequestException(ServerProblem.PortOutOfRange);
            case PortProblem.Taken: throw new ServerRequestException(ServerProblem.PortTaken);
        }

        var server = new ManagedServer
        {
            Id = await NewIdAsync(cancellation), Name = name, Port = port, TimeZone = zone, MaxUsers = request.MaxUsers,
            Version = string.IsNullOrWhiteSpace(request.Version) ? BuildInfo.Current.Version : request.Version.Trim(),
            CreatedAtUtc = time.GetUtcNow().UtcDateTime, CpuCores = limits.CpuCores, MemoryMb = limits.MemoryMb,
        };
        using (var identity = identities.Create(server.Id)) server.AdminFingerprint = identity.Fingerprint;
        server.ContainerId = await docker.CreateAsync(new ContainerSpec(server.Id, options.ServerImage, server.Version, port, Environment(server, request.Password), Network, limits), cancellation);
        database.Servers.Add(server);
        await database.SaveChangesAsync(cancellation);
        try
        {
            await docker.StartAsync(server.ContainerId, cancellation);
        }
        catch (PortInUseException exception)
        {
            await docker.RemoveAsync(server.ContainerId, cancellation);
            await docker.RemoveVolumeAsync(ManagedLabels.VolumeName(server.Id), cancellation);
            database.Servers.Remove(server);
            await database.ServerIdentities.Where(row => row.ServerId == server.Id).ExecuteDeleteAsync(cancellation);
            await database.SaveChangesAsync(cancellation);
            throw new ServerRequestException(ServerProblem.PortInUseOnHost, exception);
        }
        await audit.WriteAsync(actor, AuditActions.ServerCreated, server.Name, server.Id, $"port {port}, version {server.Version}", cancellation);
        return server;
    }

    /// <summary>AC3: exactly the start values, the time zone, the server admin and the telemetry to the manager (D6).</summary>
    /// <summary>The variables of <paramref name="server"/>'s container (M7); Package M29 gives them to an adopted server too.</summary>
    internal static Dictionary<string, string> Environment(ManagedServer server, string? password)
    {
        var port = server.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var variables = new Dictionary<string, string>
        {
            ["OVS_PORT"] = port,
            ["OVS_SERVER_NAME"] = server.Name,
            ["OVS_MAX_USERS"] = server.MaxUsers.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["TZ"] = server.TimeZone,
            ["OVS_SERVER_ADMIN_FINGERPRINT"] = server.AdminFingerprint,
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://ovs-manager:4318",
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
            ["OTEL_SERVICE_NAME"] = "ovs-server",
            ["OTEL_RESOURCE_ATTRIBUTES"] = $"service.instance.id={server.Id}",
        };
        if (!string.IsNullOrEmpty(password)) variables["OVS_PASSWORD"] = password;
        return variables;
    }

    /// <summary>A short id no other server has.</summary>
    async Task<string> NewIdAsync(CancellationToken cancellation)
    {
        while (true)
        {
            var id = Guid.NewGuid().ToString("N")[..8];
            if (!await database.Servers.AnyAsync(server => server.Id == id, cancellation)) return id;
        }
    }
}
