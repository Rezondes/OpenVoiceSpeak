namespace OVS.Manager.Control;

/// <summary>Package M19 (AC1): what a server's chat is told about before it happens.</summary>
internal enum AnnouncedAction
{
    /// <summary>The server restarts.</summary>
    Restart,
    /// <summary>The server stops.</summary>
    Stop,
    /// <summary>The server is updated (M18).</summary>
    Update,
}
