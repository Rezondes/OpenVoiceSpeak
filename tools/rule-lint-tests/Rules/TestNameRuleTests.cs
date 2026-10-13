using RuleLint.Rules;

namespace RuleLint.Tests.Rules;

/// <summary>The test rules: three-part names, Tests classes, no fixed waits.</summary>
public sealed class TestNameRuleTests
{
    static readonly IRule Rule = new TestNameRule();

    static List<string> ListRuleIds(string members, string type = "ShopTests") =>
        LintCase.ListRuleIds(Rule, "tests/App.Tests/" + type + ".cs", "namespace App.Tests;\nclass " + type + " { " + members + " }");

    [Theory]
    [InlineData("[Fact] void Buy_Works() { }")]
    [InlineData("[Theory] void Buy_Empty_Fails_Twice() { }")]
    [InlineData("[AvaloniaFact] void Buy__Fails() { }")]
    public void Check_NotThreeParts_Reports(string member) => Assert.Equal(["RL601"], ListRuleIds(member));

    [Fact]
    public void Check_ThreePartName_FindsNothing() => Assert.Empty(ListRuleIds("[Fact] void Buy_EmptyCart_Fails() { }"));

    [Fact]
    public void Check_TestsInAClassWithoutTestsSuffix_Warns() => Assert.Equal(["RL602"], ListRuleIds("[Fact] void Buy_EmptyCart_Fails() { }", "ShopFacts"));

    [Theory]
    [InlineData("async System.Threading.Tasks.Task WaitAsync() { await Task.Delay(10); }")]
    [InlineData("void Read() { var now = DateTime.Now; }")]
    public void Check_FixedWaitOrWallClock_Warns(string member) => Assert.Equal(["RL603"], ListRuleIds(member));

    [Fact]
    public void Check_ProductionCode_FindsNothing() =>
        Assert.Empty(LintCase.ListRuleIds(Rule, "src/App/Shop.cs", "namespace App;\nclass Shop { [Fact] void Bad() { } }"));

    [Theory]
    [InlineData("Buy_EmptyCart_Fails", true)]
    [InlineData("Buy_Fails", false)]
    [InlineData("_Empty_Fails", false)]
    [InlineData("", false)]
    public void HasThreeParts_Names_TellThreePartNames(string name, bool threeParts) => Assert.Equal(threeParts, TestNameRule.HasThreeParts(name));
}
