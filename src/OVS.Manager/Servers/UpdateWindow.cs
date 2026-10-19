namespace OVS.Manager.Servers;

/// <summary>
/// Package M18 (AC4, D14 8): a server's automatic updates in a weekly maintenance window of an hour, in the server's time
/// zone; off for every server until the admin turns it on.
/// </summary>
internal sealed class UpdateWindow
{
    /// <summary>How long the window stays open.</summary>
    public static readonly TimeSpan Length = TimeSpan.FromHours(1);

    /// <summary>The server's id.</summary>
    public string ServerId { get; set; } = "";

    /// <summary>Automatic updates are on.</summary>
    public bool AutoUpdate { get; set; }

    /// <summary>The window's day.</summary>
    public DayOfWeek Day { get; set; } = DayOfWeek.Monday;

    /// <summary>The window's start in the server's time zone.</summary>
    public TimeOnly Time { get; set; } = new(4, 0);

    /// <summary>When the window last ran an update, in UTC.</summary>
    public DateTime? LastRunUtc { get; set; }

    /// <summary>The start of the window open at <paramref name="nowUtc"/>, in UTC; null while it is closed or automatic updates are off.</summary>
    public DateTime? OpenSinceUtc(DateTime nowUtc, TimeZoneInfo zone)
    {
        if (!AutoUpdate) return null;
        var local = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone);
        var start = local.Date + Time.ToTimeSpan();
        if (local.DayOfWeek != Day || local < start || local >= start + Length) return null;
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(start, DateTimeKind.Unspecified), zone);
    }

    /// <summary>True when the window is open and has not run in it yet.</summary>
    public bool IsDue(DateTime nowUtc, TimeZoneInfo zone) => OpenSinceUtc(nowUtc, zone) is { } since && (LastRunUtc is null || LastRunUtc < since);
}
