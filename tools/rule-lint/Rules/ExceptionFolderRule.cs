using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RuleLint.Rules;

/// <summary>Rule 9: an exception type of production or tool code lives in an <c>Exceptions/</c> folder.</summary>
internal sealed class ExceptionFolderRule : IRule
{
    /// <inheritdoc/>
    public IEnumerable<Finding> Check(SourceFile file, RepositoryIndex index)
    {
        if (!file.IsCode || file.Root!.Kind == RootKind.Test || file.FolderSegments.Contains("Exceptions")) yield break;
        foreach (var type in DeclaredTypes.ListTopLevel(file.Syntax).OfType<ClassDeclarationSyntax>())
        {
            var name = type.Identifier.ValueText;
            if (!name.EndsWith("Exception", StringComparison.Ordinal) || type.BaseList is null) continue;
            yield return Finding.CreateAt(file, type.Identifier, "RL091", Severity.Error,
                $"Exception '{name}' lives outside an Exceptions/ folder; move it there (rule 9).", name);
        }
    }
}
