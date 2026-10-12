using RuleLint.Rules;

namespace RuleLint.Tests.Rules;

/// <summary>Rules 12 and 13 in XAML: no literal user text, fixed prefixes.</summary>
public sealed class XamlRuleTests
{
    static readonly LintSettings Settings = LintCase.CreateSettings(""", "xamlPrefixes": { "vm": "App.ViewModels" }""");

    static readonly IRule Rule = new XamlRule(Settings);

    static List<string> ListRuleIds(string xaml) =>
        [.. LintCase.Check(Rule, LintCase.CreateFile("src/App/Views/ChatView.axaml", xaml, Settings)).Select(finding => finding.RuleId)];

    [Fact]
    public void Check_BoundAndResourceText_FindsNothing() =>
        Assert.Empty(ListRuleIds("<UserControl x:Class=\"App.Views.ChatView\"><TextBlock Text=\"{Binding Name}\"/><TextBlock Text=\"·\"/></UserControl>"));

    [Fact]
    public void Check_LiteralText_WarnsOnce()
    {
        var finding = Assert.Single(LintCase.Check(Rule, LintCase.CreateFile("src/App/Views/ChatView.axaml",
            "<UserControl x:Class=\"App.Views.ChatView\">\n  <Button Content=\"Senden\"/>\n</UserControl>", Settings)));
        Assert.Equal(("RL121", Severity.Warning, 2), (finding.RuleId, finding.Severity, finding.Line));
    }

    [Fact]
    public void Check_FixedPrefix_FindsNothing() =>
        Assert.Empty(ListRuleIds("<UserControl x:Class=\"App.Views.ChatView\" xmlns:vm=\"using:App.ViewModels\"/>"));

    [Fact]
    public void Check_OtherPrefixForAListedNamespace_Reports() =>
        Assert.Equal(["RL131"], ListRuleIds("<UserControl x:Class=\"App.Views.ChatView\" xmlns:models=\"using:App.ViewModels\"/>"));

    [Fact]
    public void Check_ListedPrefixForAnotherNamespace_Reports() =>
        Assert.Equal(["RL131"], ListRuleIds("<UserControl x:Class=\"App.Views.ChatView\" xmlns:vm=\"using:App.Models\"/>"));
}
