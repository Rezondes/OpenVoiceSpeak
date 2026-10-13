using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RuleLint;

/// <summary>Tells test methods by their attributes (xUnit, NUnit, MSTest, the headless Avalonia ones).</summary>
internal static class TestAttributes
{
    static readonly string[] Names = ["Fact", "Theory", "AvaloniaFact", "AvaloniaTheory", "Test", "TestCase", "TestMethod", "DataTestMethod"];

    /// <summary>True when <paramref name="method"/> carries a test attribute.</summary>
    public static bool IsTest(MethodDeclarationSyntax method) =>
        method.AttributeLists.SelectMany(list => list.Attributes).Any(attribute =>
        {
            var name = attribute.Name.ToString();
            name = name[(name.LastIndexOf('.') + 1)..];
            if (name.EndsWith("Attribute", StringComparison.Ordinal)) name = name[..^"Attribute".Length];
            return Names.Contains(name) || name.EndsWith("Fact", StringComparison.Ordinal) || name.EndsWith("Theory", StringComparison.Ordinal);
        });
}
