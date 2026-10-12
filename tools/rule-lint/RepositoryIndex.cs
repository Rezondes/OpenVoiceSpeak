using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RuleLint;

/// <summary>
/// What the rules need to know about all files at once: the type names of each source root, the code files of each
/// type, the first file under each folder (so a rule about a folder reports it once) and how often each interface is
/// implemented in production or tool code and in tests.
/// </summary>
public sealed partial class RepositoryIndex
{
    readonly Dictionary<string, HashSet<string>> typeNamesByRoot = [];
    readonly Dictionary<string, List<string>> filesByType = [];
    readonly Dictionary<string, string> firstFileUnderFolder = [];
    readonly Dictionary<string, int> productionImplementations = [];
    readonly Dictionary<string, int> testImplementations = [];

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
            if (file.IsCode) index.CountImplementations(file);
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

    /// <summary>How many types of production or tool code name <paramref name="interfaceName"/> in their base list.</summary>
    public int CountProductionImplementations(string interfaceName) => productionImplementations.GetValueOrDefault(interfaceName);

    /// <summary>How many test types name <paramref name="interfaceName"/> in their base list (fakes).</summary>
    public int CountTestImplementations(string interfaceName) => testImplementations.GetValueOrDefault(interfaceName);

    /// <summary>The <c>x:Class</c> attribute of a XAML file.</summary>
    [GeneratedRegex("x:Class=\"([^\"]+)\"")]
    public static partial Regex BuildXamlClassPattern();

    /// <summary>The simple name of a base type: without namespace and type arguments.</summary>
    public static string ReadSimpleName(TypeSyntax type) => type switch
    {
        QualifiedNameSyntax qualified => ReadSimpleName(qualified.Right),
        GenericNameSyntax generic => generic.Identifier.ValueText,
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        AliasQualifiedNameSyntax alias => ReadSimpleName(alias.Name),
        _ => type.ToString(),
    };

    void CountImplementations(SourceFile file)
    {
        var counts = file.Root!.Kind == RootKind.Test ? testImplementations : productionImplementations;
        foreach (var type in file.Syntax.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            if (type is InterfaceDeclarationSyntax || type.BaseList is null) continue;
            foreach (var baseType in type.BaseList.Types)
            {
                var name = ReadSimpleName(baseType.Type);
                counts[name] = counts.GetValueOrDefault(name) + 1;
            }
        }
    }

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
