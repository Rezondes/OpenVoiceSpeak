using System.Buffers;
using System.Globalization;
using System.Text;

namespace OVS.Shared.Protocol;

/// <summary>
/// Package 83: which characters names and texts may hold. The server enforces them on every request; the client's dialogs
/// use the same rules so a user sees the problem before sending.
/// </summary>
public static class TextRules
{
    /// <summary>
    /// Returns the trimmed name when it has 1..max chars and is allowed, or null. Refused: control characters, format characters (category Cf: zero-width,
    /// bidi controls), line and paragraph separators and lone surrogates.
    /// </summary>
    public static string? NormalizeName(string? name, int max)
    {
        var text = name?.Trim();
        return text is { Length: > 0 } && text.Length <= max && Clean(text, rune => Rune.IsControl(rune) || Rune.GetUnicodeCategory(rune)
            is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator) ? text : null;
    }

    /// <summary>
    /// Returns the multi-line text with "\r\n" turned into "\n" when it has at most max chars and is allowed, or null;
    /// not trimmed. Refused: every other
    /// control character (C0 and C1, also a lone "\r" or a tab), bidi controls, line and paragraph separators and lone surrogates.
    /// Other format characters stay allowed, emoji sequences need the zero-width joiner.
    /// </summary>
    public static string? NormalizeText(string? text, int max)
    {
        var value = text?.Replace("\r\n", "\n");
        return value is not null && value.Length <= max && Clean(value, rune => rune.Value != '\n' && (Rune.IsControl(rune) || IsBidiControl(rune)
            || Rune.GetUnicodeCategory(rune) is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)) ? value : null;
    }

    static bool IsBidiControl(Rune rune) => rune.Value is 0x061C or 0x200E or 0x200F or (>= 0x202A and <= 0x202E) or (>= 0x2066 and <= 0x2069);

    /// <summary>False for a lone surrogate or a character the rule forbids.</summary>
    static bool Clean(string text, Func<Rune, bool> forbidden)
    {
        var rest = text.AsSpan();
        while (!rest.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(rest, out var rune, out int used) != OperationStatus.Done || forbidden(rune)) return false;
            rest = rest[used..];
        }
        return true;
    }
}
