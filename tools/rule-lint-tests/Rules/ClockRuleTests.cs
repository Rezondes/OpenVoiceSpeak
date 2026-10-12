using RuleLint.Rules;

namespace RuleLint.Tests.Rules;

/// <summary>Async rule 6: one injected clock, other clock uses marked.</summary>
public sealed class ClockRuleTests
{
    static readonly LintSettings Settings = LintCase.CreateSettings(""", "compositionRoots": [ "src/App/Program.cs" ]""");

    static readonly IRule Rule = new ClockRule(Settings);

    static List<string> ListUses(string body, string path = "src/App/Shop.cs") =>
        [.. LintCase.Check(Rule, LintCase.CreateFile(path, "namespace App;\nclass Shop { void Run(System.TimeProvider time) {\n" + body + "\n} }", Settings))
            .Select(finding => finding.Subject[(finding.Subject.LastIndexOf(':') + 1)..])];

    [Theory]
    [InlineData("var now = DateTime.UtcNow;", "DateTime.UtcNow")]
    [InlineData("var now = System.DateTime.Now;", "System.DateTime.Now")]
    [InlineData("var ticks = Environment.TickCount64;", "Environment.TickCount64")]
    [InlineData("_ = Task.Delay(100);", "Task.Delay")]
    [InlineData("using var timer = new PeriodicTimer(System.TimeSpan.FromSeconds(1));", "new PeriodicTimer")]
    [InlineData("var watch = new Stopwatch();", "new Stopwatch")]
    [InlineData("var clock = TimeProvider.System;", "TimeProvider.System")]
    public void Check_UnmarkedClockUse_Reports(string body, string use) => Assert.Equal([use], ListUses(body));

    [Theory]
    [InlineData("var now = DateTime.UtcNow; // clock: debug")]
    [InlineData("_ = Task.Delay(100, time);")]
    [InlineData("using var timer = new PeriodicTimer(System.TimeSpan.FromSeconds(1), time);")]
    [InlineData("var now = time.GetUtcNow();")]
    public void Check_InjectedOrMarked_FindsNothing(string body) => Assert.Empty(ListUses(body));

    [Fact]
    public void Check_CompositionRoot_FindsNothing() => Assert.Empty(ListUses("var now = DateTime.UtcNow;", "src/App/Program.cs"));

    [Fact]
    public void Check_TestCode_FindsNothing() => Assert.Empty(ListUses("var now = DateTime.UtcNow;", "tests/App.Tests/Shop.cs"));
}
