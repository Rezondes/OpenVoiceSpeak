using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RuleLint;

/// <summary>Reads the types a parsed file declares: classes, structs, records, interfaces, enums and delegates.</summary>
public static class DeclaredTypes
{
    /// <summary>The top-level types of <paramref name="unit"/>, inside its namespaces or outside any.</summary>
    public static IEnumerable<MemberDeclarationSyntax> ListTopLevel(CompilationUnitSyntax unit) => ListIn(unit.Members);

    /// <summary>Every type of <paramref name="unit"/>, nested ones included, in source order.</summary>
    public static IEnumerable<MemberDeclarationSyntax> ListAll(CompilationUnitSyntax unit) =>
        unit.DescendantNodes().OfType<MemberDeclarationSyntax>().Where(IsType);

    /// <summary>True for a type or delegate declaration.</summary>
    public static bool IsType(MemberDeclarationSyntax member) => member is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax;

    /// <summary>The identifier of a type or delegate declaration.</summary>
    public static SyntaxToken ReadIdentifier(MemberDeclarationSyntax member) => member switch
    {
        BaseTypeDeclarationSyntax type => type.Identifier,
        DelegateDeclarationSyntax declaration => declaration.Identifier,
        _ => default,
    };

    /// <summary>The simple name of a type or delegate declaration, without type parameters.</summary>
    public static string ReadName(MemberDeclarationSyntax member) => ReadIdentifier(member).ValueText;

    /// <summary>The namespace <paramref name="node"/> is declared in ("" outside any namespace).</summary>
    public static string ReadNamespace(SyntaxNode node) =>
        string.Join('.', node.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(space => space.Name.ToString()));

    static IEnumerable<MemberDeclarationSyntax> ListIn(SyntaxList<MemberDeclarationSyntax> members)
    {
        foreach (var member in members)
        {
            if (member is BaseNamespaceDeclarationSyntax space)
            {
                foreach (var inner in ListIn(space.Members)) yield return inner;
            }
            else if (IsType(member))
            {
                yield return member;
            }
        }
    }
}
