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

    /// <summary>Package M20: the telemetry database file in <see cref="DataDir"/>.</summary>
    public string TelemetryPath => Path.Combine(DataDir, "telemetry.db");

    /// <summary>Package M21 (MA7): days of minute values, <c>OVS_MANAGER_RETENTION_MINUTE_DAYS</c>, 7 by default.</summary>
    public int RetentionMinuteDays { get; init; } = 7;

    /// <summary>Package M21 (MA7): days of hourly values, <c>OVS_MANAGER_RETENTION_HOUR_DAYS</c>, 400 by default.</summary>
    public int RetentionHourDays { get; init; } = 400;

    /// <summary>Package M21 (MA7): days of log lines, <c>OVS_MANAGER_RETENTION_LOG_DAYS</c>, 30 by default.</summary>
    public int RetentionLogDays { get; init; } = 30;

    /// <summary>Package M21 (MA7): days of spans, <c>OVS_MANAGER_RETENTION_TRACE_DAYS</c>, 7 by default.</summary>
    public int RetentionTraceDays { get; init; } = 7;

    /// <summary>Package M21 (AC4): the telemetry database's limit, <c>OVS_MANAGER_TELEMETRY_MAX_MB</c>, 5000 MB by default.</summary>
    public long TelemetryMaxBytes { get; init; } = 5000L * 1024 * 1024;

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

    /// <summary>Package M18 (AC1): how long an updated server has to answer, <c>OVS_MANAGER_UPDATE_HEALTH_SECONDS</c>, 60 s by default.</summary>
    public TimeSpan UpdateHealthTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Package M14 (MA8): the host clients use, <c>OVS_MANAGER_PUBLIC_HOST</c>; without it the host of the request.</summary>
    public string? PublicHost { get; init; }

    /// <summary>Package M32 (AC2): the address users reach the manager at, <c>OVS_MANAGER_BASE_URL</c>; null takes the request's.</summary>
    public Uri? BaseUrl { get; init; }

    /// <summary>Package M32 (AC2): the proxies whose forwarded headers count, <c>OVS_MANAGER_TRUSTED_PROXIES</c>; none by default.</summary>
    public IReadOnlyList<System.Net.IPNetwork> TrustedProxies { get; init; } = [];

    /// <summary>Package M33 (AC4): the password of the scheduled manager backups, <c>OVS_MANAGER_BACKUP_PASSWORD</c>; null keeps the schedule off.</summary>
    public string? BackupPassword { get; init; }

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
            PublicHost = configuration["OVS_MANAGER_PUBLIC_HOST"] is { Length: > 0 } publicHost ? publicHost : null,
            UpdateHealthTimeout = int.TryParse(configuration["OVS_MANAGER_UPDATE_HEALTH_SECONDS"], System.Globalization.CultureInfo.InvariantCulture, out var seconds) && seconds > 0
                ? TimeSpan.FromSeconds(seconds) : TimeSpan.FromSeconds(60),
            RetentionMinuteDays = Positive(configuration["OVS_MANAGER_RETENTION_MINUTE_DAYS"], 7),
            RetentionHourDays = Positive(configuration["OVS_MANAGER_RETENTION_HOUR_DAYS"], 400),
            RetentionLogDays = Positive(configuration["OVS_MANAGER_RETENTION_LOG_DAYS"], 30),
            RetentionTraceDays = Positive(configuration["OVS_MANAGER_RETENTION_TRACE_DAYS"], 7),
            TelemetryMaxBytes = Positive(configuration["OVS_MANAGER_TELEMETRY_MAX_MB"], 5000) * 1024L * 1024,
            BaseUrl = ManagerProxy.ParseBaseUrl(configuration[ManagerProxy.BaseUrlVariable]),
            TrustedProxies = ManagerProxy.ParseTrustedProxies(configuration[ManagerProxy.TrustedProxiesVariable]),
            BackupPassword = configuration["OVS_MANAGER_BACKUP_PASSWORD"] switch
            {
                null or "" => null,
                { Length: >= Data.ManagerBackup.MinPasswordLength } password => password,
                _ => throw new InvalidOperationException($"OVS_MANAGER_BACKUP_PASSWORD must have at least {Data.ManagerBackup.MinPasswordLength} characters."),
            },
        };

    /// <summary>Package M21: a whole number above 0, else <paramref name="otherwise"/>.</summary>
    static int Positive(string? text, int otherwise) =>
        int.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var value) && value > 0 ? value : otherwise;
}
