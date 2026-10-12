using RuleLint.Rules;

namespace RuleLint.Tests.Rules;

/// <summary>Rule 15 for methods: the Async suffix and no repetition of the type's name.</summary>
public sealed class MethodNameRuleTests
{
    static readonly IRule Rule = new MethodNameRule();

    static List<string> ListRuleIds(string members) => LintCase.ListRuleIds(Rule, "src/App/Shop.cs", "namespace App;\nclass Shop { " + members + " }");

    [Theory]
    [InlineData("Task Buy() => Task.CompletedTask;")]
    [InlineData("ValueTask<int> Count() => default;")]
    [InlineData("System.Threading.Tasks.Task Load() => null!;")]
    public void Check_TaskWithoutAsync_Reports(string member) => Assert.Equal(["RL154"], ListRuleIds(member));

    [Theory]
    [InlineData("Task BuyAsync() => Task.CompletedTask;")]
    [InlineData("[Fact] public async Task Buy_Empty_Fails() { }")]
    [InlineData("public override Task Run() => null!;")]
    [InlineData("static Task Main() => null!;")]
    [InlineData("Task IRunner.Run() => null!;")]
    [InlineData("int Count() => 1;")]
    public void Check_AllowedMethod_FindsNothing(string member) => Assert.Empty(ListRuleIds(member));

    [Fact]
    public void Check_LocalFunctionReturningTask_Reports() =>
        Assert.Equal(["RL154"], ListRuleIds("void Run() { Task Next() => Task.CompletedTask; }"));

    [Fact]
    public void Check_NameRepeatsTheType_Warns() => Assert.Equal(["RL155"], ListRuleIds("void OpenShop() { }"));
}
