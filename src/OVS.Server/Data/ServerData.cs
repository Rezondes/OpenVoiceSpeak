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

    // Package 70: statistics (A86). Files from before start at null or 0, without a new data version.
    public DateTimeOffset? LastLogin { get; set; }
    public int LoginCount { get; set; }
    /// <summary>A personal datum: only shown to those who may see the user list.</summary>
    public string? LastIp { get; set; }
    /// <summary>Newest first, at most MaxPreviousNicknames, without duplicates and without LastNickname.</summary>
    public List<string> PreviousNicknames { get; set; } = [];
    public TimeSpan OnlineTime { get; set; }
    /// <summary>Relayed voice only, 20 ms per packet.</summary>
    public TimeSpan SpeechTime { get; set; }
    public int ChatMessages { get; set; }
    /// <summary>Package 85: a server mute outlasts the session; applied on login before the Welcome.</summary>
    public bool ServerMuted { get; set; }

    public const int MaxPreviousNicknames = 5;

    /// <summary>Package 70: a login with this nickname; the old one moves to the front of the history.</summary>
    public void Login(string nickname, string ip, DateTimeOffset now)
    {
        if (LastNickname.Length > 0 && LastNickname != nickname)
        {
            PreviousNicknames.RemoveAll(n => n.Equals(LastNickname, StringComparison.OrdinalIgnoreCase));
            PreviousNicknames.Insert(0, LastNickname);
        }
        PreviousNicknames.RemoveAll(n => n.Equals(nickname, StringComparison.OrdinalIgnoreCase));
        if (PreviousNicknames.Count > MaxPreviousNicknames) PreviousNicknames.RemoveRange(MaxPreviousNicknames, PreviousNicknames.Count - MaxPreviousNicknames);
        LastNickname = nickname;
        LastLogin = now;
        LoginCount++;
        LastIp = ip;
    }
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

    // Package 80 (A97): details and history. Bans saved before start at null or 0, without a new data version.
    public DateTimeOffset? CreatedAt { get; set; }
    public string? CreatedByFingerprint { get; set; }
    /// <summary>The original duration; null = permanent (or unknown for an old ban with an end).</summary>
    public int? DurationMinutes { get; set; }
    public DateTimeOffset? LiftedAt { get; set; }
    /// <summary>Nickname of who lifted the ban.</summary>
    public string? LiftedBy { get; set; }
    public int BlockedAttempts { get; set; }
    public DateTimeOffset? LastAttempt { get; set; }
    public string? LastAttemptIp { get; set; }

    public bool IsActive(DateTimeOffset now) => LiftedAt is null && (ExpiresAt is null || ExpiresAt > now);

    /// <summary>When a lifted or expired ban ended; null while active.</summary>
    public DateTimeOffset? EndedAt(DateTimeOffset now) => IsActive(now) ? null : LiftedAt ?? ExpiresAt;
}

public sealed class ServerData
{
    /// <summary>
    /// 1 = before Package 31 (files without this field), 2 = chat rights, 3 = server settings from the environment (69),
    /// 4 = separate view, create and delete rights (76), 5 = the backup right (89). New servers start at the current version.
    /// </summary>
    public const int CurrentVersion = 5;
    public int DataVersion { get; set; } = 1;

    public ServerSettings Settings { get; set; } = new();
    public Guid DefaultChannelId { get; set; }
    public List<ChannelRecord> Channels { get; set; } = [];
    public List<ChannelLink> Links { get; set; } = [];
    public List<Group> Groups { get; set; } = [];
    public List<UserRecord> Users { get; set; } = [];
    public List<BanRecord> Bans { get; set; } = [];

    /// <summary>
    /// Package 87: removes users that have nothing but the Guest group, no ban (active or past) and were last seen
    /// before cutoff. The group history is not stored, so "only ever Guest" is read as "Guest now". A record without
    /// any date (older than Package 70) is kept: its age is unknown. Returns the count.
    /// </summary>
    public int PruneGuests(DateTimeOffset cutoff)
    {
        var banned = Bans.Select(b => b.Fingerprint).ToHashSet();
        return Users.RemoveAll(u => u.GroupIds.All(g => g == PermissionRules.GuestGroupId) && !banned.Contains(u.Fingerprint)
                                    && (u.LastLogin ?? u.FirstSeen) is var seen && seen != default && seen < cutoff);
    }

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
        if (DataVersion < 4) // Package 76 (A92): nobody loses a possibility; a stored "All" already reads as every new right
            for (int i = 0; i < Groups.Count; i++) Groups[i] = Groups[i] with { Permissions = WithViewRights(Groups[i].Permissions) };
        if (DataVersion < 5) // Package 89 (A101): backups were part of ServerConfig; a stored "All" already reads as the new right
            for (int i = 0; i < Groups.Count; i++)
                if (Groups[i].Permissions.Has(Permission.ServerConfig)) Groups[i] = Groups[i] with { Permissions = Groups[i].Permissions | Permission.BackupsManage };
        DataVersion = CurrentVersion;
        return true;
    }

    static Permission WithViewRights(Permission p)
    {
        if (p.Has(Permission.GroupsManage)) p |= Permission.GroupsView | Permission.GroupsCreate | Permission.GroupsDelete;
        if (p.Has(Permission.GroupsAssign)) p |= Permission.UsersView;
        if (p.Has(Permission.UserBan)) p |= Permission.BansView;
        return p;
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
