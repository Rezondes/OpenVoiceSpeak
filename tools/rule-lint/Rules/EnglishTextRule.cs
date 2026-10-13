using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RuleLint.Rules;

/// <summary>
/// Rule 16, as a warning: an exception message in production or tool code with German letters (ä, ö, ü, ß) is likely not
/// English. Log lines are not recognized (their writers differ per project); the review checks them.
/// </summary>
public sealed class EnglishTextRule : IRule
{
    /// <inheritdoc/>
    public IEnumerable<Finding> Check(SourceFile file, RepositoryIndex index)
    {
        if (!file.IsCode || file.Root!.Kind == RootKind.Test) yield break;
        foreach (var creation in file.Syntax.DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>())
        {
            if (!creation.Ancestors().Any(ancestor => ancestor is ThrowStatementSyntax or ThrowExpressionSyntax)) continue;
            var text = string.Concat(creation.ArgumentList?.Arguments.SelectMany(argument => argument.DescendantTokens()).Select(token => token.ValueText) ?? []);
            if (!text.Any(letter => "äöüÄÖÜß".Contains(letter, StringComparison.Ordinal))) continue;
            yield return Finding.CreateAt(file, creation, "RL161", Severity.Warning,
                "This exception message looks German; exception messages and log lines are English (rule 16).", MemberNames.ReadEnclosing(creation));
        }
    }
}
