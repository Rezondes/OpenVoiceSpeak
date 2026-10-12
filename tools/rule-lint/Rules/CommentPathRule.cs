using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace RuleLint.Rules;

/// <summary>Rule 24, as a warning: a comment names a type, not the path of its source file.</summary>
public sealed partial class CommentPathRule : IRule
{
    /// <inheritdoc/>
    public IEnumerable<Finding> Check(SourceFile file, RepositoryIndex index)
    {
        if (!file.IsCode) yield break;
        foreach (var trivia in file.Syntax.DescendantTrivia())
        {
            if (!trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) && !trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)
                && !trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) && !trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia)) continue;
            foreach (Match match in BuildPathPattern().Matches(trivia.ToFullString()))
            {
                yield return Finding.CreateAtOffset(file, trivia.FullSpan.Start + match.Index, "RL241", Severity.Warning,
                    $"The comment names the path '{match.Value}'; name the type instead (rule 24).", match.Value);
            }
        }
    }

    [GeneratedRegex(@"[\w.-]+(/[\w.-]+)+\.(cs|axaml)\b")]
    private static partial Regex BuildPathPattern();
}
