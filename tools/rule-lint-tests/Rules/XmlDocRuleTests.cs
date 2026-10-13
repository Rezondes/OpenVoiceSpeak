using RuleLint.Rules;

namespace RuleLint.Tests.Rules;

/// <summary>Rule 23: XML docs on every type and non-private member of production code.</summary>
public sealed class XmlDocRuleTests
{
    static readonly IRule Rule = new XmlDocRule();

    static List<string> ListSubjects(string path, string text) => [.. LintCase.Check(Rule, LintCase.CreateFile(path, text)).Select(finding => finding.Subject)];

    [Fact]
    public void Check_UndocumentedTypeAndPublicMember_ReportsBoth() =>
        Assert.Equal(["Shop", "Shop.Buy"], ListSubjects("src/App/Shop.cs", "namespace App;\npublic class Shop { public void Buy() { } void Hidden() { } }"));

    [Fact]
    public void Check_DocumentedAndInherited_FindsNothing() =>
        Assert.Empty(ListSubjects("src/App/Shop.cs",
            "namespace App;\n/// <summary>A shop.</summary>\npublic class Shop : IShop\n{\n    /// <inheritdoc/>\n    public void Buy() { }\n    private int count;\n    sealed class Inner { public void Open() { } }\n}"));

    [Fact]
    public void Check_InterfaceAndEnumMembers_NeedDocs() =>
        Assert.Equal(["IShop.Buy", "Kind.A"], ListSubjects("src/App/IShop.cs",
            "namespace App;\n/// <summary>x</summary>\ninterface IShop { void Buy(); }\n/// <summary>x</summary>\nenum Kind { A }"));

    [Fact]
    public void Check_TestCode_FindsNothing() =>
        Assert.Empty(ListSubjects("tests/App.Tests/ShopTests.cs", "namespace App.Tests;\npublic class ShopTests { public void Buy_Empty_Fails() { } }"));

    [Fact]
    public void Check_LoggerMessageMethod_MessageCountsAsDoc() =>
        Assert.Equal(["Lines.Plain"], ListSubjects("src/App/Lines.cs",
            "namespace App;\n/// <summary>x</summary>\nstatic partial class Lines\n{\n    [LoggerMessage(Level = LogLevel.Information, Message = \"Started\")]\n" +
            "    public static partial void Started(this ILogger logger);\n    public static void Plain() { }\n}"));
}
