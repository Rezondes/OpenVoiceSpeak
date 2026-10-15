using System.Reflection;
using OVS.Tests.TestSupport;
using Xunit.Abstractions;

namespace OVS.Tests;

/// <summary>
/// Every test reference in the Tests sections of PLAN.md resolves (CODING-RULES-OVS.md §Tests): a method by reflection on
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
        ["LevelDbReaderTests > \"Read_LogAndTable_ReturnsLatestValuePerKey\""] = "planned in Package 171, written when it is built",
        ["LevelDbReaderTests > \"Read_DamagedBlock_SkippedNotThrown\""] = "planned in Package 171, written when it is built",
        ["LevelDbReaderTests > \"Read_LockedFolder_ReadsFromACopy\""] = "planned in Package 171, written when it is built",
        ["DiscordAudioImportTests > \"Read_Fixture_AudioValuesFound\""] = "planned in Package 171, written when it is built",
        ["DiscordAudioImportTests > \"Read_NoInstallation_NoneFound\""] = "planned in Package 171, written when it is built",
        ["DiscordAudioImportTests > \"Read_UnknownFormat_ReasonReported\""] = "planned in Package 171, written when it is built",
        ["ImportMappingTests > \"Map_EveryValue_OntoOvsScales\""] = "planned in Package 171, written when it is built",
        ["ImportMappingTests > \"Map_EqualValues_NoChange\""] = "planned in Package 171, written when it is built",
        ["ImportMappingTests > \"Map_UnknownDevice_LeftOutWithNote\""] = "planned in Package 171, written when it is built",
        ["ImportMappingTests > \"Map_UnsupportedValue_LeftOut\""] = "planned in Package 171, written when it is built",
        ["ImportDialogTests > \"Rows_Shown_AllCheckedByDefault\""] = "planned in Package 171, written when it is built",
        ["ImportDialogTests > \"Uncheck_Row_ThatSettingKept\""] = "planned in Package 171, written when it is built",
        ["ImportDialogTests > \"Nothing_ToChange_SaysSo\""] = "planned in Package 171, written when it is built",
        ["ImportDialogTests > \"Keyboard_SpaceAndEnter_ToggleAndTakeOver\""] = "planned in Package 171, written when it is built",
        ["SettingsTests > \"ImportFromDiscord_Checked_SetOnPageNotSaved\""] = "planned in Package 171, written when it is built",
        ["SettingsTests > \"ImportFromDiscord_Cancelled_NothingChanges\""] = "planned in Package 171, written when it is built",
        ["ResponsiveTests > \"ImportDialog_At360_Fits\""] = "planned in Package 171, written when it is built",
        ["JoinLinkTests > \"Parse_HostAndPort_Accepted\""] = "planned in Package 170, written when it is built",
        ["JoinLinkTests > \"Parse_Ipv6InBrackets_Accepted\""] = "planned in Package 170, written when it is built",
        ["JoinLinkTests > \"Parse_PathQueryUserInfo_Refused\""] = "planned in Package 170, written when it is built",
        ["JoinLinkTests > \"Parse_PortOutOfRange_Refused\""] = "planned in Package 170, written when it is built",
        ["JoinLinkTests > \"Build_WebLink\""] = "planned in Package 170, written when it is built",
        ["SingleInstanceTests > \"Second_HandsLinkOver_Ends\""] = "planned in Package 170, written when it is built",
        ["ConnectionViewModelTests > \"Link_OpensDialogFilled_NoConnect\""] = "planned in Package 170, written when it is built",
    };

    /// <summary>N20: checked criteria no test can check and without a manual marker, each with the reason.</summary>
    static readonly Dictionary<string, string> NoTestPossible = new()
    {
        ["Package 1 AC1"] = "the build itself: every test run builds with TreatWarningsAsErrors",
        ["Package 1 AC4"] = "what git status shows comes from .gitignore, not from code",
        ["Package 5 AC2"] = "a running Docker container",
        ["Package 5 AC3"] = "a running Docker container",
        ["Package 5 AC4"] = "a running Docker container",
        ["Package 5 AC5"] = "a running Docker container",
        ["Package 5 AC6"] = "a running Docker container",
        ["Package 19 AC1"] = "a multi-platform Docker build",
        ["Package 19 AC2"] = "an arm64 Docker host or QEMU",
        ["Package 19 AC3"] = "a Windows PC without .NET",
        ["Package 19 AC4"] = "what the README says",
        ["Package 27 AC3"] = "moving and maximizing the window is the window manager's",
        ["Package 28 AC4"] = "the logo's contrast, measured once with a contrast tool",
        ["Package 42 AC4"] = "the GitHub workflow, checked by its runs",
        ["Package 42 AC5"] = "the GitHub workflow, checked by its runs",
        ["Package 44 AC1"] = "the GitHub workflow, checked by its runs",
        ["Package 44 AC2"] = "the published image",
        ["Package 44 AC3"] = "a Docker compose run",
        ["Package 44 AC4"] = "what the README says",
        ["Package 45 AC6"] = "a limit of that package's scope, not a behaviour",
        ["Package 49 AC5"] = "the GitHub workflow of the website, checked by its runs",
        ["Package 58 AC2"] = "the whole suite staying green",
        ["Package 63 AC1"] = "the process-wide exception handlers of Program.Main, which end the process",
        ["Package 63 AC2"] = "an investigation, its result recorded in A77",
        ["Package 140 AC5"] = "a timing measured once on the dev machine, in the notes",
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
        output.WriteLine($"{references.Count} references, ignored spans: " + string.Join(", ", ignored.Select(value => $"{value.Line} `{value.Span}`")));

        Assert.True(references.Count > 900, $"only {references.Count} references: did the package or Tests headings change?");
        Assert.Equal(NoTest.Keys.Order(), ignored.Select(value => value.Span).Distinct().Order());
        var problems = FindProblems(references, Uncovered, planReference => Resolves(planReference, tests, root));
        Assert.True(problems.Count == 0, $"{problems.Count} PLAN.md reference problems:\n" + string.Join("\n", problems));
    }

    /// <summary>
    /// N20: every checked criterion carries a manual marker, is named by a resolving reference of its package, or stands
    /// here with the reason no test can check it.
    /// </summary>
    [Fact]
    public void Plan_EveryCheckedCriterion_CoveredMarkedOrExempt()
    {
        var root = RepoPaths.Root;
        var plan = File.ReadAllText(Path.Combine(root, "PLAN.md"));
        var tests = FindTestMethods(typeof(PlanReferenceTests).Assembly);
        var covered = PlanReference.Parse(plan).References.Where(planReference => Resolves(planReference, tests, root))
            .SelectMany(planReference => planReference.AcceptanceCriteria.Select(number => $"Package {planReference.Package} AC{number}")).ToHashSet();
        var criteria = PlanCriterion.Parse(plan);
        Assert.True(criteria.Count > 700, $"only {criteria.Count} checked criteria: did the package headings or the AC form change?");

        var problems = FindUncovered(criteria, covered, NoTestPossible);
        Assert.True(problems.Count == 0, $"{problems.Count} criterion problems:\n" + string.Join("\n", problems));
    }

    [Fact]
    public void ParseCriteria_CheckedUncheckedAndMarked_OnlyCheckedWithTheirMarker()
    {
        var criteria = PlanCriterion.Parse("## Package 7: X\n\n- [x] AC1: a\n- [ ] AC2: b\n- [x] AC3 (manuell): c\n- [x] AC4 (spike): d\n## Other\n- [x] AC9: e");
        Assert.Equal([(7, 1, 3, false), (7, 3, 5, true), (7, 4, 6, true)],
            criteria.Select(criterion => (criterion.Package, criterion.Number, criterion.Line, criterion.IsManual)));
    }

    [Fact]
    public void FindUncovered_UncoveredStaleAndMarked_ReportsOnlyTheFirstTwo()
    {
        PlanCriterion[] criteria = [new(1, 1, 10, false), new(1, 2, 11, false), new(1, 3, 12, true), new(1, 4, 13, false)];
        var problems = FindUncovered(criteria, new HashSet<string> { "Package 1 AC1" }, new Dictionary<string, string> { ["Package 1 AC4"] = "reason", ["Package 9 AC9"] = "gone" });
        Assert.Equal(["stale exemption (covered, marked or gone): Package 9 AC9", "not covered: PLAN.md:11 Package 1 AC2"], problems);
    }

    [Fact]
    public void Parse_ClassAndQuotedMethod_GivesClassAndMethod()
    {
        var planReference = Assert.Single(Parse("1. `VoiceRelayTests > \"Hello_FromOtherAddress_EndpointNotChanged\"`"));
        Assert.Equal((5, 4, "VoiceRelayTests", "Hello_FromOtherAddress_EndpointNotChanged"), (planReference.Package, planReference.Line, planReference.Class, planReference.Method));
    }

    [Fact]
    public void Parse_CriterionAfterReference_IsRead()
    {
        var planReference = Assert.Single(Parse("1. `VoiceRelayTests > \"Hello_FromOtherAddress_EndpointNotChanged\"` (AC1)"));
        Assert.Equal([1], planReference.AcceptanceCriteria);
    }

    [Fact]
    public void Parse_FileWithTheory_GivesClassOfFileName()
    {
        var planReference = Assert.Single(Parse("2. `PermissionRulesTests.cs > [Theory] \"Effective_Cases\"` (AC6, AC7)"));
        Assert.Equal(("PermissionRulesTests", "Effective_Cases"), (planReference.Class, planReference.Method));
        Assert.Equal([6, 7], planReference.AcceptanceCriteria);
    }

    [Fact]
    public void Parse_EnglishRange_GivesEveryCriterion()
    {
        var planReference = Assert.Single(Parse("1. `ItemMotionTests > \"Simplified_Jumps\"` (AC2 to AC4)"));
        Assert.Equal([2, 3, 4], planReference.AcceptanceCriteria);
    }

    [Fact]
    public void Parse_HeaderWithBullets_BulletsTakeHeaderClassUnlessTheirLineNamesOne()
    {
        var refs = Parse("1. `DataStoreTests.cs`:\n   - `\"Load_MissingFile_CreatesDefaults\"` (AC1)\n   - `BackupTests > \"Restore_Invalid_NothingChanged\"`, `\"Restore_Twice_Same\"`\n   - `[Theory] \"Create_InvalidName_Error\"` (AC2 bis AC4)\n2. `\"Read_EndBetweenFrames_ReturnsNull\"` (AC5)");
        Assert.Equal(
            [("DataStoreTests", null), ("DataStoreTests", "Load_MissingFile_CreatesDefaults"), ("BackupTests", "Restore_Invalid_NothingChanged"),
                ("BackupTests", "Restore_Twice_Same"), ("DataStoreTests", "Create_InvalidName_Error"), ("DataStoreTests", "Read_EndBetweenFrames_ReturnsNull")],
            refs.Select(planReference => (planReference.Class, planReference.Method)));
        Assert.Equal([2, 3, 4], refs[4].AcceptanceCriteria);
    }

    [Fact]
    public void Parse_QuotedMethodWithoutHeader_HasNoClass()
    {
        var planReference = Assert.Single(Parse("4. `\"Read_EndInsideFrame_ThrowsEndOfStream\"` und mehr (AC4)"));
        Assert.Equal((null, "Read_EndInsideFrame_ThrowsEndOfStream"), (planReference.Class, planReference.Method));
        Assert.Equal([4], planReference.AcceptanceCriteria);
    }

    [Fact]
    public void Parse_ClassAloneAndDotted_GiveClassAndMethod()
    {
        var refs = Parse("7. `LocalizationTests`, `ResponsiveTests.Dialogs_FitAt360` deckt AC2 und AC5 ab");
        Assert.Equal([("LocalizationTests", null), ("ResponsiveTests", "Dialogs_FitAt360")], refs.Select(planReference => (planReference.Class, planReference.Method)));
        Assert.All(refs, planReference => Assert.Equal([2, 5], planReference.AcceptanceCriteria));
    }

    [Fact]
    public void Parse_WebsiteFileAndText_GivesFileAndText()
    {
        var planReference = Assert.Single(Parse("1. `website/src/App.test.tsx > \"Hero shows version\"` (AC1)"));
        Assert.Equal(("website/src/App.test.tsx", "Hero shows version", null, null), (planReference.File, planReference.Text, planReference.Class, planReference.Method));
    }

    [Fact]
    public void Parse_NoteLine_IsSkippedAlsoWhenIndented()
    {
        var refs = Parse("1. `BTests > \"B_C\"`\n(as built: `CTests > \"C_D\"`)\n   - `\"B_D\"`\n     (Hinweis zur Referenz: `DTests > \"D_E\"`)");
        Assert.Equal(["B_C", "B_D"], refs.Select(planReference => planReference.Method));
    }

    [Fact]
    public void Parse_ReplacedTest_IsNoReference()
    {
        var refs = Parse("1. `BTests > \"B_C\"` ersetzt `\"Old_Name\"`\n2. `BTests > \"B_D\"` replaces `BTests.Old_Other`");
        Assert.Equal(["B_C", "B_D"], refs.Select(planReference => planReference.Method));
    }

    [Fact]
    public void Parse_OtherSections_AreSkipped()
    {
        var plan = "## Package 3: X\n\n### Acceptance Criteria\n\n- `ATests > \"A_B\"`\n\n### Tests (TDD)\n\n1. `BTests > \"B_C\"`\n\n## Umsetzungsstand\n\n### Tests (TDD)\n\n1. `DTests > \"D_E\"`\n";
        var planReference = Assert.Single(PlanReference.Parse(plan).References);
        Assert.Equal((3, 9, "BTests"), (planReference.Package, planReference.Line, planReference.Class));
    }

    [Fact]
    public void Parse_UnknownForm_ReportsSpanAsIgnored()
    {
        var (references, ignored) = PlanReference.Parse("## Package 5: X\n\n### Tests (TDD)\n\n`DebugApiTests.cs`: `MissingHeader_Forbidden` (AC1)\n`Given_A_B` and `plain` text\n- Gegeben: `Ping`\n");
        Assert.Equal([("DebugApiTests", null)], references.Select(planReference => (planReference.Class, planReference.Method)));
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
        assembly.GetTypes().GroupBy(candidate => candidate.Name).ToDictionary(grouping => grouping.Key, grouping => grouping
            .SelectMany(candidate => candidate.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            .Where(methodInfo => methodInfo.IsDefined(typeof(FactAttribute), true)).Select(methodInfo => methodInfo.Name).ToHashSet());

    /// <summary>
    /// Whether <paramref name="reference"/> names something that exists: a website file under <paramref name="root"/> that
    /// contains the text, a class, a test method of the named class, or a test method of any class.
    /// </summary>
    static bool Resolves(PlanReference reference, Dictionary<string, HashSet<string>> testMethods, string root) => reference switch
    {
        { File: { } file } => File.Exists(Path.Combine(root, file)) && File.ReadAllText(Path.Combine(root, file)).Contains(reference.Text!, StringComparison.Ordinal),
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
        var broken = references.Where(planReference => !resolves(planReference)).ToList();
        var problems = uncovered.Keys.Except(broken.Select(planReference => planReference.Target)).Select(key => $"stale Uncovered entry (resolves or is gone): {key}").ToList();
        problems.AddRange(broken.Where(planReference => !uncovered.ContainsKey(planReference.Target)).Select(planReference => $"does not resolve: {planReference}"));
        return problems;
    }

    /// <summary>
    /// One line per stale entry of <paramref name="exempt"/> (its criterion is covered, marked or gone) and per checked,
    /// unmarked criterion that neither <paramref name="covered"/> nor <paramref name="exempt"/> holds.
    /// </summary>
    static List<string> FindUncovered(IEnumerable<PlanCriterion> criteria, HashSet<string> covered, IReadOnlyDictionary<string, string> exempt)
    {
        var open = criteria.Where(criterion => !criterion.IsManual && !covered.Contains(criterion.Key)).ToList();
        var problems = exempt.Keys.Except(open.Select(criterion => criterion.Key)).Select(key => $"stale exemption (covered, marked or gone): {key}").ToList();
        problems.AddRange(open.Where(criterion => !exempt.ContainsKey(criterion.Key)).Select(criterion => $"not covered: PLAN.md:{criterion.Line} {criterion.Key}"));
        return problems;
    }

    static bool Resolves(PlanReference reference) => Resolves(reference, FindTestMethods(typeof(PlanReferenceTests).Assembly), RepoPaths.Root);

    static PlanReference CreateReference(string type, string? method) => new(1, 1, type, method, null, null, []);

    /// <summary>The references of <paramref name="tests"/> placed in the Tests section of Package 5, whose first line is line 4.</summary>
    static List<PlanReference> Parse(string tests) => PlanReference.Parse("## Package 5: X\n\n### Tests (TDD)\n" + tests).References;
}
