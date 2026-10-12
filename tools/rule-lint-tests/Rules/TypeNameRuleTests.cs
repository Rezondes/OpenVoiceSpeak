using RuleLint.Rules;

namespace RuleLint.Tests.Rules;

/// <summary>Rule 15 for type names: bag names, subject words, plurals of other types.</summary>
public sealed class TypeNameRuleTests
{
    static readonly IRule Rule = new TypeNameRule(LintCase.CreateSettings());

    [Theory]
    [InlineData("OrderHelper")]
    [InlineData("StringUtils")]
    [InlineData("SessionManager")]
    public void Check_BagName_Reports(string name) =>
        Assert.Equal(["RL151"], LintCase.ListRuleIds(Rule, $"src/App/{name}.cs", $"namespace App;\nclass {name} {{ }}"));

    [Fact]
    public void Check_SubjectWordAlone_Reports() =>
        Assert.Equal(["RL152"], LintCase.ListRuleIds(Rule, "src/App/Data.cs", "namespace App;\nclass Data { }"));

    [Fact]
    public void Check_SubjectWordWithSubject_FindsNothing() =>
        Assert.Empty(LintCase.ListRuleIds(Rule, "src/App/ChannelInfo.cs", "namespace App;\nrecord ChannelInfo(int Id);"));

    [Fact]
    public void Check_PluralOfAnotherType_Reports()
    {
        var findings = LintCase.Check(Rule,
            LintCase.CreateFile("src/App/Order.cs", "namespace App;\nclass Order { }"),
            LintCase.CreateFile("src/App/Orders.cs", "namespace App;\nclass Orders { }"));
        Assert.Equal(("RL153", "Orders"), (Assert.Single(findings).RuleId, findings[0].Subject));
    }

    [Fact]
    public void Check_TypeInTests_FindsNothing() =>
        Assert.Empty(LintCase.ListRuleIds(Rule, "tests/App.Tests/FakeManager.cs", "namespace App.Tests;\nclass FakeManager { }"));

    [Theory]
    [InlineData("Orders", "Order")]
    [InlineData("Boxes", "Box")]
    [InlineData("Categories", "Category")]
    public void FindSingular_PluralForms_GiveTheType(string plural, string singular) =>
        Assert.Equal(singular, TypeNameRule.FindSingular(plural, new HashSet<string> { singular }));

    [Theory]
    [InlineData("Address")]
    [InlineData("Status")]
    [InlineData("")]
    public void FindSingular_NoSuchType_IsNull(string name) =>
        Assert.Null(TypeNameRule.FindSingular(name, new HashSet<string> { "Order" }));
}
