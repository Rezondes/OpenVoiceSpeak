namespace OVS.Manager.Notifications;

/// <summary>Package M27: what a user can be told about.</summary>
internal enum NotificationEvent
{
    /// <summary>A server crashed.</summary>
    Crashed,
    /// <summary>A server restarts again and again.</summary>
    RestartLoop,
    /// <summary>A server has not answered for over 5 minutes.</summary>
    NotReachable,
    /// <summary>A server has as many users as it takes.</summary>
    Full,
    /// <summary>A server dropped more voice packets than its threshold for its minutes (AC3).</summary>
    Drops,
    /// <summary>A server can be updated (M17).</summary>
    UpdateAvailable,
    /// <summary>A server's update failed (M18).</summary>
    UpdateFailed,
    /// <summary>The manager's event: Docker's disk is below 10 % (M25).</summary>
    DiskLow,
    /// <summary>The manager's event: a new manager version is out (M17).</summary>
    ManagerUpdate,
}
