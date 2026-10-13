using System.Text;
using System.Text.RegularExpressions;
using OVS.Tests.TestSupport;

namespace OVS.Tests;

/// <summary>
/// The scanner of CODING-RULES-OVS.md §Rule checker. It works on text, not on a syntax tree: comments, strings, character
/// literals and preprocessor lines are stripped, braces are counted, and the <c>namespace</c> and type declarations
/// (top-level and nested, with their modifiers) are read from what stands before each <c>{</c> or <c>;</c>. It is no
/// C# parser; <see cref="SourceLayoutTests"/> pins the forms the code base uses.
/// </summary>
static class SourceLayout
{
    static readonly string[] Roots = ["src", "tests", "tools/render-icon"];

    // CODING-RULES.md rule 7; "Backup*" is a prefix, the others whole names, all ignoring case like git on Windows.
    static readonly string[] IgnoredFolders = ["Log", "Logs", "Debug", "Release", "bin", "obj", "x64", "artifacts", "publish", "Generated_Code"];

    static readonly Regex Token = new(@"@?[\p{L}_][\p{L}\p{Nd}_]*|=>|==|::|\S");
    static readonly Regex Identifier = new(@"^@?[\p{L}_][\p{L}\p{Nd}_]*$");
    static readonly Regex XamlClass = new("x:Class=\"([^\"]+)\"");

    /// <summary>
    /// Every violation in the <c>.cs</c> and <c>.axaml</c> files under <c>src/</c>, <c>tests/</c> and
    /// <c>tools/render-icon</c> of <paramref name="repo"/> (without <c>bin/</c>, <c>obj/</c> and <c>*.Designer.cs</c>),
    /// one line each: "path: rule: detail", the path relative to <paramref name="repo"/> with forward slashes.
    /// </summary>
    public static List<string> Check(string repo)
    {
        var files = new List<(string Path, string Project, List<string> Namespaces, List<(string Name, bool TopLevel, string Access)> Types)>();
        foreach (var root in Roots)
        {
            var top = Path.Combine(repo, root);
            if (!Directory.Exists(top)) continue;
            foreach (var full in RepoPaths.SourceFiles(top, "*").Order(StringComparer.Ordinal))
            {
                var path = Path.GetRelativePath(repo, full).Replace('\\', '/');
                var parts = path.Split('/');
                var project = parts[0] == "tools" ? "tools/render-icon" : parts[0] + "/" + parts[1];
                if (path.EndsWith(".cs", StringComparison.Ordinal))
                {
                    try
                    {
                        var (namespaces, types) = Read(File.ReadAllText(full));
                        files.Add((path, project, namespaces, types));
                    }
                    catch (InvalidDataException exception) { throw new InvalidDataException($"{path}: {exception.Message}", exception); }
                }
                else if (path.EndsWith(".axaml", StringComparison.Ordinal))
                {
                    // A XAML file declares the type of its x:Class; resource dictionaries and styles declare none.
                    var match = XamlClass.Match(File.ReadAllText(full));
                    var name = match.Groups[1].Value;
                    var dot = name.LastIndexOf('.');
                    files.Add(match.Success
                        ? (path, project, [dot < 0 ? "" : name[..dot]], [(name[(dot + 1)..], true, "")])
                        : (path, project, [], []));
                }
            }
        }

        var typeNames = files.GroupBy(f => f.Project)
            .ToDictionary(grouping => grouping.Key, grouping => grouping.SelectMany(f => f.Types).Select(t => t.Name).ToHashSet(StringComparer.Ordinal));
        var problems = new List<string>();
        foreach (var (path, project, namespaces, types) in files)
        {
            void Problem(string rule, string detail) => problems.Add($"{path}: {rule}: {detail}");

            var parts = path.Split('/');
            var folders = parts[..^1];
            var folder = string.Join('/', folders);
            var file = parts[^1];
            var name = file.EndsWith(".axaml.cs", StringComparison.Ordinal) ? file[..^".axaml.cs".Length]
                : file.EndsWith(".cs", StringComparison.Ordinal) ? file[..^".cs".Length]
                : file[..^".axaml".Length];
            var topLevel = types.Where(t => t.TopLevel).Select(t => t.Name).ToList();

            foreach (var segment in folders)
            {
                if (segment.StartsWith("Backup", StringComparison.OrdinalIgnoreCase)
                    || IgnoredFolders.Contains(segment, StringComparer.OrdinalIgnoreCase))
                    Problem("no ignored folder name", segment);
            }

            if (topLevel.Count > 1) Problem("one type per file", string.Join(", ", topLevel));

            if (name.Contains('.'))
            {
                // Type.Part.cs only for the parts of ServerState in State/.
                var part = name[..name.IndexOf('.')] == "ServerState" && folder == "src/OVS.Server/State"
                    && topLevel.Count > 0 && topLevel.All(text => text == "ServerState");
                if (!part) Problem("file name = type name", $"{file} declares {(topLevel.Count == 0 ? "no type" : string.Join(", ", topLevel))}");
            }
            else if (topLevel.Count > 0 && !topLevel.Contains(name))
                Problem("file name = type name", $"{file} declares {string.Join(", ", topLevel)}");
            if (project == "src/OVS.Server" && topLevel.Contains("ServerState") && folder != "src/OVS.Server/State")
                Problem("ServerState parts in State/", folder);

            var expected = (parts[0] == "tools" ? "OVS.Tools" : parts[1]) + string.Concat(folders.Skip(2).Select(text => "." + text));
            if (topLevel.Count > 0 && namespaces.Count == 0) Problem("namespace = folder", $"no namespace, expected {expected}");
            foreach (var ns in namespaces.Where(ns => ns != expected)) Problem("namespace = folder", $"{ns}, expected {expected}");

            foreach (var (nested, _, access) in types.Where(t => !t.TopLevel && t.Access is not ("" or "private")))
                Problem("nested types only private", $"{nested} is {access}");

            if (parts[0] == "src")
            {
                if (folders.Contains("Views"))
                {
                    foreach (var type in topLevel.Where(text => !text.EndsWith("View", StringComparison.Ordinal) && text != "MainWindow"))
                        Problem("views end in View", type);
                }
                if (folders.Contains("ViewModels"))
                {
                    foreach (var type in topLevel.Where(text => !text.EndsWith("ViewModel", StringComparison.Ordinal)))
                        Problem("view models end in ViewModel", type);
                }
            }

            foreach (var segment in namespaces.SelectMany(ns => ns.Split('.')).Distinct().Where(typeNames[project].Contains))
                Problem("no namespace segment named like a type", segment);
        }
        return problems;
    }

    /// <summary>
    /// The namespaces (full names, also file-scoped ones) and the types a source file declares, each type with whether
    /// it is top-level (in a namespace or the file) or nested (in a type), and its access modifiers ("" when none).
    /// </summary>
    static (List<string> Namespaces, List<(string Name, bool TopLevel, string Access)> Types) Read(string text)
    {
        var namespaces = new List<string>();
        var types = new List<(string Name, bool TopLevel, string Access)>();
        // Open braces: 'n' a namespace, 't' a type body, 'o' any other block (method, property, initializer, lambda),
        // whose content is skipped. A block opened inside the parentheses or brackets of a declaration (an attribute
        // argument) gives that declaration back when it closes.
        var open = new Stack<(char Kind, string OuterNamespace, List<string>? Header)>();
        var current = "";
        var header = new List<string>();
        foreach (Match match in Token.Matches(Strip(text)))
        {
            var token = match.Value;
            var kind = open.Count == 0 ? 'n' : open.Peek().Kind;
            if (token == "}")
            {
                if (open.Count == 0) throw new InvalidDataException("scanner could not read the file: unbalanced braces");
                var (closed, outer, saved) = open.Pop();
                if (closed == 'n') current = outer;
                header = saved ?? [];
                continue;
            }
            if (kind == 'o')
            {
                if (token == "{") open.Push(('o', current, null));
                continue;
            }
            if (token is "{" or ";")
            {
                if (header.Count > 1 && header[0] == "namespace")
                {
                    var full = string.Concat(header.Skip(1));
                    namespaces.Add(current.Length == 0 ? full : current + "." + full);
                    if (token == "{") open.Push(('n', current, null));
                    current = namespaces[^1];
                }
                else if (Declaration(header) is (string name, string access))
                {
                    types.Add((name, kind == 'n', access));
                    if (token == "{") open.Push(('t', current, null));
                }
                else if (token == "{")
                {
                    var depth = header.Count(value => value is "(" or "[") - header.Count(value => value is ")" or "]");
                    open.Push(('o', current, depth > 0 ? header : null));
                }
                header = [];
                continue;
            }
            if (token == "namespace") header = []; // after an [assembly: ...] attribute, which ends without ";"
            header.Add(token);
        }
        // A misread string, comment or #if branch shifts the braces; fail loudly instead of passing a file unread.
        if (open.Count != 0) throw new InvalidDataException("scanner could not read the file: unbalanced braces");
        return (namespaces, types);
    }

    // The type a declaration header declares, or null: a type keyword before the first "(", "=", "=>", ":" or "where"
    // (so neither a constraint "where T : class" nor an initializer counts), outside attribute brackets, followed by a
    // name ("record" may be followed by "class" or "struct"; a delegate's name stands before its parameter list).
    static (string Name, string Access)? Declaration(List<string> header)
    {
        var modifiers = new List<string>();
        for (var index = 0; index < header.Count; index++)
        {
            var token = header[index];
            if (token == "[")
            {
                for (var depth = 0; index < header.Count; index++)
                {
                    if (header[index] == "[") depth++;
                    else if (header[index] == "]" && --depth == 0) break;
                }
                continue;
            }
            if (token is "(" or "=" or "=>" or ":" or "where") return null;
            if (token is "class" or "struct" or "interface" or "enum" or "record" or "delegate")
            {
                var next = index + 1;
                if (token == "record" && next < header.Count && header[next] is "class" or "struct") next++;
                var name = token == "delegate" ? DelegateName(header, next) : next < header.Count ? header[next] : null;
                if (name is null || !Identifier.IsMatch(name)) return null;
                var access = modifiers.Where(text => text is "public" or "private" or "protected" or "internal" or "file");
                return (name.TrimStart('@'), string.Join(' ', access));
            }
            modifiers.Add(token);
        }
        return null;
    }

    // "delegate* unmanaged<...>" is a function pointer, no type; otherwise the name before "(" or before "<T>(".
    static string? DelegateName(List<string> header, int start)
    {
        if (start >= header.Count || header[start] == "*") return null;
        var end = header.IndexOf("(", start);
        if (end < 0) return null;
        if (header[end - 1] == ">")
        {
            for (var depth = 0; end > start; end--)
            {
                if (header[end - 1] == ">") depth++;
                else if (header[end - 1] == "<" && --depth == 0) { end--; break; }
            }
        }
        return end - 1 >= start ? header[end - 1] : null;
    }

    /// <summary>
    /// <paramref name="text"/> with comments, strings (regular, verbatim, interpolated with their holes, raw), character
    /// literals and preprocessor lines replaced by a space each, so braces and keywords inside them do not count.
    /// </summary>
    static string Strip(string text)
    {
        var code = new StringBuilder(text.Length);
        var index = 0;
        Code(text, ref index, code, hole: false);
        return code.ToString();
    }

    // Copies code to `code` (skips it when null) up to the end of the text, or in an interpolation hole up to its
    // closing brace, which it leaves for the caller.
    static void Code(string text, ref int index, StringBuilder? code, bool hole)
    {
        var depth = 0;
        var lineStart = !hole;
        while (index < text.Length)
        {
            var character = text[index];
            if (character == '\n') lineStart = !hole;
            else if (lineStart && character == '#')
            {
                while (index < text.Length && text[index] != '\n') index++;
                continue;
            }
            else if (!char.IsWhiteSpace(character)) lineStart = false;

            var next = index + 1 < text.Length ? text[index + 1] : '\0';
            if (character == '/' && next == '/')
            {
                while (index < text.Length && text[index] != '\n') index++;
                continue;
            }
            if (character == '/' && next == '*')
            {
                var end = text.IndexOf("*/", index + 2, StringComparison.Ordinal);
                index = end < 0 ? text.Length : end + 2;
                code?.Append(' ');
                continue;
            }
            if (character == '\'')
            {
                var otherIndex = index + 1;
                if (otherIndex < text.Length && text[otherIndex] == '\\') otherIndex += 2;
                while (otherIndex < text.Length && text[otherIndex] != '\'') otherIndex++;
                index = otherIndex + 1;
                code?.Append(' ');
                continue;
            }
            if (character is '"' or '$' or '@' && StringStart(text, index))
            {
                SkipString(text, ref index);
                code?.Append(' ');
                continue;
            }
            if (hole && character == '}' && depth == 0) return;
            if (character == '{') depth++;
            else if (character == '}') depth--;
            code?.Append(character);
            index++;
        }
    }

    static bool StringStart(string text, int index)
    {
        while (index < text.Length && text[index] is '$' or '@') index++;
        return index < text.Length && text[index] == '"';
    }

    static void SkipString(string text, ref int index)
    {
        int dollars = 0, quotes = 0;
        var verbatim = false;
        for (; text[index] is '$' or '@'; index++)
        {
            if (text[index] == '$') dollars++;
            else verbatim = true;
        }
        // A verbatim string has one opening quote; @"""a" starts with an escaped quote, it is no raw string.
        if (verbatim) quotes = 1;
        else while (index + quotes < text.Length && text[index + quotes] == '"') quotes++;
        index += quotes;
        if (quotes == 2) return; // ""
        if (quotes >= 3)
        {
            // Raw: ends at a run of as many quotes; with n dollars, n braces open a hole (more are literal).
            while (index < text.Length)
            {
                var run = 0;
                if (text[index] == '"')
                {
                    while (index + run < text.Length && text[index + run] == '"') run++;
                    index += run;
                    if (run >= quotes) return;
                }
                else if (dollars > 0 && text[index] == '{')
                {
                    while (index + run < text.Length && text[index + run] == '{') run++;
                    index += run;
                    if (run >= dollars)
                    {
                        Code(text, ref index, null, hole: true);
                        index += dollars;
                    }
                }
                else index++;
            }
            return;
        }
        while (index < text.Length)
        {
            var character = text[index];
            var next = index + 1 < text.Length ? text[index + 1] : '\0';
            if (!verbatim && character == '\\') index += 2;
            else if (character == '"' && verbatim && next == '"') index += 2;
            else if (character == '"')
            {
                index++;
                return;
            }
            else if (dollars > 0 && character is '{' or '}' && next == character) index += 2;
            else if (dollars > 0 && character == '{')
            {
                index++;
                Code(text, ref index, null, hole: true);
                index++;
            }
            else index++;
        }
    }
}
