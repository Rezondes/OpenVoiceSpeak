namespace RuleLint.Tests.Rules;

/// <summary>Rules 1 and 2: one top-level type per file, named after it.</summary>
public sealed class TypeFileRuleTests
{
    [Fact]
    public void Check_OneTypeNamedLikeItsFile_FindsNothing() =>
        Assert.Empty(LintCase.ListRuleIds("src/App/Order.cs", "namespace App;\n/// <summary>x</summary>\npublic sealed class Order { }"));

    [Fact]
    public void Check_TwoTopLevelTypes_ReportsTheSecond()
    {
        var finding = Assert.Single(LintCase.Run(LintCase.CreateFile("src/App/Order.cs", "namespace App;\nclass Order { }\nrecord Line(int Count);")));
        Assert.Equal(("RL011", "Line", 3), (finding.RuleId, finding.Subject, finding.Line));
    }

    [Fact]
    public void Check_TypeInAFileOfAnotherName_ReportsTheFileName() =>
        Assert.Equal(["RL012"], LintCase.ListRuleIds("src/App/Orders.cs", "namespace App;\nclass Order { }"));

    [Fact]
    public void Check_GenericTypeInItsBareName_FindsNothing() =>
        Assert.Empty(LintCase.ListRuleIds("src/App/Choice.cs", "namespace App;\nrecord Choice<T>(T Value) where T : notnull;"));

    [Fact]
    public void Check_CodeBehindOfAView_FindsNothing() =>
        Assert.Empty(LintCase.ListRuleIds("src/App/Views/MainWindow.axaml.cs", "namespace App.Views;\npartial class MainWindow { }"));

    [Fact]
    public void Check_FileWithoutTypes_FindsNothing() =>
        Assert.Empty(LintCase.ListRuleIds("src/App/Program.cs", "System.Console.WriteLine(1);"));

    [Fact]
    public void Check_EmptyFile_FindsNothing() => Assert.Empty(LintCase.ListRuleIds("src/App/Empty.cs", ""));

    [Fact]
    public void Check_XamlClassNamedLikeItsFile_FindsNothing() =>
        Assert.Empty(LintCase.ListRuleIds("src/App/Views/ChatView.axaml", "<UserControl x:Class=\"App.Views.ChatView\"/>"));

    [Fact]
    public void Check_XamlClassOfAnotherName_ReportsTheFileName() =>
        Assert.Equal(["RL012"], LintCase.ListRuleIds("src/App/Views/Chat.axaml", "<UserControl x:Class=\"App.Views.ChatView\"/>"));

    [Fact]
    public void Check_XamlWithoutClass_FindsNothing() =>
        Assert.Empty(LintCase.ListRuleIds("src/App/Styles/Colors.axaml", "<ResourceDictionary/>"));

    [Fact]
    public void Check_MalformedCode_StillReportsWhatItCanRead() =>
        Assert.Equal(["RL011"], LintCase.ListRuleIds("src/App/Order.cs", "namespace App;\nclass Order { void Broken( }\nclass Second { }"));

    [Fact]
    public void ReadTypeNameOfFile_PartFile_GivesTheType() => Assert.Equal("ServerState", RuleLint.Rules.TypeFileRule.ReadTypeNameOfFile("ServerState.Admin.cs"));
}
