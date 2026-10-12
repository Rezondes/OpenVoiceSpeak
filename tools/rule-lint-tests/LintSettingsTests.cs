namespace RuleLint.Tests;

/// <summary>The settings file: defaults of the general rules, the project's lists, comments allowed.</summary>
public sealed class LintSettingsTests
{
    [Fact]
    public void Parse_EmptyObject_KeepsTheGeneralDefaults()
    {
        var settings = LintSettings.Parse("{}");
        Assert.Equal((0, 400, 40), (settings.Roots.Count, settings.MaxTypeLines, settings.MaxMethodLines));
        Assert.Contains("vm", settings.BannedNames);
    }

    [Fact]
    public void Parse_RootsWithKindAndComments_ReadsThem()
    {
        var settings = LintSettings.Parse("{ // comment\n" + """ "roots": [ { "folder": "src/A", "rootNamespace": "A", "kind": "Tool" } ], }""");
        Assert.Equal(new SourceRoot("src/A", "A", RootKind.Tool), Assert.Single(settings.Roots));
    }

    [Fact]
    public void Parse_Null_Throws() => Assert.Throws<InvalidDataException>(() => LintSettings.Parse("null"));

    [Fact]
    public void Parse_UnknownKind_Throws() =>
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => LintSettings.Parse("""{ "roots": [ { "folder": "a", "rootNamespace": "A", "kind": "Other" } ] }"""));
}
