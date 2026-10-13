namespace RuleLint.Rules;

/// <summary>
/// Rules 10 and 11, for production code: a type in a <c>Views/</c> folder ends in <c>View</c> (except the listed ones),
/// a type in a <c>ViewModels/</c> folder ends in <c>ViewModel</c>.
/// </summary>
internal sealed class UiSuffixRule : IRule
{
    readonly LintSettings settings;

    /// <summary>The rule with the project's views that keep their names.</summary>
    public UiSuffixRule(LintSettings settings)
    {
        this.settings = settings;
    }

    /// <inheritdoc/>
    public IEnumerable<Finding> Check(SourceFile file, RepositoryIndex index)
    {
        if (!file.IsCode || file.Root!.Kind != RootKind.Source || file.FolderSegments.Count == 0) yield break;
        var folder = file.FolderSegments[^1];
        foreach (var type in DeclaredTypes.ListTopLevel(file.Syntax))
        {
            var name = DeclaredTypes.ReadName(type);
            if (folder == "Views" && !name.EndsWith("View", StringComparison.Ordinal) && !settings.ViewExceptions.Contains(name))
            {
                yield return Finding.CreateAt(file, DeclaredTypes.ReadIdentifier(type), "RL101", Severity.Error,
                    $"'{name}' is in Views/ but does not end in 'View' (rule 10).", name);
            }
            if (folder == "ViewModels" && !name.EndsWith("ViewModel", StringComparison.Ordinal))
            {
                yield return Finding.CreateAt(file, DeclaredTypes.ReadIdentifier(type), "RL111", Severity.Error,
                    $"'{name}' is in ViewModels/ but is no view model ending in 'ViewModel'; bound records and enums go to Models/ (rule 11).", name);
            }
        }
    }
}
