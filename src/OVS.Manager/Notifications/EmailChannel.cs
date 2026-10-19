using OVS.Manager.Mail;

namespace OVS.Manager.Notifications;

/// <summary>Package M27: a notification as a mail (M26); the subject is its text.</summary>
internal sealed class EmailChannel(IMailSender mail) : INotificationChannel
{
    /// <inheritdoc/>
    public NotificationChannelKind Kind => NotificationChannelKind.Email;

    /// <inheritdoc/>
    public Task SendAsync(string target, Notification notification, CancellationToken cancellation = default) =>
        mail.SendAsync(new OutgoingMail(target, $"OpenVoiceSpeak: {notification.Text}", $"{notification.Text}\n\n{notification.AtUtc:u}"), cancellation);
}
