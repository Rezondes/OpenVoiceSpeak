using System.Globalization;
using OVS.Client.Localization;

namespace OVS.Tests.Client.Localization;

/// <summary>The duration, size and clock texts of the administration and the chat, in German and English, as they always read.</summary>
public class FormatsTests
{
    /// <summary>Runs the body with this culture for texts and numbers on the current thread.</summary>
    static string In(string culture, Func<string> body)
    {
        var (before, beforeUi) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            return body();
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = (before, beforeUi);
        }
    }

    [Fact]
    public void Duration_German_SameAsBefore()
    {
        (TimeSpan Time, string German, string English)[] table =
        [
            (TimeSpan.Zero, "0 s", "0 s"),
            (TimeSpan.FromSeconds(45), "45 s", "45 s"),
            (TimeSpan.FromSeconds(59.9), "59 s", "59 s"),
            (TimeSpan.FromMinutes(1), "1 min", "1 min"),
            (TimeSpan.FromSeconds(75), "1 min 15 s", "1 min 15 s"),
            (new TimeSpan(0, 59, 59), "59 min 59 s", "59 min 59 s"),
            (TimeSpan.FromHours(1), "1 h", "1 h"),
            (new TimeSpan(1, 30, 20), "1 h 30 min", "1 h 30 min"),
            (new TimeSpan(23, 59, 59), "23 h 59 min", "23 h 59 min"),
            (TimeSpan.FromDays(1), "1 T 0 h", "1 d 0 h"),
            (new TimeSpan(2, 3, 45, 0), "2 T 3 h", "2 d 3 h"),
            (new TimeSpan(400, 5, 0, 0), "400 T 5 h", "400 d 5 h"),
            (TimeSpan.FromDays(3650), "3650 T 0 h", "3650 d 0 h"),
        ];
        foreach (var (time, german, english) in table)
        {
            Assert.Equal(german, In("de-DE", () => Formats.Duration(time)));
            Assert.Equal(english, In("en-US", () => Formats.Duration(time)));
        }
    }

    [Fact]
    public void Size_German_SameAsBefore()
    {
        (long Bytes, string German, string English)[] table =
        [
            (0, "1 KB", "1 KB"),
            (1, "1 KB", "1 KB"),
            (1024, "1 KB", "1 KB"),
            (1025, "2 KB", "2 KB"),
            (1024 * 1024 - 1, "1024 KB", "1024 KB"),
            (1024 * 1024, "1,0 MB", "1.0 MB"),
            (1536 * 1024, "1,5 MB", "1.5 MB"),
            (123_456_789, "117,7 MB", "117.7 MB"),
            (5L * 1024 * 1024 * 1024, "5120,0 MB", "5120.0 MB"),
        ];
        foreach (var (bytes, german, english) in table)
        {
            Assert.Equal(german, In("de-DE", () => Formats.Size(bytes)));
            Assert.Equal(english, In("en-US", () => Formats.Size(bytes)));
        }
    }

    [Fact]
    public void ClockTime_German_SameAsBefore()
    {
        (DateTime Time, string Text)[] table =
        [
            (new DateTime(2026, 10, 5, 0, 0, 0), "00:00"),
            (new DateTime(2026, 10, 5, 0, 1, 59), "00:01"),
            (new DateTime(2026, 10, 5, 12, 30, 0), "12:30"),
            (new DateTime(2026, 10, 5, 23, 59, 59), "23:59"),
        ];
        foreach (var (time, text) in table)
            foreach (var culture in new[] { "de-DE", "en-US" })
                Assert.Equal(text, In(culture, () => Formats.ClockTime(time)));
    }
}
