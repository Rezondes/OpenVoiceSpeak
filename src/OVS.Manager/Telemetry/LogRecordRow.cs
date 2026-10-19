namespace OVS.Manager.Telemetry;

/// <summary>Package M20: one line of a server's log as it came in.</summary>
internal sealed class LogRecordRow
{
    /// <summary>The row's id.</summary>
    public long Id { get; set; }

    /// <summary>The server's id (its <c>service.instance.id</c>).</summary>
    public string ServerId { get; set; } = "";

    /// <summary>When it was written, in Unix milliseconds.</summary>
    public long AtUnixMs { get; set; }

    /// <summary>The OTLP severity number: 9 to 12 information, 13 to 16 warning, 17 and above error.</summary>
    public int Severity { get; set; }

    /// <summary>The severity as the server named it, such as "Warning".</summary>
    public string SeverityText { get; set; } = "";

    /// <summary>The text of the line.</summary>
    public string Body { get; set; } = "";

    /// <summary>The trace the line was written in as 32 hex digits, null outside a span.</summary>
    public string? TraceId { get; set; }

    /// <summary>The span the line was written in as 16 hex digits, null outside a span.</summary>
    public string? SpanId { get; set; }

    /// <summary>The line's attributes (such as <c>exception.stacktrace</c>) as a JSON object of strings, "" when it has none.</summary>
    public string Attributes { get; set; } = "";
}
