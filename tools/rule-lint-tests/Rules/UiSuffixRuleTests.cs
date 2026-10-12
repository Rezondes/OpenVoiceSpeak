using RuleLint.Rules;

namespace RuleLint.Tests.Rules;

/// <summary>Rules 10 and 11: Views end in View, ViewModels in ViewModel.</summary>
public sealed class UiSuffixRuleTests
{
    static readonly IRule Rule = new UiSuffixRule(LintCase.CreateSettings());

    [Fact]
    public void Check_ViewWithSuffix_FindsNothing() =>
        Assert.Empty(LintCase.ListRuleIds(Rule, "src/App/Views/ChatView.axaml.cs", "namespace App.Views;\nclass ChatView { }"));

    [Fact]
    public void Check_ViewWithoutSuffix_Reports() =>
        Assert.Equal(["RL101"], LintCase.ListRuleIds(Rule, "src/App/Views/Chat.axaml.cs", "namespace App.Views;\nclass Chat { }"));

    [Fact]
    public void Check_ListedMainWindow_FindsNothing() =>
        Assert.Empty(LintCase.ListRuleIds(Rule, "src/App/Views/MainWindow.axaml.cs", "namespace App.Views;\nclass MainWindow { }"));

    [Fact]
    public void Check_RecordInViewModels_Reports() =>
        Assert.Equal(["RL111"], LintCase.ListRuleIds(Rule, "src/App/ViewModels/Notice.cs", "namespace App.ViewModels;\nrecord Notice(string Text);"));

    [Fact]
    public void Check_TestsInViewModelsFolder_FindsNothing() =>
        Assert.Empty(LintCase.ListRuleIds(Rule, "tests/App.Tests/ViewModels/ChatViewModelTests.cs", "namespace App.Tests.ViewModels;\nclass ChatViewModelTests { }"));
}
