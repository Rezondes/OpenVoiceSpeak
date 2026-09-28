using OVS.Shared;

namespace OVS.Tests.Shared;

/// <summary>Package 42: the version is a build name derived from one UTC instant, as in PersonalEinsatzPlanung.</summary>
public class BuildInfoTests
{
    static readonly DateTimeOffset Sample = new(2026, 9, 8, 7, 5, 3, TimeSpan.Zero); // 25503 s = 19*1296 + 24*36 + 15 = "0jof"

    [Fact]
    public void Format_CiBuild_DayMonthYearAndStamp() => Assert.Equal("080926.0jof", BuildInfo.Format(Sample, isCi: true));

    [Fact]
    public void Format_LocalBuild_Dev() => Assert.Equal("dev.0jof", BuildInfo.Format(Sample, isCi: false));

    [Fact]
    public void Stamp_MidnightAndLastSecond_UtcNotLocal()
    {
        Assert.Equal("0000", BuildInfo.Stamp(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        Assert.Equal("1unz", BuildInfo.Stamp(new DateTimeOffset(2026, 1, 1, 23, 59, 59, TimeSpan.Zero)));
        Assert.Equal("0000", BuildInfo.Stamp(new DateTimeOffset(2026, 1, 1, 2, 0, 0, TimeSpan.FromHours(2)))); // 00:00 UTC
    }

    [Fact]
    public void FromMetadata_CiValues()
    {
        var info = BuildInfo.FromMetadata(new Dictionary<string, string?>
        {
            [BuildInfo.TimeKey] = "2026-09-08T07:05:03Z",
            [BuildInfo.CommitKey] = "abcdef0123456789",
            [BuildInfo.CiKey] = "true",
        });
        Assert.Equal(("080926.0jof", "abcdef0"), (info.Version, info.ShortCommit));
    }

    [Fact]
    public void FromMetadata_MissingValues_DevWithNow()
    {
        var info = BuildInfo.FromMetadata(new Dictionary<string, string?>(), now: Sample);
        Assert.Equal(("dev.0jof", "", false), (info.Version, info.Commit, info.IsCi));
    }
}
