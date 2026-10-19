namespace OVS.Manager.Control;

/// <summary>Package M12: the settings of a server the manager changes; the AFK settings stay as they are.</summary>
internal sealed class SettingsInput
{
    /// <summary>The server's name.</summary>
    public string Name { get; set; } = "";

    /// <summary>The welcome text.</summary>
    public string WelcomeText { get; set; } = "";

    /// <summary>A new password, empty to keep the old one.</summary>
    public string NewPassword { get; set; } = "";

    /// <summary>True removes the password.</summary>
    public bool RemovePassword { get; set; }

    /// <summary>The user limit.</summary>
    public int MaxUsers { get; set; }

    /// <summary>Days of log files, 0 keeps all.</summary>
    public int LogDays { get; set; }

    /// <summary>A new log file every day.</summary>
    public bool LogRotateDaily { get; set; }

    /// <summary>A restart every day at <see cref="AutoRestartTime"/>.</summary>
    public bool AutoRestart { get; set; }

    /// <summary>The time of the daily restart in the server's time zone.</summary>
    public TimeOnly AutoRestartTime { get; set; }
}
