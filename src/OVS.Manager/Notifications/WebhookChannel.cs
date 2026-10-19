using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace OVS.Manager.Notifications;

/// <summary>Package M27: a notification as generic JSON: event, server, resolved, text and time.</summary>
internal sealed class WebhookChannel(IHttpClientFactory clients) : INotificationChannel
{
    /// <inheritdoc/>
    public NotificationChannelKind Kind => NotificationChannelKind.Webhook;

    /// <inheritdoc/>
    public async Task SendAsync(string target, Notification notification, CancellationToken cancellation = default)
    {
        using var client = clients.CreateClient(Notifier.HttpClientName);
        using var response = await client.PostAsJsonAsync(new Uri(target), new Payload(JsonNamingPolicyEvent(notification.Event), notification.Subject, notification.Resolved,
            notification.Text, notification.AtUtc), cancellation);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>"crashed", "restart_loop" and so on.</summary>
    static string JsonNamingPolicyEvent(NotificationEvent value) => System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());

    /// <summary>The JSON body.</summary>
    sealed record Payload([property: JsonPropertyName("event")] string Event, [property: JsonPropertyName("server")] string Server,
        [property: JsonPropertyName("resolved")] bool Resolved, [property: JsonPropertyName("text")] string Text, [property: JsonPropertyName("at")] DateTimeOffset At);
}
