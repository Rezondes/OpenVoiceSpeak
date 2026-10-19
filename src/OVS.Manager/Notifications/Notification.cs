namespace OVS.Manager.Notifications;

/// <summary>Package M27: what a channel sends.</summary>
/// <param name="Event">The event.</param>
/// <param name="Subject">The server's name, or the manager.</param>
/// <param name="Resolved">AC2: it is over ("wieder ok").</param>
/// <param name="Text">The text in the user's language.</param>
/// <param name="AtUtc">When it happened.</param>
internal sealed record Notification(NotificationEvent Event, string Subject, bool Resolved, string Text, DateTimeOffset AtUtc);
