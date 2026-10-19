namespace OVS.Manager.Notifications;

/// <summary>Package M27 (AC3): a server's thresholds for <see cref="NotificationEvent.Drops"/>.</summary>
internal sealed class ServerAlertSettings
{
    /// <summary>The defaults of a server without its own.</summary>
    public const double DefaultDropsPercent = 5;

    /// <summary>The defaults of a server without its own.</summary>
    public const int DefaultDropsMinutes = 5;

    /// <summary>The server's id.</summary>
    public string ServerId { get; set; } = "";

    /// <summary>Dropped voice packets in percent of those received, above which a minute counts.</summary>
    public double DropsPercent { get; set; } = DefaultDropsPercent;

    /// <summary>How many minutes in a row must be above it.</summary>
    public int DropsMinutes { get; set; } = DefaultDropsMinutes;
}
