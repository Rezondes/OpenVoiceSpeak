using RuleLint.Rules;

namespace RuleLint.Tests.Rules;

/// <summary>Rule 9: exceptions live in an Exceptions/ folder.</summary>
public sealed class ExceptionFolderRuleTests
{
    static readonly IRule Rule = new ExceptionFolderRule();

    [Fact]
    public void Check_ExceptionInExceptionsFolder_FindsNothing() =>
        Assert.Empty(LintCase.ListRuleIds(Rule, "src/App/Exceptions/OrderException.cs", "namespace App.Exceptions;\nclass OrderException : System.Exception { }"));

    [Fact]
    public void Check_ExceptionElsewhere_Reports() =>
        Assert.Equal(["RL091"], LintCase.ListRuleIds(Rule, "src/App/OrderException.cs", "namespace App;\nclass OrderException : System.Exception { }"));

    [Fact]
    public void Check_ExceptionInTests_FindsNothing() =>
        Assert.Empty(LintCase.ListRuleIds(Rule, "tests/App.Tests/FakeException.cs", "namespace App.Tests;\nclass FakeException : System.Exception { }"));

    [Fact]
    public void Check_ClassNamedExceptionWithoutBase_FindsNothing() =>
        Assert.Empty(LintCase.ListRuleIds(Rule, "src/App/RuleException.cs", "namespace App;\nclass RuleException { }"));
}
