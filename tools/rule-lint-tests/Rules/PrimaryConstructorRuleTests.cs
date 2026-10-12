using RuleLint.Rules;

namespace RuleLint.Tests.Rules;

/// <summary>Rule 22: no primary constructor with guard clauses in a class.</summary>
public sealed class PrimaryConstructorRuleTests
{
    static readonly IRule Rule = new PrimaryConstructorRule();

    [Theory]
    [InlineData("class Shop(string name) { readonly string name = name ?? throw new System.ArgumentNullException(nameof(name)); }")]
    [InlineData("class Shop(string name) { string Name { get; } = Check(name); static string Check(string value) => value; int count = ThrowIfNegative(1); static int ThrowIfNegative(int value) => value; }")]
    public void Check_GuardedPrimaryConstructor_Reports(string type) =>
        Assert.Equal(["RL221"], LintCase.ListRuleIds(Rule, "src/App/Shop.cs", "namespace App;\n" + type));

    [Theory]
    [InlineData("class Shop(string name) { readonly string name = name; }")]
    [InlineData("record Shop(string Name) { string Name { get; } = Name ?? throw new System.ArgumentNullException(nameof(Name)); }")]
    [InlineData("class Shop { readonly string name; Shop(string name) { this.name = name ?? throw new System.ArgumentNullException(nameof(name)); } }")]
    public void Check_AllowedConstructor_FindsNothing(string type) =>
        Assert.Empty(LintCase.ListRuleIds(Rule, "src/App/Shop.cs", "namespace App;\n" + type));
}
