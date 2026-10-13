using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RuleLint.Rules;

/// <summary>
/// Rule 21: no tuple in a return type (also <c>Task&lt;(...)&gt;</c> or <c>IEnumerable&lt;(...)&gt;</c>) and no <c>out</c>
/// parameter instead of a record; <c>bool TryX(..., out T)</c>, <c>Deconstruct</c>, overrides, explicit interface
/// members and native imports keep theirs.
/// </summary>
internal sealed class TupleRule : IRule
{
    /// <inheritdoc/>
    public IEnumerable<Finding> Check(SourceFile file, RepositoryIndex index)
    {
        if (!file.IsCode) yield break;
        foreach (var node in file.Syntax.DescendantNodes())
        {
            var returned = node switch
            {
                MethodDeclarationSyntax method => method.ReturnType,
                LocalFunctionStatementSyntax function => function.ReturnType,
                PropertyDeclarationSyntax property => property.Type,
                DelegateDeclarationSyntax declaration => declaration.ReturnType,
                OperatorDeclarationSyntax declaredOperator => declaredOperator.ReturnType,
                _ => null,
            };
            if (returned is not null && returned.DescendantNodesAndSelf().OfType<TupleTypeSyntax>().Any())
            {
                yield return Finding.CreateAt(file, returned, "RL211", Severity.Error,
                    $"'{returned}' returns a tuple; return a named record (rule 21).", MemberNames.ReadEnclosing(node) + ReadLocalName(node));
            }
        }
        foreach (var method in file.Syntax.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (IsExempt(method)) continue;
            foreach (var parameter in method.ParameterList.Parameters.Where(parameter => parameter.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.OutKeyword))))
            {
                yield return Finding.CreateAt(file, parameter, "RL212", Severity.Error,
                    $"'out {parameter.Identifier.ValueText}' is a second result; return a named record, or use the 'bool TryX(..., out T)' form (rule 21).",
                    MemberNames.ReadEnclosing(method) + ":" + parameter.Identifier.ValueText);
            }
        }
    }

    static string ReadLocalName(SyntaxNode node) => node is LocalFunctionStatementSyntax function ? "." + function.Identifier.ValueText : "";

    static bool IsExempt(MethodDeclarationSyntax method)
    {
        var name = method.Identifier.ValueText;
        var returnsBool = method.ReturnType is PredefinedTypeSyntax predefined && predefined.Keyword.IsKind(SyntaxKind.BoolKeyword);
        return (name.StartsWith("Try", StringComparison.Ordinal) && returnsBool)
            || name == "Deconstruct"
            || method.ExplicitInterfaceSpecifier is not null
            || method.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.OverrideKeyword) || modifier.IsKind(SyntaxKind.ExternKeyword))
            || method.AttributeLists.SelectMany(list => list.Attributes).Any(attribute => attribute.Name.ToString() is "LibraryImport" or "DllImport");
    }
}
