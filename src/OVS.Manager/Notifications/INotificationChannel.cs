namespace OVS.Manager.Notifications;

/// <summary>Package M27 (CODING-RULES rule 26): one way out for notifications; one class per channel.</summary>
internal interface INotificationChannel
{
    /// <summary>Which channel it is.</summary>
    NotificationChannelKind Kind { get; }

    /// <summary>Sends <paramref name="notification"/> to <paramref name="target"/> (an address or a URL).</summary>
    /// <exception cref="HttpRequestException">A webhook did not take it.</exception>
    /// <exception cref="Exceptions.MailSendException">The mail did not go out.</exception>
    Task SendAsync(string target, Notification notification, CancellationToken cancellation = default);
}
