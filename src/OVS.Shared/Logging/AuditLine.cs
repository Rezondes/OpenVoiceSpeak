using System.Text.Encodings.Web;
using System.Text.Json;

namespace OVS.Shared.Logging;

/// <summary>
/// Package 165: a line of the audit log, an administrative action in the server log. After the time stamp it carries
/// <see cref="Marker"/> and the actor as a JSON string (a nickname may hold spaces, quotes or brackets), then the line's
/// usual text: <c>2026-10-15 12:00:00.000 [audit] "chef" Group 'Team' created by chef</c>. The server writes it, the
/// administration reads it with the log search; both go through this type.
/// </summary>
/// <param name="Time">The time stamp as written, server local time.</param>
/// <param name="Actor">Who did it.</param>
/// <param name="Action">What was done, the line's usual text.</param>
public sealed record AuditLine(string Time, string Actor, string Action)
{
    /// <summary>What an audit line carries right after its time stamp, and what the administration searches for.</summary>
    public const string Marker = "[audit] ";

    const int TimeLength = 23; // yyyy-MM-dd HH:mm:ss.fff

    static readonly JsonSerializerOptions Readable = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }; // umlauts stay readable

    /// <summary>The marker and the actor that go between the time stamp and the text.</summary>
    public static string Prefix(string actor) => Marker + JsonSerializer.Serialize(actor, Readable) + " ";

    /// <summary>The audit line of a log line, null when it is none (or was cut).</summary>
    public static AuditLine? Parse(string line)
    {
        var start = TimeLength + 1 + Marker.Length;
        if (line.Length <= start || string.CompareOrdinal(line, TimeLength + 1, Marker, 0, Marker.Length) != 0 || line[start] != '"') return null;
        int end = start + 1;
        while (end < line.Length && line[end] != '"') end += line[end] == '\\' ? 2 : 1;
        if (end >= line.Length) return null;
        try
        {
            var actor = JsonSerializer.Deserialize<string>(line.AsSpan(start, end - start + 1));
            return actor is null ? null : new AuditLine(line[..TimeLength], actor, line[Math.Min(line.Length, end + 2)..]);
        }
        catch (JsonException)
        {
            return null; // not written by Prefix
        }
    }
}
