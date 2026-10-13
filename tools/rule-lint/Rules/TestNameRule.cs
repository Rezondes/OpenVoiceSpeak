using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RuleLint.Rules;

/// <summary>
/// The test rules, in test code: every test name has three parts <c>Subject_Condition_Outcome</c> (an error), a class
/// with tests ends in <c>Tests</c> (a warning; a feature-named class may keep its name), and no fixed wait or wall clock
/// (<c>Task.Delay</c>, <c>DateTime.Now</c>; a <c>Thread.Sleep</c> is RL505) in a test (a warning).
/// </summary>
internal sealed class TestNameRule : IRule
{
    static readonly string[] WallClocks = ["DateTime.Now", "DateTime.UtcNow", "DateTimeOffset.Now", "DateTimeOffset.UtcNow"];

    /// <inheritdoc/>
    public IEnumerable<Finding> Check(SourceFile file, RepositoryIndex index)
    {
        if (!file.IsCode || file.Root!.Kind != RootKind.Test) yield break;
        foreach (var type in file.Syntax.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            var tests = type.Members.OfType<MethodDeclarationSyntax>().Where(TestAttributes.IsTest).ToList();
            if (tests.Count > 0 && !type.Identifier.ValueText.EndsWith("Tests", StringComparison.Ordinal))
            {
                yield return Finding.CreateAt(file, type.Identifier, "RL602", Severity.Warning,
                    $"'{type.Identifier.ValueText}' holds tests but does not end in 'Tests' (§Tests).", type.Identifier.ValueText);
            }
            foreach (var test in tests.Where(test => !HasThreeParts(test.Identifier.ValueText)))
            {
                yield return Finding.CreateAt(file, test.Identifier, "RL601", Severity.Error,
                    $"'{test.Identifier.ValueText}' is no three-part test name Subject_Condition_Outcome (§Tests).", MemberNames.ReadEnclosing(test));
            }
        }
        foreach (var call in file.Syntax.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var text = call.Expression.ToString();
            if (!text.EndsWith("Task.Delay", StringComparison.Ordinal)) continue;
            yield return Finding.CreateAt(file, call, "RL603", Severity.Warning,
                $"{text} in a test; advance a manual clock and drain, or bound the wait (§Tests).", MemberNames.ReadEnclosing(call) + ":" + text);
        }
        foreach (var access in file.Syntax.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
        {
            var text = access.ToString();
            if (!WallClocks.Any(clock => text.EndsWith(clock, StringComparison.Ordinal))) continue;
            yield return Finding.CreateAt(file, access, "RL603", Severity.Warning,
                $"{text} in a test; use a manual TimeProvider (§Tests).", MemberNames.ReadEnclosing(access) + ":" + text);
        }
    }

    /// <summary>True for a name of exactly three non-empty parts separated by underscores.</summary>
    public static bool HasThreeParts(string name)
    {
        var parts = name.Split('_');
        return parts.Length == 3 && parts.All(part => part.Length > 0);
    }
}
