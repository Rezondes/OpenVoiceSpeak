using System.Globalization;
using System.Reflection;

namespace OVS.Shared;

/// <summary>
/// Build identity (Package 42, as in PersonalEinsatzPlanung). Not semver on purpose: the name only says which build
/// this is. Directory.Build.props stamps raw data into every assembly (build time, commit, CI or not), the
/// formatting lives here where tests reach it.
/// </summary>
public sealed record BuildInfo(DateTimeOffset BuildTime, string Commit, bool IsCi)
{
    /// <summary>The assembly metadata keys Directory.Build.props stamps: build time, commit and whether CI built it.</summary>
    public const string TimeKey = "OvsBuildTime", CommitKey = "OvsCommit", CiKey = "OvsIsCI";

    /// <summary>The build of the running assembly.</summary>
    public static BuildInfo Current { get; } = FromMetadata(typeof(BuildInfo).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>().ToDictionary(assemblyMetadataAttribute => assemblyMetadataAttribute.Key, assemblyMetadataAttribute => assemblyMetadataAttribute.Value));

    /// <summary>"DDMMYY.stamp" for a CI build, "dev.stamp" for anything built locally.</summary>
    public string Version => Format(BuildTime, IsCi);
    /// <summary>The commit's first 7 characters, or the whole commit when it is shorter.</summary>
    public string ShortCommit => Commit.Length > 7 ? Commit[..7] : Commit;

    /// <summary>Seconds since UTC midnight in base36, always 4 characters: 00:00:00 = "0000", 23:59:59 = "1unz".</summary>
    public static string Stamp(DateTimeOffset time)
    {
        var utc = time.ToUniversalTime();
        int seconds = utc.Hour * 3600 + utc.Minute * 60 + utc.Second;
        const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
        var chars = new char[4];
        for (int index = 3; index >= 0; index--, seconds /= 36) chars[index] = digits[seconds % 36];
        return new string(chars);
    }

    /// <summary>"DDMMYY.stamp" for a CI build, "dev.stamp" otherwise.</summary>
    public static string Format(DateTimeOffset buildTime, bool isCi) =>
        isCi ? $"{buildTime.ToUniversalTime():ddMMyy}.{Stamp(buildTime)}" : $"dev.{Stamp(buildTime)}";

    /// <summary>Missing values: a local build of right now (local Debug builds leave the time out to stay incremental).</summary>
    public static BuildInfo FromMetadata(IReadOnlyDictionary<string, string?> metadata, DateTimeOffset? now = null)
    {
        var time = metadata.GetValueOrDefault(TimeKey) is { } raw
                   && DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : now ?? DateTimeOffset.UtcNow; // clock: real time, a local build is stamped with the moment it runs
        return new BuildInfo(time, metadata.GetValueOrDefault(CommitKey) ?? "",
            string.Equals(metadata.GetValueOrDefault(CiKey), "true", StringComparison.OrdinalIgnoreCase));
    }
}
