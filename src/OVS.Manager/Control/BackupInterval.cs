namespace OVS.Manager.Control;

/// <summary>Package M15 (AC2): how often the manager makes a scheduled backup of a server.</summary>
internal enum BackupInterval
{
    /// <summary>No scheduled backups.</summary>
    Off,
    /// <summary>Every day at the schedule's time.</summary>
    Daily,
    /// <summary>Every week on the schedule's day at its time.</summary>
    Weekly,
}
