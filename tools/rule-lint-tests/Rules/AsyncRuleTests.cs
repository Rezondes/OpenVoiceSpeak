using RuleLint.Rules;

namespace RuleLint.Tests.Rules;

/// <summary>Async rules 1 and 3: no blocking on tasks, no async void outside event handlers.</summary>
public sealed class AsyncRuleTests
{
    static readonly IRule Rule = new AsyncRule();

    static List<string> ListRuleIds(string members, string path = "src/App/Shop.cs") =>
        LintCase.ListRuleIds(Rule, path, "namespace App;\nclass Shop { " + members + " }");

    [Theory]
    [InlineData("void Run(System.Threading.Tasks.Task task) { task.GetAwaiter().GetResult(); }", "RL501")]
    [InlineData("void Run(System.Threading.Tasks.Task task) { task.Wait(); }", "RL502")]
    [InlineData("int Run() => Load().Result; System.Threading.Tasks.Task<int> Load() => null!;", "RL502")]
    [InlineData("async void Run() { }", "RL503")]
    public void Check_BlockingOrAsyncVoid_Reports(string members, string ruleId) => Assert.Equal([ruleId], ListRuleIds(members));

    [Theory]
    [InlineData("async void OnClick(object sender, System.EventArgs args) { }")]
    [InlineData("static void Main() { Load().GetAwaiter().GetResult(); } static System.Threading.Tasks.Task Load() => null!;")]
    [InlineData("void Run(System.Threading.ManualResetEventSlim gate) { gate.Wait(100); }")]
    [InlineData("int Run(Outcome outcome) => outcome.Result; record Outcome(int Result);")]
    public void Check_AllowedForms_FindNothing(string members) => Assert.Empty(ListRuleIds(members));

    [Fact]
    public void Check_BlockingInTests_FindsNothing() =>
        Assert.Empty(ListRuleIds("void Run(System.Threading.Tasks.Task task) { task.Wait(); }", "tests/App.Tests/Shop.cs"));
}
