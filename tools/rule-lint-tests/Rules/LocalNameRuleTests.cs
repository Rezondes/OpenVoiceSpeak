using RuleLint.Rules;

namespace RuleLint.Tests.Rules;

/// <summary>Rule 20: readable local, parameter, field and pattern names.</summary>
public sealed class LocalNameRuleTests
{
    static readonly IRule Rule = new LocalNameRule(LintCase.CreateSettings());

    static List<string> ListNames(string members) =>
        [.. LintCase.Check(Rule, LintCase.CreateFile("src/App/Shop.cs", "namespace App;\nclass Shop { " + members + " }")).Select(finding => finding.Subject)];

    [Theory]
    [InlineData("void Run() { var x = 1; }", "Shop.Run:x")]
    [InlineData("void Run(int s) { }", "Shop.Run:s")]
    [InlineData("void Run() { System.Func<int, int> twice = c => c * 2; }", "Shop.Run:c")]
    [InlineData("void Run() { try { } catch (System.Exception ex) { } }", "Shop.Run:ex")]
    [InlineData("void Run(int[] all) { foreach (var i in all) { } }", "Shop.Run:i")]
    [InlineData("void Run(object item) { if (item is string s) { } }", "Shop.Run:s")]
    [InlineData("int vm;", "Shop.vm:vm")]
    [InlineData("void Run(System.Threading.CancellationToken ct) { }", "Shop.Run:ct")]
    public void Check_UnreadableName_Reports(string members, string subject) => Assert.Equal([subject], ListNames(members));

    [Theory]
    [InlineData("void Run(int index, string _) { var total = index; }")]
    [InlineData("void Run(object sender, System.EventArgs args) { }")]
    [InlineData("void Run(System.Func<int, int> next) { _ = next(1); }")]
    public void Check_ReadableNamesAndDiscards_FindNothing(string members) => Assert.Empty(ListNames(members));
}
