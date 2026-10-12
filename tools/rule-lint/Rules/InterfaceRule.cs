using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RuleLint.Rules;

/// <summary>
/// Rule 3, as a warning: an interface of production or tool code has several production implementations or a fake in
/// the tests. Counted by name in base lists, so an implementation through a base class is not seen; the review decides.
/// </summary>
public sealed class InterfaceRule : IRule
{
    /// <inheritdoc/>
    public IEnumerable<Finding> Check(SourceFile file, RepositoryIndex index)
    {
        if (!file.IsCode || file.Root!.Kind == RootKind.Test) yield break;
        foreach (var declared in file.Syntax.DescendantNodes().OfType<InterfaceDeclarationSyntax>())
        {
            var name = declared.Identifier.ValueText;
            var production = index.CountProductionImplementations(name);
            if (production > 1 || index.CountTestImplementations(name) > 0) continue;
            yield return Finding.CreateAt(file, declared.Identifier, "RL031", Severity.Warning,
                $"Interface '{name}' has {production} production implementation(s) and no fake in the tests; use the class or a delegate (rule 3).", name);
        }
    }
}
