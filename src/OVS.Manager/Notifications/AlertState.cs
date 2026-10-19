namespace OVS.Manager.Notifications;

/// <summary>Package M27 (AC2): an event that was sent and has not been resolved; it is not sent again until then.</summary>
internal sealed class AlertState
{
    /// <summary>The server's id, or <see cref="Notifier.ManagerKey"/> for the manager's events.</summary>
    public string Key { get; set; } = "";

    /// <summary>The event.</summary>
    public NotificationEvent Event { get; set; }

    /// <summary>When it was sent, in UTC.</summary>
    public DateTime SentAtUtc { get; set; }
}
