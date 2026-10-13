using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RuleLint.Rules;

/// <summary>Async rule 5: no <c>Thread.Sleep</c> at all, in production, tool and test code (the user, 13 October 2026).</summary>
public sealed class SleepRule : IRule
{
    /// <inheritdoc/>
    public IEnumerable<Finding> Check(SourceFile file, RepositoryIndex index)
    {
        if (!file.IsCode) yield break;
        foreach (var call in file.Syntax.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (!call.Expression.ToString().EndsWith("Thread.Sleep", StringComparison.Ordinal)) continue;
            yield return Finding.CreateAt(file, call, "RL505", Severity.Error,
                "Thread.Sleep; wait on a wait handle that stop can end, on a timer of the injected clock, or await (Async rule 5).",
                MemberNames.ReadEnclosing(call) + ":Sleep");
        }
    }
}
