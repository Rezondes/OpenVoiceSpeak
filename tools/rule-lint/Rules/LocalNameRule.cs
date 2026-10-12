using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RuleLint.Rules;

/// <summary>
/// Rule 20: every local, parameter, field, lambda parameter, loop, catch and pattern variable is a readable word: no
/// one-letter name except the discard <c>_</c>, and none of the banned abbreviations.
/// </summary>
public sealed class LocalNameRule : IRule
{
    readonly LintSettings settings;

    /// <summary>The rule with the banned abbreviations of the settings.</summary>
    public LocalNameRule(LintSettings settings)
    {
        this.settings = settings;
    }

    /// <inheritdoc/>
    public IEnumerable<Finding> Check(SourceFile file, RepositoryIndex index)
    {
        if (!file.IsCode) yield break;
        foreach (var token in ListNameTokens(file.Syntax))
        {
            var name = token.ValueText.TrimStart('@');
            if (name == "_" || name.Length == 0) continue;
            if (name.Length > 1 && !settings.BannedNames.Contains(name)) continue;
            yield return Finding.CreateAt(file, token, "RL201", Severity.Error,
                $"'{name}' is no readable word; name what it holds (rule 20).", MemberNames.ReadEnclosing(token.Parent!) + ":" + name);
        }
    }

    static IEnumerable<SyntaxToken> ListNameTokens(SyntaxNode root)
    {
        foreach (var node in root.DescendantNodes())
        {
            var token = node switch
            {
                VariableDeclaratorSyntax variable => variable.Identifier,
                ParameterSyntax parameter => parameter.Identifier,
                SingleVariableDesignationSyntax designation => designation.Identifier,
                ForEachStatementSyntax loop => loop.Identifier,
                CatchDeclarationSyntax caught => caught.Identifier,
                FromClauseSyntax from => from.Identifier,
                LetClauseSyntax let => let.Identifier,
                JoinClauseSyntax join => join.Identifier,
                QueryContinuationSyntax continuation => continuation.Identifier,
                _ => default,
            };
            if (token != default) yield return token;
        }
    }
}
