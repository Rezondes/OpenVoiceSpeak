namespace RuleLint.Tests;

/// <summary>The rule list and the order of the findings.</summary>
public sealed class LinterTests
{
    [Fact]
    public void CreateRules_EveryRuleType_IsListedOnce()
    {
        var listed = Linter.CreateRules(LintCase.CreateSettings()).Select(rule => rule.GetType()).ToList();
        var declared = typeof(IRule).Assembly.GetTypes().Where(type => typeof(IRule).IsAssignableFrom(type) && !type.IsInterface).ToList();
        Assert.Equal(declared.Count, listed.Distinct().Count());
        Assert.Equal(declared.OrderBy(type => type.Name), listed.OrderBy(type => type.Name));
    }

    [Fact]
    public void Run_FindingsOfSeveralFiles_AreOrderedByPathAndLine()
    {
        var findings = LintCase.Run(
            LintCase.CreateFile("src/App/B.cs", "namespace App;\nclass B { }\nclass C { }"),
            LintCase.CreateFile("src/App/A.cs", "namespace Wrong;\nclass A { }\nclass D { }"));
        Assert.Equal(["src/App/A.cs:1", "src/App/A.cs:3", "src/App/B.cs:3"], findings.Where(finding => finding.RuleId is "RL011" or "RL061").Select(finding => finding.Path + ":" + finding.Line));
    }

    [Fact]
    public void Run_NoFiles_FindsNothing() => Assert.Empty(LintCase.Run());

    [Fact]
    public void Format_Error_IsTheMsBuildForm() =>
        Assert.Equal("src/A.cs(3,5): error RL011: text", new Finding("RL011", Severity.Error, "src/A.cs", 3, 5, "text", "A").Format());
}
