using Microsoft.Extensions.Configuration;

namespace OVS.Manager;

/// <summary>Package M1: the manager's settings from the environment (MA1); the port is the image's, set in <c>Dockerfile.manager</c>.</summary>
/// <param name="DataDir">The folder of the database and the keys, <c>OVS_MANAGER_DATA_DIR</c>, <c>/data</c> by default.</param>
internal sealed record ManagerOptions(string DataDir)
{
    /// <summary>The environment variable of <see cref="DataDir"/>.</summary>
    public const string DataDirVariable = "OVS_MANAGER_DATA_DIR";

    /// <summary>Package M5: the environment variable of <see cref="AuditDays"/>.</summary>
    public const string AuditDaysVariable = "OVS_MANAGER_AUDIT_DAYS";

    /// <summary>Package M6: the environment variable of <see cref="DockerHost"/>.</summary>
    public const string DockerHostVariable = "OVS_MANAGER_DOCKER_HOST";

    /// <summary>The database file in <see cref="DataDir"/>.</summary>
    public string DatabasePath => Path.Combine(DataDir, "manager.db");

    /// <summary>Package M5 (AC4): how many days audit lines are kept; null keeps them forever.</summary>
    public int? AuditDays { get; init; }

    /// <summary>Package M6 (D8): the socket proxy, the manager's only way to Docker.</summary>
    public string DockerHost { get; init; } = "tcp://socket-proxy:2375";

    /// <summary>Package M7 (D14 4): the server image without its tag, <c>OVS_MANAGER_SERVER_IMAGE</c>.</summary>
    public string ServerImage { get; init; } = "ghcr.io/rezondes/openvoicespeak-server";

    /// <summary>Package M7 (D14 3): the manager's time zone (TZ), which new servers take; UTC without one.</summary>
    public string TimeZone { get; init; } = "UTC";

    /// <summary>
    /// Package M11 (MA6): the host of every server, <c>OVS_MANAGER_SERVER_HOST</c>; without it a server is reached as
    /// <c>ovs-&lt;id&gt;</c> in the network ovs-managed. A manager outside Docker (development) sets the Docker host here.
    /// </summary>
    public string? ServerHost { get; init; }

    /// <summary>Package M11: where the manager reaches the server <paramref name="serverId"/>.</summary>
    public string HostOf(string serverId) => ServerHost ?? Docker.ManagedLabels.ContainerName(serverId);

    /// <summary>The settings in <paramref name="configuration"/>, the data folder as a full path.</summary>
    /// <exception cref="InvalidOperationException">A value is set but invalid.</exception>
    public static ManagerOptions Read(IConfiguration configuration) =>
        new(Path.GetFullPath(configuration[DataDirVariable] is { Length: > 0 } folder ? folder : "/data"))
        {
            AuditDays = configuration[AuditDaysVariable] switch
            {
                null or "" => null,
                var text when int.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var days) && days > 0 => days,
                var text => throw new InvalidOperationException($"{AuditDaysVariable} must be a number of days above 0, but is '{text}'."),
            },
            DockerHost = configuration[DockerHostVariable] is { Length: > 0 } host ? host : "tcp://socket-proxy:2375",
            ServerImage = configuration["OVS_MANAGER_SERVER_IMAGE"] is { Length: > 0 } image ? image : "ghcr.io/rezondes/openvoicespeak-server",
            TimeZone = configuration["TZ"] is { Length: > 0 } zone ? zone : "UTC",
            ServerHost = configuration["OVS_MANAGER_SERVER_HOST"] is { Length: > 0 } serverHost ? serverHost : null,
        };
}
