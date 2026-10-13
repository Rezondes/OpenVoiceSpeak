using System.Globalization;

namespace OVS.Client.Localization;

/// <summary>
/// Step 38: fills a localized text (<see cref="Strings"/>) with its values in the current culture. The one place that
/// formats them: a text's format is read on each call, since it changes with the UI language.
/// </summary>
internal static class TextFormat
{
    /// <summary>The localized <paramref name="format"/> with <paramref name="values"/> filled in.</summary>
#pragma warning disable CA1863 // a cached CompositeFormat would keep the language it was parsed in; the format changes with the UI language
    public static string Of(string format, params object?[] values) => string.Format(CultureInfo.CurrentCulture, format, values);
#pragma warning restore CA1863
}
