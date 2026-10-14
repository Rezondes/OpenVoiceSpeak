using System.Globalization;
using System.Text;

namespace OVS.Client.Models;

/// <summary>
/// Packages 158 and 163: how well a text matches what the user typed, the same for the settings search and the quick
/// switcher: case, accents and the German umlauts do not count, so "gerate" finds "Geräte".
/// </summary>
internal static class TextMatch
{
    /// <summary>
    /// 0 for the same text, 1 when the text starts with the query, 2 when one of its words does, 3 when it only holds it;
    /// null when it does not hold it or the query is empty.
    /// </summary>
    public static int? Rank(string text, string? query)
    {
        var wanted = Fold(query ?? "").Trim();
        if (wanted.Length == 0) return null;
        var folded = Fold(text);
        if (!folded.Contains(wanted, StringComparison.Ordinal)) return null;
        return folded == wanted ? 0 : folded.StartsWith(wanted, StringComparison.Ordinal) ? 1
            : folded.Contains(" " + wanted, StringComparison.Ordinal) || folded.Contains("-" + wanted, StringComparison.Ordinal) ? 2 : 3;
    }

    /// <summary>Upper case without accents, and the German umlauts as their base letter.</summary>
    static string Fold(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var letter in text.Replace("ß", "ss", StringComparison.Ordinal).ToUpperInvariant().Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(letter) != UnicodeCategory.NonSpacingMark) builder.Append(letter);
        return builder.ToString();
    }
}
