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
    /// A trimmed name of 1..max chars, else null. Refused: control characters, format characters (category Cf: zero-width,
    /// bidi controls), line and paragraph separators and lone surrogates.
    /// </summary>
    public static string? Name(string? name, int max)
    {
        var n = name?.Trim();
        return n is { Length: > 0 } && n.Length <= max && Clean(n, r => Rune.IsControl(r) || Rune.GetUnicodeCategory(r)
            is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator) ? n : null;
    }

    /// <summary>
    /// A multi-line text of at most max chars with "\r\n" turned into "\n", else null; not trimmed. Refused: every other
    /// control character (C0 and C1, also a lone "\r" or a tab), bidi controls, line and paragraph separators and lone surrogates.
    /// Other format characters stay allowed, emoji sequences need the zero-width joiner.
    /// </summary>
    public static string? Text(string? text, int max)
    {
        var t = text?.Replace("\r\n", "\n");
        return t is not null && t.Length <= max && Clean(t, r => r.Value != '\n' && (Rune.IsControl(r) || IsBidiControl(r)
            || Rune.GetUnicodeCategory(r) is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)) ? t : null;
    }

    static bool IsBidiControl(Rune r) => r.Value is 0x061C or 0x200E or 0x200F or (>= 0x202A and <= 0x202E) or (>= 0x2066 and <= 0x2069);

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
