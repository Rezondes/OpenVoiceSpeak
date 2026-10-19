namespace OVS.Manager.Control;

/// <summary>Package M19 (AC1, AC2): an announced action that has not run yet.</summary>
/// <param name="Action">What runs.</param>
/// <param name="DueUtc">When it runs.</param>
internal sealed record PendingAnnouncement(AnnouncedAction Action, DateTimeOffset DueUtc);
