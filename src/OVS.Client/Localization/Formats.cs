namespace OVS.Client.Localization;

/// <summary>The duration, size and clock texts the administration and the chat show, in the chosen language.</summary>
public static class Formats
{
    /// <summary>"45 s", "1 min 15 s", "1 h 30 min", "2 T 3 h".</summary>
    public static string Duration(TimeSpan t) => t switch
    {
        { TotalMinutes: < 1 } => $"{t.Seconds} s",
        { TotalHours: < 1 } => t.Seconds == 0 ? $"{t.Minutes} min" : $"{t.Minutes} min {t.Seconds} s",
        { TotalDays: < 1 } => t.Minutes == 0 ? $"{t.Hours} h" : $"{t.Hours} h {t.Minutes} min",
        _ => string.Format(Strings.Ui_DurationDays, (int)t.TotalDays, t.Hours),
    };

    /// <summary>A file size: whole KB (at least 1) below one MB, MB with one decimal from there.</summary>
    internal static string Size(long bytes) =>
        bytes >= 1024 * 1024 ? $"{bytes / (1024d * 1024):0.0} MB" : $"{Math.Max(1, (bytes + 1023) / 1024)} KB";

    /// <summary>The time of day as hours and minutes, as chat lines and notices show it.</summary>
    public static string ClockTime(DateTime time) => time.ToString("HH:mm");
}
