using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace OVS.Manager.Telemetry;

/// <summary>
/// Package M24: the errors in the servers' logs (level Error and above). The error ID is the trace id (Package 146) and the
/// line of <c>RequestFailed</c> carries the stack trace (Package 148) as <c>exception.stacktrace</c>.
/// </summary>
internal sealed class ErrorQuery(TelemetryStore store, TimeProvider time)
{
    /// <summary>The lowest OTLP severity of an error.</summary>
    public const int ErrorSeverity = 17;

    /// <summary>How many of a server's newest errors the page groups.</summary>
    const int Newest = 1000;

    const string TemplateAttribute = "{OriginalFormat}";
    const string ErrorIdAttribute = "ErrorId";

    /// <summary>AC1: the server's newest errors grouped by message, the group with the newest error first.</summary>
    public async Task<IReadOnlyList<ErrorGroup>> GroupsAsync(string serverId, CancellationToken cancellation = default)
    {
        var lines = await store.LogRecords.AsNoTracking().Where(line => line.ServerId == serverId && line.Severity >= ErrorSeverity)
            .OrderByDescending(line => line.AtUnixMs).Take(Newest).ToListAsync(cancellation);
        return [.. lines.Select(Entry).GroupBy(entry => entry.Message).Select(group => new ErrorGroup(group.Key, group.Count(), [.. group]))];
    }

    /// <summary>AC2: the error with <paramref name="errorId"/> on a server in <paramref name="visible"/> (null: every server), with its trace; null when there is none.</summary>
    public async Task<ErrorDetail?> LookupAsync(string errorId, IReadOnlySet<string>? visible, CancellationToken cancellation = default)
    {
        string id;
        try
        {
            id = Convert.ToHexStringLower(Convert.FromHexString(errorId.Trim())); // the IDs are hex: any case, stored lower
        }
        catch (FormatException)
        {
            return null;
        }
        if (id.Length == 0) return null;
        var marker = $"\"{ErrorIdAttribute}\":\"{id}\"";
        var lines = await store.LogRecords.AsNoTracking().Where(line => line.Severity >= ErrorSeverity && (line.TraceId == id || line.Attributes.Contains(marker)))
            .ToListAsync(cancellation);
        if (lines.FirstOrDefault(line => visible is null || visible.Contains(line.ServerId)) is not { } found) return null;
        List<SpanRow> spans = found.TraceId is { } trace
            ? await store.Spans.AsNoTracking().Where(span => span.TraceId == trace && span.ServerId == found.ServerId).OrderBy(span => span.StartUnixMs).ToListAsync(cancellation)
            : [];
        return new ErrorDetail(found.ServerId, Entry(found), spans);
    }

    /// <summary>AC3: the errors of the last 24 hours per server of <paramref name="serverIds"/>; a server without any is missing.</summary>
    public async Task<IReadOnlyDictionary<string, int>> CountsAsync(IReadOnlyCollection<string> serverIds, CancellationToken cancellation = default)
    {
        var since = time.GetUtcNow().AddDays(-1).ToUnixTimeMilliseconds();
        return await store.LogRecords.Where(line => serverIds.Contains(line.ServerId) && line.Severity >= ErrorSeverity && line.AtUnixMs >= since)
            .GroupBy(line => line.ServerId).Select(group => new { group.Key, Count = group.Count() }).ToDictionaryAsync(group => group.Key, group => group.Count, cancellation);
    }

    static ErrorEntry Entry(LogRecordRow line)
    {
        var attributes = line.Attributes.Length == 0 ? [] : JsonSerializer.Deserialize<Dictionary<string, string>>(line.Attributes) ?? [];
        return new ErrorEntry(line.Id, line.AtUnixMs, attributes.GetValueOrDefault(TemplateAttribute) ?? line.Body, line.Body,
            attributes.GetValueOrDefault(ErrorIdAttribute) ?? line.TraceId ?? "", attributes.GetValueOrDefault("exception.stacktrace") ?? "",
            attributes.GetValueOrDefault("exception.message") ?? "");
    }
}
