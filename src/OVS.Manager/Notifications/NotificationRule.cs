namespace OVS.Manager.Notifications;

/// <summary>Package M27 (AC1, AC4): a user wants <see cref="Event"/> through <see cref="Channel"/>; the last failed send stays shown.</summary>
internal sealed class NotificationRule
{
    /// <summary>The row's id.</summary>
    public long Id { get; set; }

    /// <summary>The account.</summary>
    public string UserId { get; set; } = "";

    /// <summary>The event.</summary>
    public NotificationEvent Event { get; set; }

    /// <summary>The channel.</summary>
    public NotificationChannelKind Channel { get; set; }

    /// <summary>The webhook's URL; "" for e-mail, which goes to the account's address.</summary>
    public string Target { get; set; } = "";

    /// <summary>AC4: why the last send failed after its retries, null after a success.</summary>
    public string? LastError { get; set; }

    /// <summary>When it failed, in UTC.</summary>
    public DateTime? LastErrorAtUtc { get; set; }
}
