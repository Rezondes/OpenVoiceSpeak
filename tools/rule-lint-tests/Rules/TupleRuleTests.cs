using RuleLint.Rules;

namespace RuleLint.Tests.Rules;

/// <summary>Rule 21: no tuple returns and no out parameters instead of a record.</summary>
public sealed class TupleRuleTests
{
    static readonly IRule Rule = new TupleRule();

    static List<string> ListRuleIds(string members) => LintCase.ListRuleIds(Rule, "src/App/Shop.cs", "namespace App;\nclass Shop { " + members + " }");

    [Theory]
    [InlineData("(int, int) Split() => (1, 2);")]
    [InlineData("System.Threading.Tasks.Task<(int Count, string Name)> LoadAsync() => null!;")]
    [InlineData("System.Collections.Generic.IEnumerable<(string Path, object Value)> Walk() => [];")]
    [InlineData("(int, int) Pair { get; }")]
    [InlineData("void Run() { (int, int) Local() => (1, 2); }")]
    public void Check_TupleReturn_Reports(string member) => Assert.Equal(["RL211"], ListRuleIds(member));

    [Fact]
    public void Check_OutParameterAsSecondResult_Reports() => Assert.Equal(["RL212"], ListRuleIds("int Read(string text, out string rest) { rest = text; return 1; }"));

    [Theory]
    [InlineData("bool TryRead(string text, out int value) { value = 1; return true; }")]
    [InlineData("void Deconstruct(out int count, out string name) { count = 1; name = \"\"; }")]
    [InlineData("[LibraryImport(\"user32\")] static partial int GetCursorPos(out int point);")]
    [InlineData("void Run() { var pair = (1, 2); }")]
    [InlineData("void Run((int, int) pair) { }")]
    public void Check_AllowedForms_FindNothing(string member) => Assert.Empty(ListRuleIds(member));
}
