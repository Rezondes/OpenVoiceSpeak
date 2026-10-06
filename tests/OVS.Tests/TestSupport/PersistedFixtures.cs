namespace OVS.Tests.TestSupport;

/// <summary>
/// The persisted files under <c>TestSupport/Fixtures</c>, one folder per version that wrote them: the last commit before
/// the second refactoring (3d5c4db) and the last release (deploy-4053cfa). They were written by the real code of that
/// version, all at <see cref="Date"/>, with a throwaway certificate in the backup; they are never regenerated.
/// </summary>
static class PersistedFixtures
{
    /// <summary>The time the fixtures were written at; a test that loads them pins its clock to it.</summary>
    public static readonly DateTimeOffset Date = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    public const string Current = "3d5c4db";
    public const string Release = "deploy-4053cfa";

    public static TheoryData<string> Versions => new(Current, Release);

    /// <summary>Copies one fixture file of a version into <paramref name="directory"/> and returns its new path.</summary>
    public static string CopyTo(string version, string fileName, string directory)
    {
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, fileName);
        File.Copy(Path.Combine(RepoPaths.Root, "tests", "OVS.Tests", "TestSupport", "Fixtures", version, fileName), target);
        return target;
    }
}
