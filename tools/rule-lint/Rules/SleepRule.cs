using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RuleLint.Rules;

/// <summary>Async rule 5: no <c>Thread.Sleep</c> with an argument other than 0 in production or tool code.</summary>
public sealed class SleepRule : IRule
{
    /// <inheritdoc/>
    public IEnumerable<Finding> Check(SourceFile file, RepositoryIndex index)
    {
        if (!file.IsCode || file.Root!.Kind == RootKind.Test) yield break;
        foreach (var call in file.Syntax.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (!call.Expression.ToString().EndsWith("Thread.Sleep", StringComparison.Ordinal)) continue;
            if (call.ArgumentList.Arguments is [{ Expression: LiteralExpressionSyntax { Token.ValueText: "0" } }]) continue;
            yield return Finding.CreateAt(file, call, "RL505", Severity.Error,
                "Thread.Sleep with a non-zero argument; wait on a wait handle that stop can end, or on the injected clock (Async rule 5).",
                MemberNames.ReadEnclosing(call) + ":Sleep");
        }
    }
}
