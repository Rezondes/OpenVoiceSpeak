namespace OVS.Manager.Servers;

/// <summary>Package M18 (AC1, AC5): the steps of an update, shown live and in the audit log.</summary>
internal enum UpdateStep
{
    /// <summary>The new image is pulled.</summary>
    Pull,
    /// <summary>The backup before the update is made (M15).</summary>
    Backup,
    /// <summary>The users are told (M19).</summary>
    Announce,
    /// <summary>The old container is stopped and removed; its volume stays.</summary>
    Stop,
    /// <summary>The container is made again with the same variables, port, volume and labels and the new tag.</summary>
    Recreate,
    /// <summary>The new container starts.</summary>
    Start,
    /// <summary>The server must answer the handshake in time.</summary>
    Health,
    /// <summary>The update is done.</summary>
    Done,
    /// <summary>The health check failed: the old version and the backup come back (AC2).</summary>
    RollBack,
}
