using System.Globalization;
using System.Text.RegularExpressions;

namespace OVS.Tests;

/// <summary>
/// One test reference in a Tests section of a package of PLAN.md or docs/archive/PLAN-ARCHIVE.md (CODING-RULES-OVS.md §Tests): a test
/// method with or without its class, a test class alone, or a website file and a text it contains; with the acceptance criteria named after it
/// on its line. <see cref="Parse(string, string)"/> reads them; <see cref="PlanReferenceTests"/> resolves them against the test assembly.
/// </summary>
sealed record PlanReference(int Package, int Line, string? Class, string? Method, string? File, string? Text, IReadOnlyList<int> AcceptanceCriteria)
{
    const string CriteriaConnector = @"\s*(,|bis|to|und|and)\s*";

    static readonly Regex PackageHeading = new(@"^## Package (\d+):");
    static readonly Regex Span = new("`([^`]+)`");
    static readonly Regex Website = new(@"^(?<file>[\w.\-]+(?:/[\w.\-]+)+) > ""(?<text>[^""]+)""$");
    static readonly Regex ClassAndQuotedMethod = new(@"^(?<class>\w+?)(?:\.cs)? > (?:\[Theory\] )?""(?<method>\w+)""$");
    static readonly Regex ClassAlone = new(@"^(?<class>\w+Tests)(?:\.cs)?$");
    static readonly Regex Dotted = new(@"^(?<class>\w+Tests)\.(?<method>\w+)$");
    static readonly Regex QuotedMethodAlone = new(@"^(?:\[Theory\] )?""(?<method>\w*_\w*)""$");
    static readonly Regex RustFileAndTest = new(@"^(?<file>[\w\-]+(?:/[\w\-]+)*\.rs) > (?<method>[a-z][a-z0-9_]*)$"); // Package 172
    static readonly Regex RustTestAlone = new(@"^[a-z][a-z0-9]*(?:_[a-z0-9]+)+$");
    static readonly Regex LooksLikeTest = new(@"\w_\w|Tests\b");
    static readonly Regex CriteriaList = new($@"AC\d+(?:{CriteriaConnector}AC\d+)*");
    static readonly Regex CriteriaPart = new($@"(?:^|{CriteriaConnector})AC(\d+)");

    /// <summary>
    /// Every reference in the <c>### Tests</c> sections of the <c>## Package N</c> chapters of <paramref name="plan"/>,
    /// and the spans there that look like a reference (a name with an underscore or ending in Tests) but are no form the
    /// parser knows, with their line (test commands, <c>dotnet ...</c> and <c>cargo ...</c>, are not counted). Package 172:
    /// <c>file.rs > name</c> is a test of the native library, and a bare snake-case name takes the <c>.rs</c> file named
    /// last, by the same context rule as a method takes its class. Lines starting with "(" (after
    /// indentation) are notes, such as "(as built: ...)", and not read. A method without a class takes the class named
    /// before it on its line, else the class last named on a top-level line of its section (a header or a numbered item;
    /// a bullet below a header does not change it), if any. A test named right after "ersetzt" or "replaces" is the
    /// history of a replaced test and no reference.
    /// </summary>
    public static PlanParse Parse(string plan) => Parse(plan, PlanFiles.Open);

    /// <summary><see cref="Parse(string)"/> of the plan file named <paramref name="planFile"/>, whose name the references carry.</summary>
    public static PlanParse Parse(string plan, string planFile)
    {
        var references = new List<PlanReference>();
        var ignored = new List<(int, string)>();
        int? package = null;
        var inTests = false;
        string? context = null;
        var lines = plan.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].TrimEnd('\r');
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                var heading = PackageHeading.Match(line);
                package = heading.Success ? int.Parse(heading.Groups[1].Value, CultureInfo.InvariantCulture) : null;
                inTests = false;
                continue;
            }
            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                inTests = package is not null && line.StartsWith("### Tests", StringComparison.Ordinal);
                context = null;
                continue;
            }
            if (!inTests || line.TrimStart().StartsWith('(')) continue;
            var lineContext = context;
            foreach (Match span in Span.Matches(line))
            {
                var text = span.Groups[1].Value;
                if (line[..span.Index].EndsWith("ersetzt ", StringComparison.Ordinal) || line[..span.Index].EndsWith("replaces ", StringComparison.Ordinal)) continue;
                if (Read(text, ref lineContext) is not { } target)
                {
                    if (LooksLikeTest.IsMatch(text) && !text.StartsWith("dotnet ", StringComparison.Ordinal) && !text.StartsWith("cargo ", StringComparison.Ordinal))
                        ignored.Add((index + 1, text));
                    continue;
                }
                references.Add(new(package!.Value, index + 1, target.Class, target.Method, target.File, target.Text,
                    ReadCriteria(line[(span.Index + span.Length)..])) { PlanFile = planFile });
            }
            if (line.Length > 0 && !char.IsWhiteSpace(line[0])) context = lineContext;
        }
        return new(references, ignored);
    }

    /// <summary>What one backtick span references, or null when it is no reference.</summary>
    static SpanTarget? Read(string span, ref string? context)
    {
        if (Website.Match(span) is { Success: true } web) return new(null, null, web.Groups["file"].Value, web.Groups["text"].Value);
        if (ClassAndQuotedMethod.Match(span) is { Success: true } flat) return new(context = flat.Groups["class"].Value, flat.Groups["method"].Value, null, null);
        if (ClassAlone.Match(span) is { Success: true } alone) return new(context = alone.Groups["class"].Value, null, null, null);
        if (Dotted.Match(span) is { Success: true } dotted) return new(context = dotted.Groups["class"].Value, dotted.Groups["method"].Value, null, null);
        if (QuotedMethodAlone.Match(span) is { Success: true } bare) return new(context, bare.Groups["method"].Value, null, null);
        if (RustFileAndTest.Match(span) is { Success: true } rust) return new(null, rust.Groups["method"].Value, context = rust.Groups["file"].Value, null);
        if (RustTestAlone.IsMatch(span) && context is { } file && file.EndsWith(".rs", StringComparison.Ordinal)) return new(null, span, file, null);
        return null;
    }

    /// <summary>The criteria of the first list in <paramref name="rest"/>: "AC6, AC7", "AC2 bis AC4" (2, 3, 4), "AC2 und AC5".</summary>
    static int[] ReadCriteria(string rest)
    {
        var list = CriteriaList.Match(rest);
        if (!list.Success) return [];
        var result = new List<int>();
        foreach (Match part in CriteriaPart.Matches(list.Value))
        {
            var number = int.Parse(part.Groups[2].Value, CultureInfo.InvariantCulture);
            if (part.Groups[1].Value is "bis" or "to" && result.Count > 0)
                for (var count = result[^1] + 1; count < number; count++) result.Add(count);
            result.Add(number);
        }
        return [.. result];
    }

    /// <summary>What is referenced, in the PLAN.md form: <c>Class > "Method"</c>, <c>"Method"</c>, <c>Class</c> or <c>file > "text"</c>.</summary>
    public string Target =>
        IsRust ? $"{File} > {Method}" : File is not null ? $"{File} > \"{Text}\"" :Method is null ? Class! : Class is null ? $"\"{Method}\"" : $"{Class} > \"{Method}\"";

    /// <summary>Package 172: a test of the native library, <c>file.rs > name</c>, the file under native/ovs_audio.</summary>
    public bool IsRust => File is not null && File.EndsWith(".rs", StringComparison.Ordinal);

    /// <summary>The plan file the reference stands in, <see cref="PlanFiles.Open"/> unless the parse named another.</summary>
    public string PlanFile { get; init; } = PlanFiles.Open;

    public override string ToString() => $"{PlanFile}:{Line} (Package {Package}): {Target}";

    sealed record SpanTarget(string? Class, string? Method, string? File, string? Text);
}
