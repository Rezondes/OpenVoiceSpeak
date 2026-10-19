using RuleLint.Rules;

namespace RuleLint.Tests.Rules;

/// <summary>Rules 1 and 2: one top-level type per file, named after it.</summary>
public sealed class TypeFileRuleTests
{
    static readonly IRule Rule = new TypeFileRule();

    [Fact]
    public void Check_OneTypeNamedLikeItsFile_FindsNothing() =>
        Assert.Empty(LintCase.ListRuleIds(Rule, "src/App/Order.cs", "namespace App;\n/// <summary>x</summary>\npublic sealed class Order { }"));

    [Fact]
    public void Check_TwoTopLevelTypes_ReportsTheSecond()
    {
        var finding = Assert.Single(LintCase.Check(Rule, LintCase.CreateFile("src/App/Order.cs", "namespace App;\nclass Order { }\nrecord Line(int Count);")));
        Assert.Equal(("RL011", "Line", 3), (finding.RuleId, finding.Subject, finding.Line));
    }

    [Fact]
    public void Check_TypeInAFileOfAnotherName_ReportsTheFileName() =>
        Assert.Equal(["RL012"], LintCase.ListRuleIds(Rule, "src/App/Orders.cs", "namespace App;\nclass Order { }"));

    [Fact]
    public void Check_GenericTypeInItsBareName_FindsNothing() =>
        Assert.Empty(LintCase.ListRuleIds(Rule, "src/App/Choice.cs", "namespace App;\nrecord Choice<T>(T Value) where T : notnull;"));

    [Fact]
    public void Check_CodeBehindOfAView_FindsNothing() =>
        Assert.Empty(LintCase.ListRuleIds(Rule, "src/App/Views/MainWindow.axaml.cs", "namespace App.Views;\npartial class MainWindow { }"));

    /// <summary>Package M1: the code-behind of a Blazor component.</summary>
    [Fact]
    public void Check_RazorCodeBehind_FindsNothing() =>
        Assert.Empty(LintCase.ListRuleIds(Rule, "src/App/Components/Overview.razor.cs", "namespace App.Components;\npartial class Overview { }"));

    [Fact]
    public void Check_FileWithoutTypes_FindsNothing() =>
        Assert.Empty(LintCase.ListRuleIds(Rule, "src/App/Program.cs", "System.Console.WriteLine(1);"));

    [Fact]
    public void Check_EmptyFile_FindsNothing() => Assert.Empty(LintCase.ListRuleIds(Rule, "src/App/Empty.cs", ""));

    [Fact]
    public void Check_XamlClassNamedLikeItsFile_FindsNothing() =>
        Assert.Empty(LintCase.ListRuleIds(Rule, "src/App/Views/ChatView.axaml", "<UserControl x:Class=\"App.Views.ChatView\"/>"));

    [Fact]
    public void Check_XamlClassOfAnotherName_ReportsTheFileName() =>
        Assert.Equal(["RL012"], LintCase.ListRuleIds(Rule, "src/App/Views/Chat.axaml", "<UserControl x:Class=\"App.Views.ChatView\"/>"));

    [Fact]
    public void Check_XamlWithoutClass_FindsNothing() =>
        Assert.Empty(LintCase.ListRuleIds(Rule, "src/App/Styles/Colors.axaml", "<ResourceDictionary/>"));

    [Fact]
    public void Check_MalformedCode_StillReportsWhatItCanRead() =>
        Assert.Equal(["RL011"], LintCase.ListRuleIds(Rule, "src/App/Order.cs", "namespace App;\nclass Order { void Broken( }\nclass Second { }"));

    [Fact]
    public void ReadTypeNameOfFile_PartFile_GivesTheType() => Assert.Equal("ServerState", RuleLint.Rules.TypeFileRule.ReadTypeNameOfFile("ServerState.Admin.cs"));
}
