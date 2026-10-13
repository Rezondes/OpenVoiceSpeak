using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RuleLint.Rules;

/// <summary>
/// Async rule 6, one injected clock: in the clock folders (every production root when none are listed), outside the
/// composition roots, a read of the system clock or a timer or delay without the injected <c>TimeProvider</c> needs the
/// marker <c>// clock: &lt;kind&gt;</c> at the end of its line.
/// </summary>
internal sealed class ClockRule : IRule
{
    static readonly string[] ClockReads =
    [
        "DateTime.Now", "DateTime.UtcNow", "DateTime.Today", "DateTimeOffset.Now", "DateTimeOffset.UtcNow",
        "Environment.TickCount", "Environment.TickCount64", "Stopwatch.StartNew", "Stopwatch.GetTimestamp", "TimeProvider.System",
    ];

    readonly LintSettings settings;

    /// <summary>The rule with the clock folders and composition roots of the settings.</summary>
    public ClockRule(LintSettings settings)
    {
        this.settings = settings;
    }

    /// <inheritdoc/>
    public IEnumerable<Finding> Check(SourceFile file, RepositoryIndex index)
    {
        if (!file.IsCode || !AppliesTo(file)) yield break;
        var lines = file.Text.Split('\n');
        foreach (var node in file.Syntax.DescendantNodes())
        {
            var use = ReadClockUse(node);
            if (use is null) continue;
            var line = node.GetLocation().GetLineSpan().StartLinePosition.Line;
            if (lines[line].Contains("// clock:", StringComparison.Ordinal)) continue;
            yield return Finding.CreateAt(file, node, "RL506", Severity.Error,
                $"{use} outside the composition roots; use the injected TimeProvider or mark the line '// clock: <kind>' (Async rule 6).",
                MemberNames.ReadEnclosing(node) + ":" + use);
        }
    }

    /// <summary>What clock <paramref name="node"/> uses on its own (a member access or a creation), or null.</summary>
    public static string? ReadClockUse(SyntaxNode node) => node switch
    {
        MemberAccessExpressionSyntax access when ClockReads.Any(read => access.ToString().EndsWith(read, StringComparison.Ordinal)
            && access.Name.Identifier.ValueText == read[(read.LastIndexOf('.') + 1)..]) => access.ToString(),
        InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Delay" } delay } call
            when delay.Expression.ToString().EndsWith("Task", StringComparison.Ordinal) && !MentionsClock(call.ArgumentList) => "Task.Delay",
        BaseObjectCreationExpressionSyntax creation when ReadCreatedName(creation) is "Timer" or "PeriodicTimer" or "Stopwatch"
            && !MentionsClock(creation.ArgumentList) => "new " + ReadCreatedName(creation),
        _ => null,
    };

    bool AppliesTo(SourceFile file)
    {
        if (file.Root!.Kind != RootKind.Source || settings.CompositionRoots.Contains(file.Path)) return false;
        return settings.ClockFolders.Count == 0
            || settings.ClockFolders.Any(folder => file.Path.StartsWith(folder.TrimEnd('/') + "/", StringComparison.Ordinal));
    }

    static string ReadCreatedName(BaseObjectCreationExpressionSyntax creation) =>
        creation is ObjectCreationExpressionSyntax named ? RepositoryIndex.ReadSimpleName(named.Type) : "";

    /// <summary>True when an argument is a variable or member named like a clock (<c>time</c>, <c>Time</c>, <c>clock</c>), not a computed value.</summary>
    static bool MentionsClock(ArgumentListSyntax? arguments) =>
        arguments is not null && arguments.Arguments.Any(argument =>
        {
            var name = argument.Expression switch
            {
                IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText,
                _ => "",
            };
            return name.Contains("time", StringComparison.OrdinalIgnoreCase) || name.Contains("clock", StringComparison.OrdinalIgnoreCase);
        });
}
