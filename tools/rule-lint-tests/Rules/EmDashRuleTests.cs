using RuleLint.Rules;

namespace RuleLint.Tests.Rules;

/// <summary>Rule 18: no em dash in any text file.</summary>
public sealed class EmDashRuleTests
{
    static readonly IRule Rule = new EmDashRule();

    [Fact]
    public void Check_EmDashInADocument_ReportsItsLineAndColumn()
    {
        var finding = Assert.Single(LintCase.Check(Rule, LintCase.CreateFile("docs/notes.md", "first\nsecond " + EmDashRule.EmDash + " third")));
        Assert.Equal(("RL181", 2, 8), (finding.RuleId, finding.Line, finding.Column));
    }

    [Fact]
    public void Check_TwoEmDashesInCode_ReportsBoth() =>
        Assert.Equal(2, LintCase.Check(Rule, LintCase.CreateFile("src/App/A.cs", "// a " + EmDashRule.EmDash + " b " + EmDashRule.EmDash)).Count);

    /// <summary>Package M1: the manager's Blazor markup is text the scanner reads.</summary>
    [Fact]
    public void Check_EmDashInRazor_Reports()
    {
        Assert.True(SourceScanner.IsIncluded("src/OVS.Manager/Components/Layout/MainLayout.razor", LintCase.CreateSettings()));
        Assert.Equal(["RL181"], LintCase.ListRuleIds(Rule, "src/OVS.Manager/Components/Layout/MainLayout.razor", "<h1>A " + EmDashRule.EmDash + " B</h1>"));
    }

    [Theory]
    [InlineData("an en dash – is fine")]
    [InlineData("")]
    public void Check_NoEmDash_FindsNothing(string text) => Assert.Empty(LintCase.Check(Rule, LintCase.CreateFile("README.md", text)));
}
