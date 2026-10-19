namespace OVS.Manager.Notifications;

/// <summary>Package M27: where a notification goes.</summary>
internal enum NotificationChannelKind
{
    /// <summary>To the user's e-mail (M26).</summary>
    Email,
    /// <summary>A JSON POST to a URL.</summary>
    Webhook,
    /// <summary>A Discord-compatible webhook (also Fluxer).</summary>
    Discord,
}
