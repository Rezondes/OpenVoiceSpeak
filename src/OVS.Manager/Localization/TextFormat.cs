using System.Globalization;

namespace OVS.Manager.Localization;

/// <summary>Package M4: a resource text with its placeholders filled in the page's culture (like the client's).</summary>
internal static class TextFormat
{
    /// <summary><paramref name="format"/> with <paramref name="values"/> in the current culture.</summary>
    public static string Of(string format, params object?[] values) => string.Format(CultureInfo.CurrentCulture, format, values);
}
