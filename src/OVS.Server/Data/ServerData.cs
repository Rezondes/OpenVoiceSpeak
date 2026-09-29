using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OVS.Server.Permissions;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;

namespace OVS.Server.Data;

public sealed class ServerSettings
{
    public string Name { get; set; } = "";
    public string WelcomeText { get; set; } = "";
    public string? PasswordHash { get; set; }
    // Package 69: formerly environment variables, now changed in the administration (A85)
    public int MaxUsers { get; set; } = 50;
    /// <summary>0 keeps every log file.</summary>
    public int LogDays { get; set; } = 30;
    public bool LogRotateDaily { get; set; } = true;
    public bool AutoRestart { get; set; }
    /// <summary>Server local time.</summary>
    public TimeOnly AutoRestartTime { get; set; } = ServerConfig.DefaultRestartTime;

    /// <summary>Package 69: takes the start values once, for a new server or on the update to data version 3.</summary>
    public void TakeStartValues(ServerConfig config)
    {
        MaxUsers = config.MaxUsers;
        LogDays = config.LogDays;
        LogRotateDaily = config.LogRotateDaily;
        AutoRestart = config.AutoRestartAt is not null;
        AutoRestartTime = config.AutoRestartAt ?? config.AutoRestartTime;
    }

    public ServerLimits Limits => new(MaxUsers, LogDays, LogRotateDaily, AutoRestart, AutoRestartTime);

    public static string? Hash(string password) =>
        password.Length == 0 ? null : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(password)));

    public bool CheckPassword(string? password)
    {
        if (PasswordHash is null) return true;
        var given = SHA256.HashData(Encoding.UTF8.GetBytes(password ?? ""));
        return CryptographicOperations.FixedTimeEquals(given, Convert.FromHexString(PasswordHash));
    }
}

public sealed class ChannelRecord
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int Order { get; set; }
    /// <summary>Package 34: nobody in this channel is heard.</summary>
    public bool IsMuted { get; set; }
    /// <summary>Package 35: 0 = unlimited. The default channel is always unlimited.</summary>
    public int MaxUsers { get; set; }
}

/// <summary>Undirected link, normalized so that A &lt; B.</summary>
public sealed record ChannelLink(Guid A, Guid B)
{
    public static ChannelLink Of(Guid x, Guid y) => x.CompareTo(y) < 0 ? new(x, y) : new(y, x);
    public bool Touches(Guid id) => A == id || B == id;
    public Guid Other(Guid id) => A == id ? B : A;
}

public sealed class UserRecord
{
    public string Fingerprint { get; set; } = "";
    public string LastNickname { get; set; } = "";
    public List<Guid> GroupIds { get; set; } = [];
    public DateTimeOffset FirstSeen { get; set; }
}

public sealed class BanRecord
{
    public Guid Id { get; set; }
    public string Fingerprint { get; set; } = "";
    public string Nickname { get; set; } = "";
    public string? Ip { get; set; }
    public string Reason { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset? ExpiresAt { get; set; }

    public bool IsActive(DateTimeOffset now) => ExpiresAt is null || ExpiresAt > now;
}

public sealed class ServerData
{
    /// <summary>
    /// 1 = before Package 31 (files without this field), 2 = chat rights, 3 = server settings from the environment (69).
    /// New servers start at the current version.
    /// </summary>
    public const int CurrentVersion = 3;
    public int DataVersion { get; set; } = 1;

    public ServerSettings Settings { get; set; } = new();
    public Guid DefaultChannelId { get; set; }
    public List<ChannelRecord> Channels { get; set; } = [];
    public List<ChannelLink> Links { get; set; } = [];
    public List<Group> Groups { get; set; } = [];
    public List<UserRecord> Users { get; set; } = [];
    public List<BanRecord> Bans { get; set; } = [];

    public static ServerData CreateDefault(ServerConfig config)
    {
        var lobby = new ChannelRecord { Id = Guid.NewGuid(), Name = "Lobby" };
        var settings = new ServerSettings { Name = config.ServerName, PasswordHash = ServerSettings.Hash(config.Password) };
        settings.TakeStartValues(config);
        return new ServerData
        {
            Settings = settings,
            DefaultChannelId = lobby.Id,
            Channels = [lobby],
            Groups = PermissionRules.DefaultGroups(),
            DataVersion = CurrentVersion,
        };
    }

    /// <summary>Brings an older file up to date. Returns true when something changed and must be saved.</summary>
    /// <param name="config">Package 69: the start values a version 2 file takes over once.</param>
    public bool Migrate(ServerConfig config)
    {
        if (DataVersion >= CurrentVersion) return false;
        int guest = Groups.FindIndex(g => g.Id == PermissionRules.GuestGroupId);
        if (DataVersion < 2 && guest >= 0) // A28: guests may chat, granted once
            Groups[guest] = Groups[guest] with { Permissions = Groups[guest].Permissions | Permission.ChatChannel | Permission.ChatPrivate };
        if (DataVersion < 3) Settings.TakeStartValues(config);
        DataVersion = CurrentVersion;
        return true;
    }
}

public sealed class DataStore(string path)
{
    public const string FileName = "server-data.json";

    static readonly JsonSerializerOptions Options = new(ProtocolJson.Options) { WriteIndented = true };

    public string Path => path;

    /// <summary>Loads the data file. A corrupt file aborts startup and is never overwritten.</summary>
    public ServerData LoadOrCreate(Func<ServerData> createDefault)
    {
        if (!File.Exists(path))
        {
            var data = createDefault();
            Save(data);
            return data;
        }
        try
        {
            return JsonSerializer.Deserialize<ServerData>(File.ReadAllBytes(path), Options)
                ?? throw new InvalidDataException($"{path} ist leer.");
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"{path} ist beschädigt und wurde nicht verändert: {e.Message}");
        }
    }

    public void Save(ServerData data)
    {
        var tmp = path + ".tmp";
        using (var stream = File.Create(tmp))
        {
            JsonSerializer.Serialize(stream, data, Options);
            stream.Flush(flushToDisk: true);
        }
        File.Move(tmp, path, overwrite: true);
    }
}
