using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace OVS.Manager.Notifications;

/// <summary>Package M27: a notification as a Discord webhook message (Fluxer takes the same).</summary>
internal sealed class DiscordWebhookChannel(IHttpClientFactory clients) : INotificationChannel
{
    /// <inheritdoc/>
    public NotificationChannelKind Kind => NotificationChannelKind.Discord;

    /// <inheritdoc/>
    public async Task SendAsync(string target, Notification notification, CancellationToken cancellation = default)
    {
        using var client = clients.CreateClient(Notifier.HttpClientName);
        using var response = await client.PostAsJsonAsync(new Uri(target), new Message((notification.Resolved ? "\u2705 " : "\u26a0\ufe0f ") + notification.Text), cancellation);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Discord's body: the text as the message.</summary>
    sealed record Message([property: JsonPropertyName("content")] string Content);
}
