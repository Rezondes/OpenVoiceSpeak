namespace RuleLint.Tests.Rules;

/// <summary>Rule 5: nested types are private and stand at the end of their container.</summary>
public sealed class NestedTypeRuleTests
{
    [Fact]
    public void Check_PrivateNestedTypeAtTheEnd_FindsNothing() =>
        Assert.Empty(LintCase.ListRuleIds("src/App/Order.cs", "namespace App;\nclass Order { int count; sealed record Line(int Count); enum Kind { A } }"));

    [Theory]
    [InlineData("public")]
    [InlineData("internal")]
    [InlineData("protected")]
    [InlineData("private protected")]
    public void Check_NestedTypeReachableFromOutside_Reports(string access) =>
        Assert.Equal(["RL051"], LintCase.ListRuleIds("src/App/Order.cs", $"namespace App;\nclass Order {{ {access} sealed class Line {{ }} }}"));

    [Fact]
    public void Check_MembersAfterANestedType_ReportsTheContainerOnce()
    {
        var finding = Assert.Single(LintCase.Run(LintCase.CreateFile("src/App/Order.cs", "namespace App;\nclass Order { class Line { } int first; int second; }")));
        Assert.Equal(("RL052", "Order"), (finding.RuleId, finding.Subject));
    }

    [Fact]
    public void Check_InterfaceMembers_AreNotChecked() =>
        Assert.Empty(LintCase.ListRuleIds("src/App/IShop.cs", "namespace App;\ninterface IShop { class Default { } void Buy(); }"));
}
