using System.Globalization;
using System.Text;

namespace OVS.Manager.Audit;

/// <summary>Package M5 (AC3): audit lines as CSV (RFC 4180), the times in the manager's local time.</summary>
internal static class AuditCsv
{
    /// <summary>The header and one row per line of <paramref name="entries"/>.</summary>
    public static string Format(IEnumerable<AuditEntry> entries, TimeZoneInfo zone)
    {
        var csv = new StringBuilder("Time,User,Action,Target,Server,Detail\r\n");
        foreach (var entry in entries)
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(entry.AtUtc, zone).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            csv.AppendJoin(',', new[] { local, entry.UserName, entry.Action, entry.Target, entry.ServerId, entry.Detail }.Select(Field)).Append("\r\n");
        }
        return csv.ToString();
    }

    /// <summary>A field, quoted when it holds a comma, a quote or a line break; a leading formula character is defused.</summary>
    static string Field(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value[0] is '=' or '+' or '-' or '@') value = "'" + value; // no formula when a spreadsheet opens the file
        return value.AsSpan().IndexOfAny(",\"\r\n") >= 0 ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
    }
}
