using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RuleLint;

/// <summary>
/// One file the rules look at: its path relative to the repository (forward slashes), its text and the source root it
/// belongs to. A file outside every source root (a document, a resource, a script) has no root; only the text rules read it.
/// </summary>
public sealed class SourceFile
{
    CompilationUnitSyntax? syntax;

    /// <summary>A file at <paramref name="path"/> (relative, forward slashes) with <paramref name="text"/>.</summary>
    public SourceFile(string path, string text, SourceRoot? root)
    {
        Path = path;
        Text = text;
        Root = root;
    }

    /// <summary>The path relative to the repository, with forward slashes.</summary>
    public string Path { get; }

    /// <summary>The whole text of the file.</summary>
    public string Text { get; }

    /// <summary>The source root the file belongs to, or null for a file the code rules do not read.</summary>
    public SourceRoot? Root { get; }

    /// <summary>True for a C# file inside a source root.</summary>
    public bool IsCode => Root is not null && Path.EndsWith(".cs", StringComparison.Ordinal);

    /// <summary>True for an Avalonia XAML file inside a source root.</summary>
    public bool IsXaml => Root is not null && Path.EndsWith(".axaml", StringComparison.Ordinal);

    /// <summary>The file name without its folders.</summary>
    public string FileName => Path[(Path.LastIndexOf('/') + 1)..];

    /// <summary>The parsed C# file (only for <see cref="IsCode"/>).</summary>
    public CompilationUnitSyntax Syntax =>
        syntax ??= (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(Text, new CSharpParseOptions(LanguageVersion.Preview), Path).GetRoot();

    /// <summary>The folders between the source root and the file (all folders of the path for a file without a root).</summary>
    public IReadOnlyList<string> FolderSegments
    {
        get
        {
            var below = Root is null ? Path : Path[(Root.Folder.TrimEnd('/').Length + 1)..];
            var segments = below.Split('/');
            return segments[..^1];
        }
    }

    /// <summary>The namespace the folder asks for: the root namespace and the folders below the root.</summary>
    public string FolderNamespace => string.Join('.', [Root?.RootNamespace ?? "", .. FolderSegments]).TrimStart('.');
}
