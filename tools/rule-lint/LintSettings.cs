using System.Text.Json;
using System.Text.Json.Serialization;

namespace RuleLint;

/// <summary>
/// The project's settings of the rule checker, read from <c>rule-lint.json</c> in the repository root. The defaults are
/// the general rules of CODING-RULES.md; a project lists its roots and its exceptions (CODING-RULES-&lt;Project&gt;.md).
/// </summary>
internal sealed class LintSettings
{
    /// <summary>The file name the checker looks for in the repository root.</summary>
    public const string FileName = "rule-lint.json";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>The project folders the code rules apply to (rule scope).</summary>
    public List<SourceRoot> Roots { get; init; } = [];

    /// <summary>Folder names skipped at any depth (build output, tools' caches).</summary>
    public List<string> ExcludedFolders { get; init; } = [".git", ".vs", "bin", "obj", "node_modules", "TestResults"];

    /// <summary>The extensions of the text files the em dash rule reads anywhere in the repository (rule 18).</summary>
    public List<string> TextExtensions { get; init; } =
        [".cs", ".axaml", ".razor", ".md", ".resx", ".json", ".yml", ".yaml", ".props", ".targets", ".csproj", ".slnx", ".xml", ".txt", ".html", ".css", ".js", ".ps1", ".sh"];

    /// <summary>Folder names the <c>.gitignore</c> ignores at any depth (rule 7), compared ignoring case.</summary>
    public List<string> IgnoredFolderNames { get; init; } = ["Log", "Logs", "Debug", "Release", "bin", "obj", "x64", "artifacts", "publish", "Generated_Code"];

    /// <summary>Folder name prefixes the <c>.gitignore</c> ignores (rule 7, <c>Backup*</c>), compared ignoring case.</summary>
    public List<string> IgnoredFolderPrefixes { get; init; } = ["Backup"];

    /// <summary>The types allowed to span several files as <c>Type.Part.cs</c> (rule 4).</summary>
    public List<string> PartialTypes { get; init; } = [];

    /// <summary>The views that keep their name without the <c>View</c> suffix (rule 10).</summary>
    public List<string> ViewExceptions { get; init; } = ["App", "MainWindow"];

    /// <summary>The fixed XAML prefix of each namespace, prefix to namespace (rule 13).</summary>
    public Dictionary<string, string> XamlPrefixes { get; init; } = [];

    /// <summary>The XAML attributes whose literal value is user-facing text (rule 12).</summary>
    public List<string> XamlTextAttributes { get; init; } = ["Text", "Content", "Header", "Title", "ToolTip.Tip", "Watermark", "PlaceholderText"];

    /// <summary>The banned abbreviations of names (rule 20), compared exactly.</summary>
    public List<string> BannedNames { get; init; } = ["vm", "ct", "cts", "ms", "ex", "ctx", "cfg", "msg", "btn"];

    /// <summary>The bag name endings of types (rule 15).</summary>
    public List<string> BagSuffixes { get; init; } = ["Helper", "Helpers", "Util", "Utils", "Utility", "Utilities", "Manager", "Common", "Misc"];

    /// <summary>The words a type name may hold only with a subject (rule 15).</summary>
    public List<string> SubjectWords { get; init; } = ["Service", "Info", "Data"];

    /// <summary>The composition root files, relative to the repository (rule 14, Async rule 6).</summary>
    public List<string> CompositionRoots { get; init; } = [];

    /// <summary>The folders the one-clock rule applies to (Async rule 6); empty means every production root.</summary>
    public List<string> ClockFolders { get; init; } = [];

    /// <summary>The baseline file of known findings, relative to the repository.</summary>
    public string BaselineFile { get; init; } = "rule-lint.baseline.json";

    /// <summary>The line count above which a type is a single-responsibility red flag.</summary>
    public int MaxTypeLines { get; init; } = 400;

    /// <summary>The line count above which a method is a single-responsibility red flag.</summary>
    public int MaxMethodLines { get; init; } = 40;

    /// <summary>The settings in <paramref name="path"/>.</summary>
    public static LintSettings Load(string path) => Parse(File.ReadAllText(path));

    /// <summary>The settings in the JSON <paramref name="json"/>.</summary>
    public static LintSettings Parse(string json) =>
        JsonSerializer.Deserialize<LintSettings>(json, JsonOptions) ?? throw new InvalidDataException("The rule-lint settings are empty.");
}
