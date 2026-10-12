using RuleLint.Rules;

namespace RuleLint.Tests.Rules;

/// <summary>Rule 24: comments name types, not source paths (a warning).</summary>
public sealed class CommentPathRuleTests
{
    static readonly IRule Rule = new CommentPathRule();

    [Theory]
    [InlineData("// moved from src/App/Old/Shop.cs\nnamespace App;")]
    [InlineData("/// <summary>See Views/ShopView.axaml.</summary>\nclass Shop { }")]
    public void Check_PathInAComment_Warns(string text)
    {
        var finding = Assert.Single(LintCase.Check(Rule, LintCase.CreateFile("src/App/Shop.cs", text)));
        Assert.Equal(("RL241", Severity.Warning), (finding.RuleId, finding.Severity));
    }

    [Theory]
    [InlineData("// see ShopView and Shop.Buy\nnamespace App;")]
    [InlineData("namespace App;\nclass Shop { string path = \"src/App/Shop.cs\"; }")]
    public void Check_TypeNamesAndStrings_FindNothing(string text) => Assert.Empty(LintCase.Check(Rule, LintCase.CreateFile("src/App/Shop.cs", text)));
}
