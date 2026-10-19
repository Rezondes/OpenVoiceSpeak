using OVS.Server;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server;

/// <summary>Package 192 (A204): the users of the current local day, counted in memory only.</summary>
public class DailyUsersTests
{
    static readonly TimeZoneInfo Plus2 = TimeZoneInfo.CreateCustomTimeZone("UTC+2", TimeSpan.FromHours(2), "UTC+2", "UTC+2");

    [Fact]
    public void Add_SameFingerprintTwice_CountsOnce()
    {
        var time = new ManualTimeProvider { Local = Plus2 };
        var users = new DailyUsers(time);
        users.Add("aa", time.GetUtcNow());
        users.Add("aa", time.GetUtcNow());
        users.Add("bb", time.GetUtcNow());
        Assert.Equal(2, users.Count);
    }

    [Fact]
    public void Add_FirstSeenToday_CountsAsNew()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero)) { Local = Plus2 };
        var users = new DailyUsers(time);
        users.Add("neu", time.GetUtcNow().AddHours(-1));
        users.Add("neu", time.GetUtcNow().AddHours(-1));
        users.Add("alt", time.GetUtcNow().AddDays(-3));
        users.Add("gestern", new DateTimeOffset(2026, 3, 9, 21, 59, 0, TimeSpan.Zero)); // 23:59 local, the day before
        Assert.Equal((3, 1), (users.Count, users.NewCount));
    }

    /// <summary>The day follows TZ like the automatic restart; at 22:00 UTC it is local midnight in UTC+2.</summary>
    [Fact]
    public void Midnight_LocalTime_StartsAtZero()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 3, 10, 21, 0, 0, TimeSpan.Zero)) { Local = Plus2 };
        var users = new DailyUsers(time);
        users.Add("aa", time.GetUtcNow());
        time.Advance(TimeSpan.FromMinutes(59));
        Assert.Equal((1, 1), (users.Count, users.NewCount));

        time.Advance(TimeSpan.FromMinutes(1)); // 00:00 local, still 10 March in UTC
        Assert.Equal((0, 0), (users.Count, users.NewCount));
        users.Add("aa", new DateTimeOffset(2026, 3, 10, 21, 0, 0, TimeSpan.Zero));
        Assert.Equal((1, 0), (users.Count, users.NewCount));
    }
}
