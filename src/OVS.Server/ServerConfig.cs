using System.Globalization;
using System.Text.Json;
using OVS.Shared.Protocol;

namespace OVS.Server;

public sealed class ConfigException(string message) : Exception(message);

/// <summary>Startup settings. Precedence: environment variable, then server-config.json, then default.</summary>
/// <remarks>
/// Package 69: only Port and DataDir stay settings of the environment. MaxUsers, LogDays, LogRotateDaily and the automatic
/// restart are start values, taken into server-data.json once (like ServerName and Password) and then changed in the administration.
/// </remarks>
/// <param name="AutoRestartAt">Daily restart time in server local time, null when the automatic restart is off.</param>
/// <param name="LogRotateDaily">True begins new log files after midnight, false keeps them until the next start.</param>
public sealed record ServerConfig(
    int Port, string DataDir, int MaxUsers, string ServerName, string Password, int LogDays = 30,
    TimeOnly? AutoRestartAt = null, bool LogRotateDaily = true)
{
    public const string FileName = "server-config.json";
    public static readonly TimeOnly DefaultRestartTime = new(4, 0, 0);

    /// <summary>Package 69: the restart time even while the restart is off, so it can be taken over as start value.</summary>
    public TimeOnly AutoRestartTime { get; init; } = AutoRestartAt ?? DefaultRestartTime;

    /// <summary>Package 69: names of the start values that were set in the environment or the file, for the hint at startup.</summary>
    public IReadOnlySet<string> GivenStartValues { get; init; } = new HashSet<string>();

    sealed record FileValues(
        int? Port, int? MaxUsers, string? ServerName, string? Password, int? LogDays,
        bool? AutoRestart, string? AutoRestartTime, bool? LogRotateDaily);

    public static ServerConfig Load(Func<string, string?> getEnv)
    {
        var dataDir = Path.GetFullPath(getEnv("OVS_DATA_DIR") is { Length: > 0 } d ? d : "data");
        try
        {
            Directory.CreateDirectory(dataDir);
            // Fail now with a clear message instead of later while writing the certificate or the data file.
            var probe = Path.Combine(dataDir, ".write-test");
            File.WriteAllText(probe, "");
            File.Delete(probe);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            throw new ConfigException($"OVS_DATA_DIR '{dataDir}' ist nicht anlegbar oder nicht beschreibbar: {e.Message}");
        }

        var file = ReadFile(Path.Combine(dataDir, FileName));

        int port = Int("OVS_PORT", getEnv("OVS_PORT"), file.Port, ProtocolInfo.DefaultPort, 1, 65535);
        int maxUsers = Int("OVS_MAX_USERS", getEnv("OVS_MAX_USERS"), file.MaxUsers, 50, 1, 100_000);
        int logDays = Int("OVS_LOG_DAYS", getEnv("OVS_LOG_DAYS"), file.LogDays, 30, 0, 3650); // 0 = keep forever
        bool autoRestart = Bool("OVS_AUTO_RESTART", getEnv("OVS_AUTO_RESTART"), file.AutoRestart, false);
        var restartTime = Time("OVS_AUTO_RESTART_TIME", getEnv("OVS_AUTO_RESTART_TIME") ?? file.AutoRestartTime, DefaultRestartTime);
        bool logRotateDaily = Bool("OVS_LOG_ROTATE_DAILY", getEnv("OVS_LOG_ROTATE_DAILY"), file.LogRotateDaily, true);
        string name = getEnv("OVS_SERVER_NAME") ?? file.ServerName ?? "OpenVoiceSpeak Server";
        string password = getEnv("OVS_PASSWORD") ?? file.Password ?? "";

        name = name.Trim();
        if (name.Length is < 1 or > 64)
            throw new ConfigException("OVS_SERVER_NAME muss 1 bis 64 Zeichen lang sein.");

        var given = new (string Key, object? FileValue)[]
            {
                ("OVS_MAX_USERS", file.MaxUsers), ("OVS_LOG_DAYS", file.LogDays), ("OVS_LOG_ROTATE_DAILY", file.LogRotateDaily),
                ("OVS_AUTO_RESTART", file.AutoRestart), ("OVS_AUTO_RESTART_TIME", file.AutoRestartTime),
            }
            .Where(v => getEnv(v.Key) is not null || v.FileValue is not null).Select(v => v.Key).ToHashSet();

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
        catch (JsonException e)
        {
            throw new ConfigException($"{path} ist kein gültiges JSON: {e.Message}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new ConfigException($"{path} kann nicht gelesen werden: {e.Message}");
        }
    }

    static bool Bool(string name, string? env, bool? fileValue, bool fallback)
    {
        if (env is null) return fileValue ?? fallback;
        return env.Trim().ToLowerInvariant() switch
        {
            "true" or "1" or "on" or "an" => true,
            "false" or "0" or "off" or "aus" => false,
            _ => throw new ConfigException($"{name} muss true oder false sein (auch 1/0, an/aus), ist aber '{env}'."),
        };
    }

    static TimeOnly Time(string name, string? value, TimeOnly fallback)
    {
        if (value is null) return fallback;
        if (!TimeOnly.TryParseExact(value.Trim(), "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            throw new ConfigException($"{name} muss eine Uhrzeit im Format hh:mm:ss sein (z. B. 04:00:00), ist aber '{value}'.");
        return time;
    }

    static int Int(string name, string? env, int? fileValue, int fallback, int min, int max)
    {
        int value;
        if (env is not null)
        {
            if (!int.TryParse(env, out value))
                throw new ConfigException($"{name} ist keine Zahl: '{env}'.");
        }
        else
        {
            value = fileValue ?? fallback;
        }

        if (value < min || value > max)
            throw new ConfigException($"{name} muss zwischen {min} und {max} liegen, ist aber {value}.");
        return value;
    }
}
