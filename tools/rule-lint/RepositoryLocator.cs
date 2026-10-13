namespace RuleLint;

/// <summary>Finds the repository root: the nearest folder at or above a start folder that holds the settings file.</summary>
internal static class RepositoryLocator
{
    /// <summary>The nearest folder at or above <paramref name="start"/> with <see cref="LintSettings.FileName"/>, or null.</summary>
    public static string? Find(string start)
    {
        for (var folder = new DirectoryInfo(start); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, LintSettings.FileName))) return folder.FullName;
        }
        return null;
    }
}
