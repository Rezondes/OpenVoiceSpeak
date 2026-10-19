namespace OVS.Manager.Control;

/// <summary>Package M15: why the manager made a backup; a backup without a mark was made by hand.</summary>
internal enum BackupKind
{
    /// <summary>By the schedule; only these are deleted beyond the schedule's number (AC2).</summary>
    Scheduled,
    /// <summary>Before an update (AC3, M18).</summary>
    BeforeUpdate,
}
