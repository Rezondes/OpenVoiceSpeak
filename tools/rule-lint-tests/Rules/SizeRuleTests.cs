using RuleLint.Rules;

namespace RuleLint.Tests.Rules;

/// <summary>The size red flags: long types and long methods (warnings).</summary>
public sealed class SizeRuleTests
{
    static readonly LintSettings Settings = LintCase.CreateSettings(""", "maxTypeLines": 6, "maxMethodLines": 3""");

    static readonly IRule Rule = new SizeRule(Settings);

    static List<string> ListRuleIds(string text, string path = "src/App/Shop.cs") =>
        [.. LintCase.Check(Rule, LintCase.CreateFile(path, text, Settings)).Select(finding => finding.RuleId)];

    [Fact]
    public void Check_LongMethod_Warns() => Assert.Contains("RL702", ListRuleIds("namespace App;\nclass Shop {\nvoid Run()\n{\nint a;\nint b;\n}\n}"));

    [Fact]
    public void Check_LongType_Warns() => Assert.Equal(["RL701"], ListRuleIds("namespace App;\nclass Shop\n{\nint a;\nint b;\nint c;\nint d;\nint e;\n}"));

    [Fact]
    public void Check_ShortCode_FindsNothing() => Assert.Empty(ListRuleIds("namespace App;\nclass Shop {\nvoid Run() { }\n}"));

    [Fact]
    public void Check_LongTestCode_FindsNothing() =>
        Assert.Empty(ListRuleIds("namespace App.Tests;\nclass ShopTests\n{\nvoid Run()\n{\nint a;\nint b;\n}\n}", "tests/App.Tests/ShopTests.cs"));
}
