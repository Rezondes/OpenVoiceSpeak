namespace OVS.Manager.Telemetry;

/// <summary>Package M20: one span of a server as it came in.</summary>
internal sealed class SpanRow
{
    /// <summary>The row's id.</summary>
    public long Id { get; set; }

    /// <summary>The server's id (its <c>service.instance.id</c>).</summary>
    public string ServerId { get; set; } = "";

    /// <summary>The trace as 32 hex digits.</summary>
    public string TraceId { get; set; } = "";

    /// <summary>The span as 16 hex digits.</summary>
    public string SpanId { get; set; } = "";

    /// <summary>The parent span as 16 hex digits, null for a root span.</summary>
    public string? ParentSpanId { get; set; }

    /// <summary>The span's name, such as "request CreateChannel".</summary>
    public string Name { get; set; } = "";

    /// <summary>When it started, in Unix milliseconds.</summary>
    public long StartUnixMs { get; set; }

    /// <summary>How long it took in milliseconds.</summary>
    public double DurationMs { get; set; }

    /// <summary>The span ended with an error status.</summary>
    public bool Failed { get; set; }

    /// <summary>The span's attributes as a JSON object of strings, "" when it has none.</summary>
    public string Attributes { get; set; } = "";
}
