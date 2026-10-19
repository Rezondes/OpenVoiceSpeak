using System.Globalization;

namespace OVS.Manager.Components.Charts;

/// <summary>Package M23 (AC4): a chart's data as a CSV download link, made in the page (a data URI), so no endpoint is needed.</summary>
internal static class ChartCsv
{
    /// <summary>The link target for <paramref name="rows"/> under <paramref name="header"/>.</summary>
    public static string Href(IEnumerable<string> header, IEnumerable<IEnumerable<string>> rows) =>
        "data:text/csv;charset=utf-8," + Uri.EscapeDataString(string.Join("\r\n", new[] { header }.Concat(rows).Select(row => string.Join(",", row.Select(Field)))) + "\r\n");

    /// <summary>The download's file name from the chart's title.</summary>
    public static string FileName(string title) => string.Concat(title.Select(letter => char.IsLetterOrDigit(letter) ? letter : '-')) + ".csv";

    /// <summary>A number as the file holds it, independent of the language.</summary>
    public static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    static string Field(string text) => text.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : text;
}
