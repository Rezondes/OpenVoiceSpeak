namespace RuleLint;

/// <summary>
/// One check of the coding rules. A rule looks at one file at a time and may use what <see cref="RepositoryIndex"/>
/// knows about all files. Every rule is listed once in <see cref="Linter.CreateRules"/>.
/// </summary>
internal interface IRule
{
    /// <summary>The violations of this rule in <paramref name="file"/>.</summary>
    IEnumerable<Finding> Check(SourceFile file, RepositoryIndex index);
}
