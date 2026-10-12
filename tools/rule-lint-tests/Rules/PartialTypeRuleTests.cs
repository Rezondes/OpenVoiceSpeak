using RuleLint.Rules;

namespace RuleLint.Tests.Rules;

/// <summary>Rule 4: only listed types are split into parts, in one folder.</summary>
public sealed class PartialTypeRuleTests
{
    static readonly LintSettings Settings = LintCase.CreateSettings(""", "partialTypes": [ "State" ]""");

    static readonly IRule Rule = new PartialTypeRule(Settings);

    [Fact]
    public void Check_ListedTypeInTwoParts_FindsNothing() =>
        Assert.Empty(LintCase.Check(Rule,
            LintCase.CreateFile("src/App/State.cs", "namespace App;\npartial class State { }", Settings),
            LintCase.CreateFile("src/App/State.Admin.cs", "namespace App;\npartial class State { }", Settings)));

    [Fact]
    public void Check_UnlistedTypeInTwoFiles_ReportsBothFiles()
    {
        var findings = LintCase.Check(Rule,
            LintCase.CreateFile("src/App/Order.cs", "namespace App;\npartial class Order { }", Settings),
            LintCase.CreateFile("src/App/Order.Lines.cs", "namespace App;\npartial class Order { }", Settings));
        Assert.Equal(["RL041", "RL041"], findings.Select(finding => finding.RuleId));
    }

    [Fact]
    public void Check_PartFileOfUnlistedTypeAlone_Reports() =>
        Assert.Contains("RL041", LintCase.ListRuleIds(Rule, "src/App/Order.Lines.cs", "namespace App;\npartial class Order { }"));

    [Fact]
    public void Check_ListedTypeInTwoFolders_ReportsTheFolders()
    {
        var findings = LintCase.Check(Rule,
            LintCase.CreateFile("src/App/State.cs", "namespace App;\npartial class State { }", Settings),
            LintCase.CreateFile("src/App/Parts/State.Admin.cs", "namespace App;\npartial class State { }", Settings));
        Assert.Contains(findings, finding => finding.RuleId == "RL042");
    }

    [Fact]
    public void Check_GeneratorPartialInOneFile_FindsNothing() =>
        Assert.Empty(LintCase.ListRuleIds(Rule, "src/App/Shop.cs", "namespace App;\npartial class Shop { }"));
}
