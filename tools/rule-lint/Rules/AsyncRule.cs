using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RuleLint.Rules;

/// <summary>
/// Async rules 1 and 3 in production and tool code: no <c>GetAwaiter().GetResult()</c> outside <c>Main</c> (an error),
/// no <c>.Result</c> or <c>.Wait()</c> on what may be a task (a warning: a wait handle's <c>Wait</c> is allowed), and no
/// <c>async void</c> except an event handler (its last parameter is some <c>EventArgs</c>).
/// </summary>
public sealed class AsyncRule : IRule
{
    /// <inheritdoc/>
    public IEnumerable<Finding> Check(SourceFile file, RepositoryIndex index)
    {
        if (!file.IsCode || file.Root!.Kind == RootKind.Test) yield break;
        foreach (var node in file.Syntax.DescendantNodes())
        {
            if (IsInMain(node)) continue;
            if (node is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access } call)
            {
                var name = access.Name.Identifier.ValueText;
                if (name == "GetResult" && access.Expression.ToString().EndsWith("GetAwaiter()", StringComparison.Ordinal))
                {
                    yield return Finding.CreateAt(file, call, "RL501", Severity.Error,
                        "GetAwaiter().GetResult() blocks on a task; await it (Async rule 1).", MemberNames.ReadEnclosing(call) + ":GetResult");
                }
                else if (name == "Wait" && call.ArgumentList.Arguments.Count == 0)
                {
                    yield return Finding.CreateAt(file, call, "RL502", Severity.Warning,
                        ".Wait() blocks; await the task (Async rule 1; a wait handle on a dedicated thread is allowed).", MemberNames.ReadEnclosing(call) + ":Wait");
                }
            }
            else if (node is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Result" } result && result.Parent is not InvocationExpressionSyntax
                && result.Expression is InvocationExpressionSyntax)
            {
                yield return Finding.CreateAt(file, result, "RL502", Severity.Warning,
                    ".Result on a call blocks if it is a task; await it (Async rule 1).", MemberNames.ReadEnclosing(result) + ":Result");
            }
            else if (node is MethodDeclarationSyntax method && IsAsyncVoid(method) && !IsEventHandler(method))
            {
                yield return Finding.CreateAt(file, method.Identifier, "RL503", Severity.Error,
                    $"'{method.Identifier.ValueText}' is async void but no event handler; return a Task (Async rule 3).", MemberNames.ReadEnclosing(method));
            }
        }
    }

    static bool IsInMain(SyntaxNode node) =>
        node.Ancestors().OfType<MethodDeclarationSyntax>().Any(method => method.Identifier.ValueText == "Main")
        || node.Ancestors().OfType<GlobalStatementSyntax>().Any();

    static bool IsAsyncVoid(MethodDeclarationSyntax method) =>
        method.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.AsyncKeyword))
        && method.ReturnType is PredefinedTypeSyntax predefined && predefined.Keyword.IsKind(SyntaxKind.VoidKeyword);

    static bool IsEventHandler(MethodDeclarationSyntax method)
    {
        var parameters = method.ParameterList.Parameters;
        return parameters.Count == 2 && parameters[1].Type is { } type && RepositoryIndex.ReadSimpleName(type).EndsWith("EventArgs", StringComparison.Ordinal);
    }
}
