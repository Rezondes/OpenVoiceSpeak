using RuleLint.Rules;

namespace RuleLint.Tests.Rules;

/// <summary>Rule 6: namespace = folder, file-scoped, no global using.</summary>
public sealed class NamespaceRuleTests
{
    static readonly IRule Rule = new NamespaceRule();

    [Fact]
    public void Check_FileScopedNamespaceOfTheFolder_FindsNothing() =>
        Assert.Empty(LintCase.ListRuleIds(Rule, "src/App/Orders/Order.cs", "namespace App.Orders;\nclass Order { }"));

    [Fact]
    public void Check_NamespaceOfAnotherFolder_Reports() =>
        Assert.Equal(["RL061"], LintCase.ListRuleIds(Rule, "src/App/Orders/Order.cs", "namespace App;\nclass Order { }"));

    [Fact]
    public void Check_BlockNamespace_Reports() =>
        Assert.Equal(["RL062"], LintCase.ListRuleIds(Rule, "src/App/Order.cs", "namespace App { class Order { } }"));

    [Fact]
    public void Check_TypeOutsideAnyNamespace_Reports() =>
        Assert.Equal(["RL061"], LintCase.ListRuleIds(Rule, "src/App/Order.cs", "class Order { }"));

    [Fact]
    public void Check_TopLevelStatementsWithoutNamespace_FindsNothing() =>
        Assert.Empty(LintCase.ListRuleIds(Rule, "src/App/Program.cs", "using System;\nConsole.WriteLine(1);"));

    [Fact]
    public void Check_GlobalUsing_Reports() =>
        Assert.Equal(["RL063"], LintCase.ListRuleIds(Rule, "src/App/Usings.cs", "global using System.Text;"));

    [Fact]
    public void Check_XamlClassInAnotherNamespace_Reports() =>
        Assert.Equal(["RL061"], LintCase.ListRuleIds(Rule, "src/App/Views/ChatView.axaml", "<UserControl x:Class=\"App.ChatView\"/>"));

    [Fact]
    public void Check_TestRoot_UsesItsRootNamespace() =>
        Assert.Empty(LintCase.ListRuleIds(Rule, "tests/App.Tests/Orders/OrderTests.cs", "namespace App.Tests.Orders;\nclass OrderTests { }"));
}
