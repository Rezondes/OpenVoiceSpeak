namespace RuleLint.Rules;

/// <summary>Rule 18: no em dash (U+2014) in any text file of the repository, code, docs and resources alike.</summary>
public sealed class EmDashRule : IRule
{
    /// <summary>The banned character.</summary>
    public const char EmDash = (char)0x2014;

    /// <inheritdoc/>
    public IEnumerable<Finding> Check(SourceFile file, RepositoryIndex index)
    {
        for (var offset = file.Text.IndexOf(EmDash); offset >= 0; offset = file.Text.IndexOf(EmDash, offset + 1))
        {
            yield return Finding.CreateAtOffset(file, offset, "RL181", Severity.Error,
                "Em dash; use a colon, a comma, parentheses or a new sentence (rule 18).", "em dash");
        }
    }
}
