namespace RuleLint.Tests.Rules;

/// <summary>Rules 7 and 8: no ignored folder names, no folder named like a type.</summary>
public sealed class FolderNameRuleTests
{
    [Theory]
    [InlineData("Logs")]
    [InlineData("debug")]
    [InlineData("BackupFiles")]
    public void Check_IgnoredFolderName_Reports(string folder) =>
        Assert.Contains("RL071", LintCase.ListRuleIds($"src/App/{folder}/Order.cs", $"namespace App.{folder};\nclass Order {{ }}"));

    [Fact]
    public void Check_FolderNamedLikeATypeOfTheProject_Reports()
    {
        var findings = LintCase.Run(
            LintCase.CreateFile("src/App/Order.cs", "namespace App;\nclass Order { }"),
            LintCase.CreateFile("src/App/Order/Line.cs", "namespace App.Order;\nclass Line { }"));
        Assert.Equal(["RL081"], findings.Select(finding => finding.RuleId));
    }

    [Fact]
    public void Check_BadFolderWithManyFiles_ReportsItOnce()
    {
        var findings = LintCase.Run(
            LintCase.CreateFile("src/App/Logs/A.cs", "namespace App.Logs;\nclass A { }"),
            LintCase.CreateFile("src/App/Logs/B.cs", "namespace App.Logs;\nclass B { }"),
            LintCase.CreateFile("src/App/Logs/Deep/C.cs", "namespace App.Logs.Deep;\nclass C { }"));
        Assert.Single(findings, finding => finding.RuleId == "RL071");
    }

    [Fact]
    public void Check_FileOutsideEveryRoot_FindsNothing() =>
        Assert.Empty(LintCase.ListRuleIds("docs/Logs/notes.md", "text"));
}
