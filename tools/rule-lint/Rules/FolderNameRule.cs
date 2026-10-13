namespace RuleLint.Rules;

/// <summary>
/// Rules 7 and 8: no folder is named like a pattern the <c>.gitignore</c> ignores, and no folder (a namespace segment) is
/// named like a type of its project. Each folder is reported once, at the first file under it.
/// </summary>
internal sealed class FolderNameRule : IRule
{
    readonly LintSettings settings;

    /// <summary>The rule with the project's ignored folder names.</summary>
    public FolderNameRule(LintSettings settings)
    {
        this.settings = settings;
    }

    /// <inheritdoc/>
    public IEnumerable<Finding> Check(SourceFile file, RepositoryIndex index)
    {
        if (file.Root is null) yield break;
        var typeNames = index.ListTypeNames(file.Root);
        var folder = file.Root.Folder.TrimEnd('/');
        foreach (var segment in file.FolderSegments)
        {
            folder += "/" + segment;
            if (!index.IsFirstUnder(folder, file)) continue;
            if (IsIgnored(segment))
            {
                yield return Finding.CreateInFile(file, "RL071", Severity.Error,
                    $"Folder '{folder}' is named like a pattern the .gitignore ignores; choose another name (rule 7).", folder);
            }
            if (typeNames.Contains(segment))
            {
                yield return Finding.CreateInFile(file, "RL081", Severity.Error,
                    $"Folder '{folder}' is named like the type '{segment}' of its project; namespace segments never name a type (rule 8).", folder);
            }
        }
    }

    bool IsIgnored(string segment) =>
        settings.IgnoredFolderNames.Contains(segment, StringComparer.OrdinalIgnoreCase)
        || settings.IgnoredFolderPrefixes.Any(prefix => segment.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}
