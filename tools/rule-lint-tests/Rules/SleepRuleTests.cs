using RuleLint.Rules;

namespace RuleLint.Tests.Rules;

/// <summary>Async rule 5: no Thread.Sleep anywhere, also not Sleep(0) and not in tests (the user, 13 October 2026).</summary>
public sealed class SleepRuleTests
{
    static readonly IRule Rule = new SleepRule();

    static List<string> ListRuleIds(string call, string path = "src/App/Shop.cs") =>
        LintCase.ListRuleIds(Rule, path, "namespace App;\nclass Shop { void Run(int wait) { " + call + " } }");

    [Theory]
    [InlineData("System.Threading.Thread.Sleep(10);")]
    [InlineData("Thread.Sleep(wait);")]
    [InlineData("Thread.Sleep(1);")]
    [InlineData("Thread.Sleep(0);")]
    public void Check_AnySleep_Reports(string call) => Assert.Equal(["RL505"], ListRuleIds(call));

    [Fact]
    public void Check_SleepInTests_Reports() => Assert.Equal(["RL505"], ListRuleIds("Thread.Sleep(50);", "tests/App.Tests/Shop.cs"));
}
