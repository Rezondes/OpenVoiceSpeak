using System.Globalization;
using System.Text.Json;
using OVS.Server.Exceptions;
using OVS.Server.Rules;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Framing;

namespace OVS.Server;

/// <summary>Startup settings. Precedence: environment variable, then server-config.json, then default.</summary>
/// <remarks>
/// Package 69: only Port and DataDir stay settings of the environment. MaxUsers, LogDays, LogRotateDaily and the automatic
/// restart are start values, taken into server-data.json once (like ServerName and Password) and then changed in the administration.
/// </remarks>
/// <param name="AutoRestartAt">Daily restart time in server local time, null when the automatic restart is off.</param>
/// <param name="LogRotateDaily">True begins new log files after midnight, false keeps them until the next start.</param>
/// <param name="Port">The TCP and UDP port.</param>
/// <param name="DataDir">The folder of the data, the logs, the backups and the certificate.</param>
/// <param name="MaxUsers">The start value of the user limit.</param>
/// <param name="ServerName">The start value of the server's name.</param>
/// <param name="Password">The start value of the server password, empty for none.</param>
/// <param name="LogDays">The start value of the log retention, 0 keeps every file.</param>
public sealed record ServerConfig(
    int Port, string DataDir, int MaxUsers, string ServerName, string Password, int LogDays = ProtocolDefaults.LogDays,
    TimeOnly? AutoRestartAt = null, bool LogRotateDaily = true)
{
    /// <summary>The config file's name inside the data folder.</summary>
    public const string FileName = "server-config.json";
    /// <summary>The restart time when none is set: 04:00.</summary>
    public static readonly TimeOnly DefaultRestartTime = new(4, 0, 0);

    /// <summary>Package 69: the restart time even while the restart is off, so it can be taken over as start value.</summary>
    public TimeOnly AutoRestartTime { get; init; } = AutoRestartAt ?? DefaultRestartTime;

    /// <summary>Package 69: names of the start values that were set in the environment or the file, for the hint at startup.</summary>
    public IReadOnlySet<string> GivenStartValues { get; init; } = new HashSet<string>();

    /// <summary>Reads the environment, then server-config.json, then the defaults; throws ConfigException for an invalid value.</summary>
    public static ServerConfig Load(Func<string, string?> getEnv)
    {
        var dataDir = Path.GetFullPath(getEnv("OVS_DATA_DIR") is { Length: > 0 } text ? text : "data");
        try
        {
            Directory.CreateDirectory(dataDir);
            // Fail now with a clear message instead of later while writing the certificate or the data file.
            var probe = Path.Combine(dataDir, ".write-test");
            File.WriteAllText(probe, "");
            File.Delete(probe);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            throw new ConfigException($"OVS_DATA_DIR '{dataDir}' cannot be created or written: {exception.Message}");
        }

        var file = ReadFile(Path.Combine(dataDir, FileName));

        int port = Int("OVS_PORT", getEnv("OVS_PORT"), file.Port, ProtocolDefaults.Port, new ValueRange(1, 65535));
        int maxUsers = Int("OVS_MAX_USERS", getEnv("OVS_MAX_USERS"), file.MaxUsers, ProtocolDefaults.MaxUsers, ServerLimitsRule.MaxUsers);
        int logDays = Int("OVS_LOG_DAYS", getEnv("OVS_LOG_DAYS"), file.LogDays, ProtocolDefaults.LogDays, ServerLimitsRule.LogDays); // 0 = keep forever
        bool autoRestart = Bool("OVS_AUTO_RESTART", getEnv("OVS_AUTO_RESTART"), file.AutoRestart, false);
        var restartTime = Time("OVS_AUTO_RESTART_TIME", getEnv("OVS_AUTO_RESTART_TIME") ?? file.AutoRestartTime, DefaultRestartTime);
        bool logRotateDaily = Bool("OVS_LOG_ROTATE_DAILY", getEnv("OVS_LOG_ROTATE_DAILY"), file.LogRotateDaily, true);
        string name = getEnv("OVS_SERVER_NAME") ?? file.ServerName ?? "OpenVoiceSpeak Server";
        string password = getEnv("OVS_PASSWORD") ?? file.Password ?? "";

        name = name.Trim();
        if (name.Length is < 1 or > ProtocolLimits.MaxNameLength)
            throw new ConfigException($"OVS_SERVER_NAME must be 1 to {ProtocolLimits.MaxNameLength} characters long.");

        var given = new (string Key, object? FileValue)[]
            {
                ("OVS_MAX_USERS", file.MaxUsers), ("OVS_LOG_DAYS", file.LogDays), ("OVS_LOG_ROTATE_DAILY", file.LogRotateDaily),
                ("OVS_AUTO_RESTART", file.AutoRestart), ("OVS_AUTO_RESTART_TIME", file.AutoRestartTime),
            }
            .Where(startValue => getEnv(startValue.Key) is not null || startValue.FileValue is not null).Select(startValue => startValue.Key).ToHashSet();

        return new ServerConfig(port, dataDir, maxUsers, name, password, logDays, autoRestart ? restartTime : null, logRotateDaily)
        {
            AutoRestartTime = restartTime,
            GivenStartValues = given,
        };
    }

    static FileValues ReadFile(string path)
    {
        var none = new FileValues(null, null, null, null, null, null, null, null);
        if (!File.Exists(path)) return none;
        try
        {
            return JsonSerializer.Deserialize<FileValues>(File.ReadAllBytes(path), ProtocolJson.Options) ?? none;
        }
        catch (JsonException exception)
        {
            throw new ConfigException($"{path} is not valid JSON: {exception.Message}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ConfigException($"{path} cannot be read: {exception.Message}");
        }
    }

    static bool Bool(string name, string? env, bool? fileValue, bool fallback)
    {
        if (env is null) return fileValue ?? fallback;
        return env.Trim().ToUpperInvariant() switch
        {
            "TRUE" or "1" or "ON" or "AN" => true,
            "FALSE" or "0" or "OFF" or "AUS" => false,
            _ => throw new ConfigException($"{name} must be true or false (also 1/0, an/aus), but is '{env}'."),
        };
    }

    static TimeOnly Time(string name, string? value, TimeOnly fallback)
    {
        if (value is null) return fallback;
        if (!TimeOnly.TryParseExact(value.Trim(), "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            throw new ConfigException($"{name} must be a time of day as hh:mm:ss (such as 04:00:00), but is '{value}'.");
        return time;
    }

    static int Int(string name, string? env, int? fileValue, int fallback, ValueRange range)
    {
        int value;
        if (env is not null)
        {
            if (!int.TryParse(env, out value))
                throw new ConfigException($"{name} is not a number: '{env}'.");
        }
        else
        {
            value = fileValue ?? fallback;
        }

        if (!range.Contains(value))
            throw new ConfigException($"{name} must be between {range.Min} and {range.Max}, but is {value}.");
        return value;
    }

    sealed record FileValues(
        int? Port, int? MaxUsers, string? ServerName, string? Password, int? LogDays,
        bool? AutoRestart, string? AutoRestartTime, bool? LogRotateDaily);
}
