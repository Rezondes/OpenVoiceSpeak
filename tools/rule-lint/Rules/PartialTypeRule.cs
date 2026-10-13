using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RuleLint.Rules;

/// <summary>Rule 4: only the types the project lists are split into parts, as <c>Type.Part.cs</c> in one folder.</summary>
internal sealed class PartialTypeRule : IRule
{
    readonly LintSettings settings;

    /// <summary>The rule with the project's list of partial types.</summary>
    public PartialTypeRule(LintSettings settings)
    {
        this.settings = settings;
    }

    /// <inheritdoc/>
    public IEnumerable<Finding> Check(SourceFile file, RepositoryIndex index)
    {
        if (!file.IsCode) yield break;
        var nameParts = file.FileName[..^".cs".Length].Split('.');
        var isPartFile = nameParts.Length > 1 && !(nameParts.Length == 2 && nameParts[1] == "axaml");
        foreach (var type in DeclaredTypes.ListTopLevel(file.Syntax).OfType<TypeDeclarationSyntax>())
        {
            var name = type.Identifier.ValueText;
            var space = DeclaredTypes.ReadNamespace(type);
            var paths = index.ListFilesOf(file.Root!, space.Length == 0 ? name : space + "." + name);
            var allowed = settings.PartialTypes.Contains(name);
            if ((isPartFile || paths.Count > 1) && !allowed)
            {
                yield return Finding.CreateAt(file, type.Identifier, "RL041", Severity.Error,
                    $"'{name}' is split into several files; only the types the project file lists may be (rule 4).", name);
            }
            else if (paths.Select(path => path[..path.LastIndexOf('/')]).Distinct().Count() > 1)
            {
                yield return Finding.CreateAt(file, type.Identifier, "RL042", Severity.Error,
                    $"The parts of '{name}' lie in different folders; keep them in one folder as '{name}.Part.cs' (rule 4).", name);
            }
        }
    }
}
