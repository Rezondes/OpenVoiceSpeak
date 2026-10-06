using System.Reflection;
using OVS.Tests.TestSupport;
using Xunit.Abstractions;

namespace OVS.Tests;

/// <summary>
/// Every test reference in the Tests sections of PLAN.md resolves (CODING-RULES.md §Tests): a method by reflection on
/// this assembly (in its class when one is named), a class alone by its name, a website reference by its file and text.
/// The parser and the resolver have their own tests on small inline plans and references.
/// </summary>
public sealed class PlanReferenceTests(ITestOutputHelper output)
{
    /// <summary>References that no test covers any more; PLAN.md keeps them as history, each with the reason.</summary>
    static readonly Dictionary<string, string> Uncovered = new()
    {
        ["UiSmokeTests > \"ChannelDialog_SameForCreateAndEdit\""] =
            "never written: no UI test opens the channel dialog in both modes; ChannelDialogViewModelTests checks title, button and fields per mode",
    };

    /// <summary>Spans in Tests sections that look like a test but are none, each with the reason.</summary>
    static readonly Dictionary<string, string> NoTest = new()
    {
        ["OVS_PORT=7200"] = "an environment variable in a Given line",
        ["$env:OVS_PORT=\"abc\"; dotnet run --project src/OVS.Server; $LASTEXITCODE"] = "the command of a manual check",
        ["--build-arg OVS_BUILD_TIME=..."] = "a docker build argument of a manual check",
        ["Update_Damaged"] = "a resource key in an Expected line",
        ["Dlg_MutedChannel"] = "a resource key in an Expected line",
        ["tests/OVS.Tests/Client/Baselines/"] = "the folder of the screenshot baselines",
    };

    [Fact]
    public void Plan_EveryTestReference_Resolves()
    {
        var root = RepoPaths.Root;
        var (references, ignored) = PlanReference.Parse(File.ReadAllText(Path.Combine(root, "PLAN.md")));
        var tests = FindTestMethods(typeof(PlanReferenceTests).Assembly);
        output.WriteLine($"{references.Count} references, ignored spans: " + string.Join(", ", ignored.Select(s => $"{s.Line} `{s.Span}`")));

        Assert.True(references.Count > 900, $"only {references.Count} references: did the package or Tests headings change?");
        Assert.Equal(NoTest.Keys.Order(), ignored.Select(s => s.Span).Distinct().Order());
        var problems = FindProblems(references, Uncovered, r => Resolves(r, tests, root));
        Assert.True(problems.Count == 0, $"{problems.Count} PLAN.md reference problems:\n" + string.Join("\n", problems));
    }

    [Fact]
    public void Parse_ClassAndQuotedMethod_GivesClassAndMethod()
    {
        var r = Assert.Single(Parse("1. `VoiceRelayTests > \"Hello_FromOtherAddress_EndpointNotChanged\"`"));
        Assert.Equal((5, 4, "VoiceRelayTests", "Hello_FromOtherAddress_EndpointNotChanged"), (r.Package, r.Line, r.Class, r.Method));
    }

    [Fact]
    public void Parse_CriterionAfterReference_IsRead()
    {
        var r = Assert.Single(Parse("1. `VoiceRelayTests > \"Hello_FromOtherAddress_EndpointNotChanged\"` (AC1)"));
        Assert.Equal([1], r.AcceptanceCriteria);
    }

    [Fact]
    public void Parse_FileWithTheory_GivesClassOfFileName()
    {
        var r = Assert.Single(Parse("2. `PermissionRulesTests.cs > [Theory] \"Effective_Cases\"` (AC6, AC7)"));
        Assert.Equal(("PermissionRulesTests", "Effective_Cases"), (r.Class, r.Method));
        Assert.Equal([6, 7], r.AcceptanceCriteria);
    }

    [Fact]
    public void Parse_EnglishRange_GivesEveryCriterion()
    {
        var r = Assert.Single(Parse("1. `ItemMotionTests > \"Simplified_Jumps\"` (AC2 to AC4)"));
        Assert.Equal([2, 3, 4], r.AcceptanceCriteria);
    }

    [Fact]
    public void Parse_HeaderWithBullets_BulletsTakeHeaderClassUnlessTheirLineNamesOne()
    {
        var refs = Parse("1. `DataStoreTests.cs`:\n   - `\"Load_MissingFile_CreatesDefaults\"` (AC1)\n   - `BackupTests > \"Restore_Invalid_NothingChanged\"`, `\"Restore_Twice_Same\"`\n   - `[Theory] \"Create_InvalidName_Error\"` (AC2 bis AC4)\n2. `\"Read_EndBetweenFrames_ReturnsNull\"` (AC5)");
        Assert.Equal(
            [("DataStoreTests", null), ("DataStoreTests", "Load_MissingFile_CreatesDefaults"), ("BackupTests", "Restore_Invalid_NothingChanged"),
                ("BackupTests", "Restore_Twice_Same"), ("DataStoreTests", "Create_InvalidName_Error"), ("DataStoreTests", "Read_EndBetweenFrames_ReturnsNull")],
            refs.Select(r => (r.Class, r.Method)));
        Assert.Equal([2, 3, 4], refs[4].AcceptanceCriteria);
    }

    [Fact]
    public void Parse_QuotedMethodWithoutHeader_HasNoClass()
    {
        var r = Assert.Single(Parse("4. `\"Read_EndInsideFrame_ThrowsEndOfStream\"` und mehr (AC4)"));
        Assert.Equal((null, "Read_EndInsideFrame_ThrowsEndOfStream"), (r.Class, r.Method));
        Assert.Equal([4], r.AcceptanceCriteria);
    }

    [Fact]
    public void Parse_ClassAloneAndDotted_GiveClassAndMethod()
    {
        var refs = Parse("7. `LocalizationTests`, `ResponsiveTests.Dialogs_FitAt360` deckt AC2 und AC5 ab");
        Assert.Equal([("LocalizationTests", null), ("ResponsiveTests", "Dialogs_FitAt360")], refs.Select(r => (r.Class, r.Method)));
        Assert.All(refs, r => Assert.Equal([2, 5], r.AcceptanceCriteria));
    }

    [Fact]
    public void Parse_WebsiteFileAndText_GivesFileAndText()
    {
        var r = Assert.Single(Parse("1. `website/src/App.test.tsx > \"Hero shows version\"` (AC1)"));
        Assert.Equal(("website/src/App.test.tsx", "Hero shows version", null, null), (r.File, r.Text, r.Class, r.Method));
    }

    [Fact]
    public void Parse_NoteLine_IsSkippedAlsoWhenIndented()
    {
        var refs = Parse("1. `BTests > \"B_C\"`\n(as built: `CTests > \"C_D\"`)\n   - `\"B_D\"`\n     (Hinweis zur Referenz: `DTests > \"D_E\"`)");
        Assert.Equal(["B_C", "B_D"], refs.Select(r => r.Method));
    }

    [Fact]
    public void Parse_ReplacedTest_IsNoReference()
    {
        var refs = Parse("1. `BTests > \"B_C\"` ersetzt `\"Old_Name\"`\n2. `BTests > \"B_D\"` replaces `BTests.Old_Other`");
        Assert.Equal(["B_C", "B_D"], refs.Select(r => r.Method));
    }

    [Fact]
    public void Parse_OtherSections_AreSkipped()
    {
        var plan = "## Package 3: X\n\n### Acceptance Criteria\n\n- `ATests > \"A_B\"`\n\n### Tests (TDD)\n\n1. `BTests > \"B_C\"`\n\n## Umsetzungsstand\n\n### Tests (TDD)\n\n1. `DTests > \"D_E\"`\n";
        var r = Assert.Single(PlanReference.Parse(plan).References);
        Assert.Equal((3, 9, "BTests"), (r.Package, r.Line, r.Class));
    }

    [Fact]
    public void Parse_UnknownForm_ReportsSpanAsIgnored()
    {
        var (references, ignored) = PlanReference.Parse("## Package 5: X\n\n### Tests (TDD)\n\n`DebugApiTests.cs`: `MissingHeader_Forbidden` (AC1)\n`Given_A_B` and `plain` text\n- Gegeben: `Ping`\n");
        Assert.Equal([("DebugApiTests", null)], references.Select(r => (r.Class, r.Method)));
        Assert.Equal([(5, "MissingHeader_Forbidden"), (6, "Given_A_B")], ignored);
    }

    [Fact]
    public void Resolves_KnownClassAndMethod_True() =>
        Assert.True(Resolves(CreateReference("PlanReferenceTests", nameof(Resolves_KnownClassAndMethod_True))));

    [Fact]
    public void Resolves_MethodMissingInExistingClass_False() =>
        Assert.False(Resolves(CreateReference("PlanReferenceTests", "No_Such_Test")));

    [Fact]
    public void Resolves_MethodOnlyInOtherClass_False() =>
        Assert.False(Resolves(CreateReference("SourceLayoutTests", nameof(Resolves_MethodOnlyInOtherClass_False))));

    [Fact]
    public void Resolves_MissingClass_False() =>
        Assert.False(Resolves(CreateReference("NoSuchTests", null)));

    [Fact]
    public void Resolves_WebsiteFileWithoutText_False() =>
        Assert.False(Resolves(new PlanReference(1, 1, null, null, "CODING-RULES.md", "no such text in the rules", [])));

    [Fact]
    public void Problems_StaleUncoveredEntry_Reported()
    {
        var resolving = CreateReference("PlanReferenceTests", nameof(Problems_StaleUncoveredEntry_Reported));
        var problems = FindProblems([resolving], new Dictionary<string, string> { [resolving.Target] = "reason" }, _ => true);
        Assert.Equal([$"stale Uncovered entry (resolves or is gone): {resolving.Target}"], problems);
    }

    /// <summary>The names of the test methods (Fact, Theory and their subclasses) per class name, for every type of <paramref name="assembly"/>.</summary>
    static Dictionary<string, HashSet<string>> FindTestMethods(Assembly assembly) =>
        assembly.GetTypes().GroupBy(t => t.Name).ToDictionary(g => g.Key, g => g
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            .Where(m => m.IsDefined(typeof(FactAttribute), true)).Select(m => m.Name).ToHashSet());

    /// <summary>
    /// Whether <paramref name="reference"/> names something that exists: a website file under <paramref name="root"/> that
    /// contains the text, a class, a test method of the named class, or a test method of any class.
    /// </summary>
    static bool Resolves(PlanReference reference, Dictionary<string, HashSet<string>> testMethods, string root) => reference switch
    {
        { File: { } file } => File.Exists(Path.Combine(root, file)) && File.ReadAllText(Path.Combine(root, file)).Contains(reference.Text!),
        { Class: null } => testMethods.Values.Any(methods => methods.Contains(reference.Method!)),
        { Method: null } => testMethods.ContainsKey(reference.Class),
        _ => testMethods.TryGetValue(reference.Class, out var methods) && methods.Contains(reference.Method),
    };

    /// <summary>
    /// One line per reference that does not resolve and is not on <paramref name="uncovered"/>, and per entry of
    /// <paramref name="uncovered"/> that no unresolved reference matches any more.
    /// </summary>
    static List<string> FindProblems(IEnumerable<PlanReference> references, IReadOnlyDictionary<string, string> uncovered, Func<PlanReference, bool> resolves)
    {
        var broken = references.Where(r => !resolves(r)).ToList();
        var problems = uncovered.Keys.Except(broken.Select(r => r.Target)).Select(k => $"stale Uncovered entry (resolves or is gone): {k}").ToList();
        problems.AddRange(broken.Where(r => !uncovered.ContainsKey(r.Target)).Select(r => $"does not resolve: {r}"));
        return problems;
    }

    static bool Resolves(PlanReference reference) => Resolves(reference, FindTestMethods(typeof(PlanReferenceTests).Assembly), RepoPaths.Root);

    static PlanReference CreateReference(string type, string? method) => new(1, 1, type, method, null, null, []);

    /// <summary>The references of <paramref name="tests"/> placed in the Tests section of Package 5, whose first line is line 4.</summary>
    static List<PlanReference> Parse(string tests) => PlanReference.Parse("## Package 5: X\n\n### Tests (TDD)\n" + tests).References;
}
