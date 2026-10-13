using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RuleLint.Rules;

/// <summary>
/// Rule 22: a class (not a record) whose primary constructor parameters are guarded (a <c>throw</c> or a
/// <c>ThrowIf...</c> call in a field or property initializer) uses the classic constructor form.
/// </summary>
internal sealed class PrimaryConstructorRule : IRule
{
    /// <inheritdoc/>
    public IEnumerable<Finding> Check(SourceFile file, RepositoryIndex index)
    {
        if (!file.IsCode) yield break;
        foreach (var type in file.Syntax.DescendantNodes().OfType<ClassDeclarationSyntax>().Where(type => type.ParameterList is not null))
        {
            var initializers = type.Members.SelectMany(ListInitializers);
            if (!initializers.Any(IsGuard)) continue;
            yield return Finding.CreateAt(file, type.Identifier, "RL221", Severity.Error,
                $"'{type.Identifier.ValueText}' guards its primary constructor parameters; use a classic constructor (rule 22).", type.Identifier.ValueText);
        }
    }

    static IEnumerable<EqualsValueClauseSyntax> ListInitializers(MemberDeclarationSyntax member)
    {
        if (member is FieldDeclarationSyntax field)
        {
            foreach (var variable in field.Declaration.Variables)
            {
                if (variable.Initializer is not null) yield return variable.Initializer;
            }
        }
        else if (member is PropertyDeclarationSyntax { Initializer: not null } property)
        {
            yield return property.Initializer;
        }
    }

    static bool IsGuard(EqualsValueClauseSyntax initializer) =>
        initializer.DescendantNodes().Any(node =>
            node is ThrowExpressionSyntax
            || (node is InvocationExpressionSyntax call && call.Expression.ToString().Contains("ThrowIf", StringComparison.Ordinal)));
}
