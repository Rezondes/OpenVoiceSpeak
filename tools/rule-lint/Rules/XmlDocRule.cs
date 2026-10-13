using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RuleLint.Rules;

/// <summary>
/// Rule 23, in production and tool code: every type and every non-private member (a member of an interface or an enum
/// counts) has an XML doc comment; <c>&lt;inheritdoc/&gt;</c> counts, and so does the message of a <c>[LoggerMessage]</c> method (Step 31: the message is
/// what the method writes). Members of private nested types are left out.
/// </summary>
internal sealed class XmlDocRule : IRule
{
    /// <inheritdoc/>
    public IEnumerable<Finding> Check(SourceFile file, RepositoryIndex index)
    {
        if (!file.IsCode || file.Root!.Kind == RootKind.Test) yield break;
        foreach (var member in file.Syntax.DescendantNodes().OfType<MemberDeclarationSyntax>())
        {
            if (member is BaseNamespaceDeclarationSyntax or GlobalStatementSyntax || !NeedsDoc(member) || HasDoc(member)) continue;
            var subject = MemberNames.ReadEnclosing(member);
            yield return Finding.CreateAt(file, member.GetFirstToken(), "RL231", Severity.Error,
                $"'{subject}' has no XML doc comment (rule 23).", subject);
        }
    }

    static bool NeedsDoc(MemberDeclarationSyntax member)
    {
        if (member.Ancestors().OfType<MemberDeclarationSyntax>().Any(IsPrivateNested)) return false;
        if (DeclaredTypes.IsType(member)) return !IsPrivateNested(member);
        if (member.Parent is InterfaceDeclarationSyntax || member is EnumMemberDeclarationSyntax) return true;
        if (member is ConstructorDeclarationSyntax constructor && constructor.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.StaticKeyword))) return false;
        if (member is MethodDeclarationSyntax method && method.ExplicitInterfaceSpecifier is not null) return false;
        return IsVisible(member);
    }

    static bool IsPrivateNested(MemberDeclarationSyntax type) =>
        DeclaredTypes.IsType(type) && type.Parent is BaseTypeDeclarationSyntax && !IsVisible(type);

    static bool IsVisible(MemberDeclarationSyntax member) =>
        member.Modifiers.Any(modifier =>
            modifier.IsKind(SyntaxKind.PublicKeyword) || modifier.IsKind(SyntaxKind.InternalKeyword) || modifier.IsKind(SyntaxKind.ProtectedKeyword));

    static bool HasDoc(MemberDeclarationSyntax member) =>
        member.AttributeLists.SelectMany(list => list.Attributes).Any(attribute => attribute.Name.ToString() is "LoggerMessage" or "LoggerMessageAttribute") ||
        member.GetLeadingTrivia().Any(trivia =>
            trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia));
}
