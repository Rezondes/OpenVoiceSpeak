using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RuleLint.Rules;

/// <summary>Rule 5: a nested type is private (no access modifier or <c>private</c>) and stands at the end of its container.</summary>
internal sealed class NestedTypeRule : IRule
{
    /// <inheritdoc/>
    public IEnumerable<Finding> Check(SourceFile file, RepositoryIndex index)
    {
        if (!file.IsCode) yield break;
        foreach (var container in file.Syntax.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            if (container is InterfaceDeclarationSyntax) continue;
            var containerName = container.Identifier.ValueText;
            var seenNested = false;
            var reportedOrder = false;
            foreach (var member in container.Members)
            {
                if (DeclaredTypes.IsType(member))
                {
                    seenNested = true;
                    if (!IsReachableFromOutside(member)) continue;
                    var name = DeclaredTypes.ReadName(member);
                    yield return Finding.CreateAt(file, DeclaredTypes.ReadIdentifier(member), "RL051", Severity.Error,
                        $"Nested type '{containerName}.{name}' is not private; make it a top-level type of its own (rule 5).", containerName + "." + name);
                }
                else if (seenNested && !reportedOrder)
                {
                    reportedOrder = true;
                    yield return Finding.CreateAt(file, member, "RL052", Severity.Error,
                        $"'{containerName}' declares '{MemberNames.Read(member)}' and more after a nested type; nested types stand at the end of their container (rule 5).", containerName);
                }
            }
        }
    }

    static bool IsReachableFromOutside(MemberDeclarationSyntax member) =>
        member.Modifiers.Any(modifier =>
            modifier.IsKind(SyntaxKind.PublicKeyword) || modifier.IsKind(SyntaxKind.InternalKeyword) || modifier.IsKind(SyntaxKind.ProtectedKeyword));
}
