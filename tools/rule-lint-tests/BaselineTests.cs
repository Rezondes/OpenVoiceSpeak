namespace RuleLint.Tests;

/// <summary>The baseline: known findings pass, new ones and extra copies are reported, fixed keys are listed.</summary>
public sealed class BaselineTests
{
    static Finding Create(string subject, int line = 1) => new("RL011", Severity.Error, "src/App/A.cs", line, 1, "message", subject);

    [Fact]
    public void Apply_EmptyBaseline_EverythingIsNew() =>
        Assert.Equal(2, Baseline.Empty.Apply([Create("A"), Create("B")]).New.Count);

    [Fact]
    public void Apply_WrittenBaseline_KnowsEveryFindingEvenOnOtherLines()
    {
        var baseline = Baseline.Parse(Baseline.Write([Create("A", 3), Create("B", 9)]));
        var result = baseline.Apply([Create("A", 40), Create("B", 2)]);
        Assert.Equal((0, 2, 0), (result.New.Count, result.Known, result.Fixed.Count));
    }

    [Fact]
    public void Apply_OneMoreCopyThanKnown_ReportsTheExtraOne()
    {
        var result = Baseline.Parse(Baseline.Write([Create("A")])).Apply([Create("A", 1), Create("A", 2)]);
        Assert.Equal((1, 1), (result.New.Count, result.Known));
    }

    [Fact]
    public void Apply_KnownFindingGone_ListsItAsFixed() =>
        Assert.Equal(["RL011|src/App/A.cs|A"], Baseline.Parse(Baseline.Write([Create("A")])).Apply([]).Fixed);

    [Fact]
    public void Parse_DuplicateKeys_AddUp() =>
        Assert.Empty(Baseline.Parse("""[{"key":"RL011|src/App/A.cs|A","count":1},{"key":"RL011|src/App/A.cs|A","count":1}]""")
            .Apply([Create("A"), Create("A")]).New);

    [Fact]
    public void Parse_EmptyList_KnowsNothing() => Assert.Single(Baseline.Parse("[]").Apply([Create("A")]).New);

    [Fact]
    public void Parse_Malformed_Throws() => Assert.ThrowsAny<System.Text.Json.JsonException>(() => Baseline.Parse("{not json"));

    [Fact]
    public void Load_MissingFile_IsEmpty() =>
        Assert.Single(Baseline.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "none.json")).Apply([Create("A")]).New);
}
