using Microsoft.CodeAnalysis;

namespace RuleLint;

/// <summary>
/// One rule violation: the rule, how it counts, where it is, what is wrong, and the subject it is about (a type, member
/// or name). The baseline matches a finding by rule, path and subject, never by line, so an edit elsewhere in the file
/// does not turn a known finding into a new one.
/// </summary>
internal sealed record Finding(string RuleId, Severity Severity, string Path, int Line, int Column, string Message, string Subject)
{
    /// <summary>The key the baseline matches this finding by.</summary>
    public string Key => RuleId + "|" + Path + "|" + Subject;

    /// <summary>The finding in the MSBuild form editors and agents parse: <c>path(line,column): error RL011: message</c>.</summary>
    public string Format() => $"{Path}({Line},{Column}): {(Severity == Severity.Error ? "error" : "warning")} {RuleId}: {Message}";

    /// <summary>A finding at the start of <paramref name="node"/> in <paramref name="file"/>.</summary>
    public static Finding CreateAt(SourceFile file, SyntaxNode node, string ruleId, Severity severity, string message, string subject) =>
        FromLocation(file, node.GetLocation(), ruleId, severity, message, subject);

    /// <summary>A finding at <paramref name="token"/> in <paramref name="file"/>.</summary>
    public static Finding CreateAt(SourceFile file, SyntaxToken token, string ruleId, Severity severity, string message, string subject) =>
        FromLocation(file, token.GetLocation(), ruleId, severity, message, subject);

    /// <summary>A finding at the character <paramref name="offset"/> of the text of <paramref name="file"/>.</summary>
    public static Finding CreateAtOffset(SourceFile file, int offset, string ruleId, Severity severity, string message, string subject)
    {
        var line = 1;
        var lineStart = 0;
        for (var position = 0; position < offset && position < file.Text.Length; position++)
        {
            if (file.Text[position] != '\n') continue;
            line++;
            lineStart = position + 1;
        }
        return new Finding(ruleId, severity, file.Path, line, offset - lineStart + 1, message, subject);
    }

    /// <summary>A finding on the first line of <paramref name="file"/>.</summary>
    public static Finding CreateInFile(SourceFile file, string ruleId, Severity severity, string message, string subject) =>
        new(ruleId, severity, file.Path, 1, 1, message, subject);

    static Finding FromLocation(SourceFile file, Location location, string ruleId, Severity severity, string message, string subject)
    {
        var start = location.GetLineSpan().StartLinePosition;
        return new Finding(ruleId, severity, file.Path, start.Line + 1, start.Character + 1, message, subject);
    }
}
