using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RuleLint;

/// <summary>
/// What the rules need to know about all files at once: the type names of each source root, the code files of each
/// type, and the first file under each folder (so a rule about a folder reports it once).
/// </summary>
public sealed partial class RepositoryIndex
{
    readonly Dictionary<string, HashSet<string>> typeNamesByRoot = [];
    readonly Dictionary<string, List<string>> filesByType = [];
    readonly Dictionary<string, string> firstFileUnderFolder = [];

    RepositoryIndex()
    {
    }

    /// <summary>The index of <paramref name="files"/>.</summary>
    public static RepositoryIndex Build(IEnumerable<SourceFile> files)
    {
        var index = new RepositoryIndex();
        foreach (var file in files)
        {
            if (file.Root is null) continue;
            index.RememberFolders(file.Path);
            if (!index.typeNamesByRoot.TryGetValue(file.Root.Folder, out var names)) index.typeNamesByRoot[file.Root.Folder] = names = [];
            foreach (var fullName in ListDeclaredTypes(file))
            {
                names.Add(fullName[(fullName.LastIndexOf('.') + 1)..]);
                if (!file.IsCode) continue;
                var key = file.Root.Folder + "|" + fullName;
                if (!index.filesByType.TryGetValue(key, out var paths)) index.filesByType[key] = paths = [];
                if (!paths.Contains(file.Path)) paths.Add(file.Path);
            }
        }
        return index;
    }

    /// <summary>The simple names of every type declared in <paramref name="root"/>, nested types and XAML classes included.</summary>
    public IReadOnlySet<string> ListTypeNames(SourceRoot root) =>
        typeNamesByRoot.TryGetValue(root.Folder, out var names) ? names : new HashSet<string>();

    /// <summary>The C# files that declare the type <paramref name="fullName"/> (namespace and name) in <paramref name="root"/>.</summary>
    public IReadOnlyList<string> ListFilesOf(SourceRoot root, string fullName) =>
        filesByType.TryGetValue(root.Folder + "|" + fullName, out var paths) ? paths : [];

    /// <summary>True when <paramref name="file"/> is the first file in path order anywhere under <paramref name="folder"/>.</summary>
    public bool IsFirstUnder(string folder, SourceFile file) =>
        firstFileUnderFolder.TryGetValue(folder, out var first) && first == file.Path;

    /// <summary>The <c>x:Class</c> attribute of a XAML file.</summary>
    [GeneratedRegex("x:Class=\"([^\"]+)\"")]
    public static partial Regex BuildXamlClassPattern();

    void RememberFolders(string path)
    {
        for (var slash = path.IndexOf('/'); slash >= 0; slash = path.IndexOf('/', slash + 1))
        {
            var folder = path[..slash];
            if (!firstFileUnderFolder.TryGetValue(folder, out var first) || string.CompareOrdinal(path, first) < 0)
                firstFileUnderFolder[folder] = path;
        }
    }

    static IEnumerable<string> ListDeclaredTypes(SourceFile file)
    {
        if (file.IsXaml)
        {
            var match = BuildXamlClassPattern().Match(file.Text);
            if (match.Success) yield return match.Groups[1].Value;
            yield break;
        }
        if (!file.IsCode) yield break;
        foreach (var member in DeclaredTypes.ListAll(file.Syntax))
        {
            var space = DeclaredTypes.ReadNamespace(member);
            var outer = string.Join('.', member.Ancestors().OfType<BaseTypeDeclarationSyntax>().Reverse().Select(type => type.Identifier.ValueText));
            yield return string.Join('.', new[] { space, outer, DeclaredTypes.ReadName(member) }.Where(part => part.Length > 0));
        }
    }
}
