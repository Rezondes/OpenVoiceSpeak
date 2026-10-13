namespace RuleLint.Rules;

/// <summary>
/// Rules 1 and 2: one top-level type per file, and the file is named after it (a generic type without its parameters,
/// a code-behind as <c>Type.axaml.cs</c>; a part of a partial type as <c>Type.Part.cs</c>, which <see cref="PartialTypeRule"/> checks).
/// </summary>
internal sealed class TypeFileRule : IRule
{
    /// <inheritdoc/>
    public IEnumerable<Finding> Check(SourceFile file, RepositoryIndex index) =>
        file.IsCode ? CheckCode(file) : file.IsXaml ? CheckXaml(file) : [];

    /// <summary>The type a file name stands for: the part before the first dot.</summary>
    public static string ReadTypeNameOfFile(string fileName) => fileName.Split('.')[0];

    static IEnumerable<Finding> CheckCode(SourceFile file)
    {
        var types = DeclaredTypes.ListTopLevel(file.Syntax).ToList();
        foreach (var extra in types.Skip(1))
        {
            var name = DeclaredTypes.ReadName(extra);
            yield return Finding.CreateAt(file, DeclaredTypes.ReadIdentifier(extra), "RL011", Severity.Error,
                $"'{name}' is a second top-level type in this file; give it a file of its own (rule 1).", name);
        }
        if (types.Count == 0) yield break;
        var first = DeclaredTypes.ReadName(types[0]);
        if (first != ReadTypeNameOfFile(file.FileName))
        {
            yield return Finding.CreateAt(file, DeclaredTypes.ReadIdentifier(types[0]), "RL012", Severity.Error,
                $"'{first}' is declared in '{file.FileName}'; name the file '{first}.cs' (rules 1 and 2).", first);
        }
    }

    static IEnumerable<Finding> CheckXaml(SourceFile file)
    {
        var match = RepositoryIndex.BuildXamlClassPattern().Match(file.Text);
        if (!match.Success) yield break;
        var fullName = match.Groups[1].Value;
        var name = fullName[(fullName.LastIndexOf('.') + 1)..];
        if (name != ReadTypeNameOfFile(file.FileName))
        {
            yield return Finding.CreateAtOffset(file, match.Index, "RL012", Severity.Error,
                $"'{name}' is declared in '{file.FileName}'; name the file '{name}.axaml' (rule 1).", name);
        }
    }
}
