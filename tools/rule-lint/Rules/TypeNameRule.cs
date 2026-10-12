namespace RuleLint.Rules;

/// <summary>
/// Rule 15 for type names, in production and tool code: no bag names, <c>Service</c>, <c>Info</c> and <c>Data</c> only
/// with a subject, and no type named as the plural of another type of its project.
/// </summary>
public sealed class TypeNameRule : IRule
{
    readonly LintSettings settings;

    /// <summary>The rule with the bag endings and subject words of the settings.</summary>
    public TypeNameRule(LintSettings settings)
    {
        this.settings = settings;
    }

    /// <inheritdoc/>
    public IEnumerable<Finding> Check(SourceFile file, RepositoryIndex index)
    {
        if (!file.IsCode || file.Root!.Kind == RootKind.Test) yield break;
        var projectTypes = index.ListTypeNames(file.Root);
        foreach (var type in DeclaredTypes.ListAll(file.Syntax))
        {
            var name = DeclaredTypes.ReadName(type);
            var identifier = DeclaredTypes.ReadIdentifier(type);
            var bag = settings.BagSuffixes.FirstOrDefault(suffix => name.EndsWith(suffix, StringComparison.Ordinal));
            if (bag is not null)
            {
                yield return Finding.CreateAt(file, identifier, "RL151", Severity.Error,
                    $"'{name}' is a bag name ending in '{bag}'; name what the type holds or decides (rule 15).", name);
            }
            if (settings.SubjectWords.Contains(name))
            {
                yield return Finding.CreateAt(file, identifier, "RL152", Severity.Error,
                    $"'{name}' needs a subject (e.g. 'Channel{name}') (rule 15).", name);
            }
            var singular = FindSingular(name, projectTypes);
            if (singular is not null)
            {
                yield return Finding.CreateAt(file, identifier, "RL153", Severity.Error,
                    $"'{name}' is the plural of the type '{singular}'; name what it holds (e.g. '{singular}List', '{singular}Rules') (rule 15).", name);
            }
        }
    }

    /// <summary>The type of <paramref name="typeNames"/> that <paramref name="name"/> is the plural of, or null.</summary>
    public static string? FindSingular(string name, IReadOnlySet<string> typeNames)
    {
        string[] candidates =
        [
            name.EndsWith('s') ? name[..^1] : "",
            name.EndsWith("es", StringComparison.Ordinal) ? name[..^2] : "",
            name.EndsWith("ies", StringComparison.Ordinal) ? name[..^3] + "y" : "",
        ];
        return candidates.FirstOrDefault(candidate => candidate.Length > 0 && candidate != name && typeNames.Contains(candidate));
    }
}
