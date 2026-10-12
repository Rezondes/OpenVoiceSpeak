using RuleLint.Rules;

namespace RuleLint.Tests.Rules;

/// <summary>Rule 16: German exception messages in production code (a warning).</summary>
public sealed class EnglishTextRuleTests
{
    static readonly IRule Rule = new EnglishTextRule();

    [Fact]
    public void Check_GermanExceptionMessage_Warns()
    {
        var finding = Assert.Single(LintCase.Check(Rule, LintCase.CreateFile("src/App/Shop.cs",
            "namespace App;\nclass Shop { void Buy() => throw new System.InvalidOperationException(\"Datei für den Kauf fehlt\"); }")));
        Assert.Equal(("RL161", Severity.Warning), (finding.RuleId, finding.Severity));
    }

    [Fact]
    public void Check_EnglishExceptionMessage_FindsNothing() =>
        Assert.Empty(LintCase.ListRuleIds(Rule, "src/App/Shop.cs", "namespace App;\nclass Shop { void Buy() { throw new System.Exception(\"File missing\"); } }"));

    [Fact]
    public void Check_UmlautOutsideAThrow_FindsNothing() =>
        Assert.Empty(LintCase.ListRuleIds(Rule, "src/App/Shop.cs", "namespace App;\nclass Shop { object Label() => new System.Text.StringBuilder(\"Größe\"); }"));

    [Fact]
    public void Check_GermanMessageInTests_FindsNothing() =>
        Assert.Empty(LintCase.ListRuleIds(Rule, "tests/App.Tests/ShopTests.cs", "namespace App.Tests;\nclass ShopTests { void Buy() => throw new System.Exception(\"Dialog kaputt, Größe\"); }"));
}
