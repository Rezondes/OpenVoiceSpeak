using RuleLint.Rules;

namespace RuleLint.Tests.Rules;

/// <summary>Async rule 5: no non-zero Thread.Sleep in production code.</summary>
public sealed class SleepRuleTests
{
    static readonly IRule Rule = new SleepRule();

    static List<string> ListRuleIds(string call, string path = "src/App/Shop.cs") =>
        LintCase.ListRuleIds(Rule, path, "namespace App;\nclass Shop { void Run(int wait) { " + call + " } }");

    [Theory]
    [InlineData("System.Threading.Thread.Sleep(10);")]
    [InlineData("Thread.Sleep(wait);")]
    [InlineData("Thread.Sleep(1);")]
    public void Check_NonZeroSleep_Reports(string call) => Assert.Equal(["RL505"], ListRuleIds(call));

    [Fact]
    public void Check_SleepZero_FindsNothing() => Assert.Empty(ListRuleIds("Thread.Sleep(0);"));

    [Fact]
    public void Check_SleepInTests_FindsNothing() => Assert.Empty(ListRuleIds("Thread.Sleep(50);", "tests/App.Tests/Shop.cs"));
}
