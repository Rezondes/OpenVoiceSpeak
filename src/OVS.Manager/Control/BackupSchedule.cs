namespace OVS.Manager.Control;

/// <summary>Package M15 (AC2): the backup schedule of one server, in the server's time zone.</summary>
internal sealed class BackupSchedule
{
    /// <summary>The server's id.</summary>
    public string ServerId { get; set; } = "";

    /// <summary>How often.</summary>
    public BackupInterval Interval { get; set; }

    /// <summary>The time of day in the server's time zone.</summary>
    public TimeOnly Time { get; set; } = new(3, 0);

    /// <summary>The day of a weekly backup.</summary>
    public DayOfWeek Day { get; set; } = DayOfWeek.Sunday;

    /// <summary>How many scheduled backups are kept; older scheduled ones are deleted.</summary>
    public int Keep { get; set; } = 7;

    /// <summary>When the schedule last made a backup, in UTC; null before the first.</summary>
    public DateTime? LastRunUtc { get; set; }

    /// <summary>The last time the schedule was due at or before <paramref name="nowUtc"/>, in UTC; null when it is off.</summary>
    public DateTime? LastDueUtc(DateTime nowUtc, TimeZoneInfo zone)
    {
        if (Interval == BackupInterval.Off) return null;
        var local = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone);
        var due = local.Date + Time.ToTimeSpan();
        if (Interval == BackupInterval.Weekly) due = due.AddDays(-(((int)due.DayOfWeek - (int)Day + 7) % 7));
        if (due > local) due = due.AddDays(Interval == BackupInterval.Weekly ? -7 : -1);
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(due, DateTimeKind.Unspecified), zone);
    }

    /// <summary>True when a due time passed since the last scheduled backup (or since <paramref name="sinceUtc"/>, the schedule's start).</summary>
    public bool IsDue(DateTime nowUtc, TimeZoneInfo zone, DateTime sinceUtc) =>
        LastDueUtc(nowUtc, zone) is { } due && due > (LastRunUtc ?? sinceUtc);
}
