using System.Text.Json.Serialization;
using OVS.Server.Security;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages.ServerInfo;

namespace OVS.Server.Data.Records;

/// <summary>The server settings as stored.</summary>
internal sealed class ServerSettings
{
    /// <summary>The server's name.</summary>
    public string Name { get; set; } = "";
    /// <summary>Shown to everyone on connect.</summary>
    public string WelcomeText { get; set; } = "";
    /// <summary>The server password as stored (persisted under "passwordHash"); null = no password.</summary>
    [JsonPropertyName("passwordHash")]
    public StoredPassword? StoredPassword { get; set; }
    // Package 69: formerly environment variables, now changed in the administration (A85)
    /// <summary>The server's user limit (Package 69, A85: changed in the administration).</summary>
    public int MaxUsers { get; set; } = ProtocolDefaults.MaxUsers;
    /// <summary>0 keeps every log file.</summary>
    public int LogDays { get; set; } = ProtocolDefaults.LogDays;
    /// <summary>True starts new log files every day, false one per start.</summary>
    public bool LogRotateDaily { get; set; } = true;
    /// <summary>True restarts the server every day at AutoRestartTime.</summary>
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

    /// <summary>The editable limits as the protocol message carries them; a method, so it is not persisted.</summary>
    public ServerLimits ToServerLimits() => new(MaxUsers, LogDays, LogRotateDaily, AutoRestart, AutoRestartTime);

    /// <summary>True when the server password is still the unsalted SHA-256 hex from before Package 91; a method, so it is not persisted.</summary>
    public bool HasLegacyHash() => StoredPassword?.IsLegacy() ?? false;

    /// <summary>Constant-time in both formats; true when there is no password.</summary>
    public bool CheckPassword(string? password) => StoredPassword?.Matches(password) ?? true;
}
