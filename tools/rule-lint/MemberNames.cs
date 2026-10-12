using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RuleLint;

/// <summary>Reads the name of a member declaration, for messages and baseline subjects.</summary>
public static class MemberNames
{
    /// <summary>The name of <paramref name="member"/>: its identifier, the first variable of a field, or its kind.</summary>
    public static string Read(MemberDeclarationSyntax member) => member switch
    {
        BaseTypeDeclarationSyntax type => type.Identifier.ValueText,
        DelegateDeclarationSyntax declaration => declaration.Identifier.ValueText,
        MethodDeclarationSyntax method => method.Identifier.ValueText,
        ConstructorDeclarationSyntax constructor => constructor.Identifier.ValueText,
        PropertyDeclarationSyntax property => property.Identifier.ValueText,
        EventDeclarationSyntax declaredEvent => declaredEvent.Identifier.ValueText,
        BaseFieldDeclarationSyntax field => string.Join(",", field.Declaration.Variables.Select(variable => variable.Identifier.ValueText)),
        _ => member.Kind().ToString(),
    };
}
