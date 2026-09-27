using System.Text.Json;
using OVS.Shared.Protocol;

namespace OVS.Server;

public sealed class ConfigException(string message) : Exception(message);

/// <summary>Startup settings. Precedence: environment variable, then server-config.json, then default.</summary>
public sealed record ServerConfig(int Port, string DataDir, int MaxUsers, string ServerName, string Password)
{
    public const string FileName = "server-config.json";

    sealed record FileValues(int? Port, int? MaxUsers, string? ServerName, string? Password);

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
        string name = getEnv("OVS_SERVER_NAME") ?? file.ServerName ?? "OpenVoiceSpeak Server";
        string password = getEnv("OVS_PASSWORD") ?? file.Password ?? "";

        name = name.Trim();
        if (name.Length is < 1 or > 64)
            throw new ConfigException("OVS_SERVER_NAME muss 1 bis 64 Zeichen lang sein.");

        return new ServerConfig(port, dataDir, maxUsers, name, password);
    }

    static FileValues ReadFile(string path)
    {
        if (!File.Exists(path)) return new FileValues(null, null, null, null);
        try
        {
            return JsonSerializer.Deserialize<FileValues>(File.ReadAllBytes(path), ProtocolJson.Options)
                ?? new FileValues(null, null, null, null);
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
