using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RuleLint.Rules;

/// <summary>
/// The single-responsibility red flags of the review checklist, as warnings in production and tool code: a type
/// declaration over the type limit, a method, constructor or local function body over the method limit.
/// </summary>
public sealed class SizeRule : IRule
{
    readonly LintSettings settings;

    /// <summary>The rule with the line limits of the settings.</summary>
    public SizeRule(LintSettings settings)
    {
        this.settings = settings;
    }

    /// <inheritdoc/>
    public IEnumerable<Finding> Check(SourceFile file, RepositoryIndex index)
    {
        if (!file.IsCode || file.Root!.Kind == RootKind.Test) yield break;
        foreach (var type in DeclaredTypes.ListTopLevel(file.Syntax).OfType<TypeDeclarationSyntax>())
        {
            var lines = CountLines(type);
            if (lines <= settings.MaxTypeLines) continue;
            yield return Finding.CreateAt(file, type.Identifier, "RL701", Severity.Warning,
                $"'{type.Identifier.ValueText}' has {lines} lines here, over {settings.MaxTypeLines}; extract a collaborator (review checklist, SRP).", type.Identifier.ValueText);
        }
        foreach (var node in file.Syntax.DescendantNodes())
        {
            var body = node switch
            {
                BaseMethodDeclarationSyntax method => (SyntaxNode?)method.Body ?? method.ExpressionBody,
                LocalFunctionStatementSyntax function => (SyntaxNode?)function.Body ?? function.ExpressionBody,
                _ => null,
            };
            if (body is null) continue;
            var lines = CountLines(body);
            if (lines <= settings.MaxMethodLines) continue;
            var local = node as LocalFunctionStatementSyntax;
            var subject = MemberNames.ReadEnclosing(node) + (local is null ? "" : "." + local.Identifier.ValueText);
            yield return Finding.CreateAt(file, local?.Identifier ?? node.GetFirstToken(), "RL702", Severity.Warning,
                $"'{subject}' has {lines} lines, over {settings.MaxMethodLines}; split it (review checklist, SRP).", subject);
        }
    }

    static int CountLines(SyntaxNode node)
    {
        var span = node.GetLocation().GetLineSpan();
        return span.EndLinePosition.Line - span.StartLinePosition.Line + 1;
    }
}
