using RuleLint.Rules;

namespace RuleLint;

/// <summary>Runs every rule over a set of files.</summary>
public static class Linter
{
    /// <summary>Every finding of every rule in <paramref name="files"/>, by path, line, column and rule.</summary>
    public static List<Finding> Run(LintSettings settings, IReadOnlyList<SourceFile> files)
    {
        var index = RepositoryIndex.Build(files);
        var rules = CreateRules(settings);
        return
        [
            .. files.SelectMany(file => rules.SelectMany(rule => rule.Check(file, index)))
                .OrderBy(finding => finding.Path, StringComparer.Ordinal)
                .ThenBy(finding => finding.Line)
                .ThenBy(finding => finding.Column)
                .ThenBy(finding => finding.RuleId, StringComparer.Ordinal),
        ];
    }

    /// <summary>Every rule, once. A new rule is added here; <c>LinterTests</c> fails for a rule type missing from this list.</summary>
    public static IReadOnlyList<IRule> CreateRules(LintSettings settings) =>
    [
        new TypeFileRule(),
        new PartialTypeRule(settings),
        new NestedTypeRule(),
        new NamespaceRule(),
        new FolderNameRule(settings),
        new ExceptionFolderRule(),
        new UiSuffixRule(settings),
        new XamlRule(settings),
        new TypeNameRule(settings),
        new MethodNameRule(),
        new EnglishTextRule(),
        new EmDashRule(),
        new LocalNameRule(settings),
        new TupleRule(),
        new PrimaryConstructorRule(),
        new XmlDocRule(),
        new CommentPathRule(),
    ];
}
