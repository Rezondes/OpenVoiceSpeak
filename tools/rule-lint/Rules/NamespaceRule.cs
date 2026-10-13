using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RuleLint.Rules;

/// <summary>Rule 6: the namespace is the folder, declared file-scoped, and no file adds a <c>global using</c>.</summary>
internal sealed class NamespaceRule : IRule
{
    /// <inheritdoc/>
    public IEnumerable<Finding> Check(SourceFile file, RepositoryIndex index) =>
        file.IsCode ? CheckCode(file) : file.IsXaml ? CheckXaml(file) : [];

    static IEnumerable<Finding> CheckCode(SourceFile file)
    {
        var expected = file.FolderNamespace;
        foreach (var directive in file.Syntax.Usings.Where(directive => directive.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword)))
        {
            yield return Finding.CreateAt(file, directive, "RL063", Severity.Error,
                $"'{directive}' adds a global using; write explicit using lines in each file (rule 6).", directive.Name?.ToString() ?? "global");
        }
        var spaces = file.Syntax.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>().ToList();
        foreach (var block in spaces.OfType<NamespaceDeclarationSyntax>())
        {
            yield return Finding.CreateAt(file, block.Name, "RL062", Severity.Error,
                $"Namespace '{block.Name}' is a block; declare it file-scoped as 'namespace {block.Name};' (rule 6).", block.Name.ToString());
        }
        if (spaces.Count == 0)
        {
            var first = DeclaredTypes.ListTopLevel(file.Syntax).FirstOrDefault();
            if (first is not null)
            {
                yield return Finding.CreateAt(file, DeclaredTypes.ReadIdentifier(first), "RL061", Severity.Error,
                    $"'{DeclaredTypes.ReadName(first)}' is declared outside any namespace; add 'namespace {expected};' (rule 6).", expected);
            }
            yield break;
        }
        foreach (var space in spaces.Where(space => space.Name.ToString() != expected))
        {
            yield return Finding.CreateAt(file, space.Name, "RL061", Severity.Error,
                $"Namespace '{space.Name}' does not match the folder; use '{expected}' (rule 6).", space.Name.ToString());
        }
    }

    static IEnumerable<Finding> CheckXaml(SourceFile file)
    {
        var match = RepositoryIndex.BuildXamlClassPattern().Match(file.Text);
        if (!match.Success) yield break;
        var fullName = match.Groups[1].Value;
        var space = fullName[..Math.Max(0, fullName.LastIndexOf('.'))];
        if (space != file.FolderNamespace)
        {
            yield return Finding.CreateAtOffset(file, match.Index, "RL061", Severity.Error,
                $"x:Class '{fullName}' is not in the namespace of its folder '{file.FolderNamespace}' (rule 6).", space);
        }
    }
}
