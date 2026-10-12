using RuleLint.Rules;

namespace RuleLint.Tests.Rules;

/// <summary>Rule 3: an interface needs a fake in the tests or several production implementations (a warning).</summary>
public sealed class InterfaceRuleTests
{
    static readonly IRule Rule = new InterfaceRule();

    static readonly SourceFile Interface = LintCase.CreateFile("src/App/IShop.cs", "namespace App;\ninterface IShop { }");

    [Fact]
    public void Check_OneImplementationAndNoFake_Warns()
    {
        var findings = LintCase.Check(Rule, Interface, LintCase.CreateFile("src/App/Shop.cs", "namespace App;\nclass Shop : IShop { }"));
        Assert.Equal(("RL031", Severity.Warning), (Assert.Single(findings).RuleId, findings[0].Severity));
    }

    [Fact]
    public void Check_FakeInTheTests_FindsNothing() =>
        Assert.Empty(LintCase.Check(Rule, Interface,
            LintCase.CreateFile("src/App/Shop.cs", "namespace App;\nclass Shop : IShop { }"),
            LintCase.CreateFile("tests/App.Tests/FakeShop.cs", "namespace App.Tests;\nclass FakeShop : App.IShop { }")));

    [Fact]
    public void Check_TwoProductionImplementations_FindsNothing() =>
        Assert.Empty(LintCase.Check(Rule, Interface,
            LintCase.CreateFile("src/App/Shop.cs", "namespace App;\nclass Shop : IShop { }"),
            LintCase.CreateFile("src/App/Market.cs", "namespace App;\nrecord Market : IShop;")));

    [Fact]
    public void Check_NoImplementation_Warns() => Assert.Single(LintCase.Check(Rule, Interface));
}
