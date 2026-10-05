using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages.ServerInfo;

namespace OVS.Server.Data.Records;

public sealed class ServerSettings
{
    public string Name { get; set; } = "";
    public string WelcomeText { get; set; } = "";
    public string? PasswordHash { get; set; }
    // Package 69: formerly environment variables, now changed in the administration (A85)
    public int MaxUsers { get; set; } = ProtocolInfo.MaxUsersDefault;
    /// <summary>0 keeps every log file.</summary>
    public int LogDays { get; set; } = ProtocolInfo.LogDaysDefault;
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

    /// <summary>The unsalted SHA-256 hex from before Package 91.</summary>
    public bool HasLegacyHash => Security.PasswordHash.IsLegacy(PasswordHash);

    /// <summary>Constant-time in both formats; true when there is no password.</summary>
    public bool CheckPassword(string? password) => Security.PasswordHash.Verify(PasswordHash, password);
}
