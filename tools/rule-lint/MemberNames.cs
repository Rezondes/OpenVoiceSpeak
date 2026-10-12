using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RuleLint;

/// <summary>Reads the name of a member declaration, for messages and baseline subjects.</summary>
public static class MemberNames
{
    /// <summary>The name of <paramref name="member"/>: its identifier, the variables of a field, or its kind.</summary>
    public static string Read(MemberDeclarationSyntax member) => member switch
    {
        BaseTypeDeclarationSyntax type => type.Identifier.ValueText,
        DelegateDeclarationSyntax declaration => declaration.Identifier.ValueText,
        MethodDeclarationSyntax method => method.Identifier.ValueText,
        ConstructorDeclarationSyntax constructor => constructor.Identifier.ValueText,
        PropertyDeclarationSyntax property => property.Identifier.ValueText,
        EventDeclarationSyntax declaredEvent => declaredEvent.Identifier.ValueText,
        EnumMemberDeclarationSyntax enumMember => enumMember.Identifier.ValueText,
        BaseFieldDeclarationSyntax field => string.Join(",", field.Declaration.Variables.Select(variable => variable.Identifier.ValueText)),
        _ => member.Kind().ToString(),
    };

    /// <summary>
    /// The type and member <paramref name="node"/> stands in, as <c>Type.Member</c> (only <c>Type</c> outside a member,
    /// <c>Program</c> in top-level statements): a baseline subject that does not move with the line.
    /// </summary>
    public static string ReadEnclosing(SyntaxNode node)
    {
        var member = node.AncestorsAndSelf().OfType<MemberDeclarationSyntax>()
            .FirstOrDefault(candidate => !DeclaredTypes.IsType(candidate) && candidate is not BaseNamespaceDeclarationSyntax);
        var type = node.AncestorsAndSelf().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault();
        var typeName = type?.Identifier.ValueText ?? "Program";
        return member is null || member is GlobalStatementSyntax ? typeName : typeName + "." + Read(member);
    }
}
