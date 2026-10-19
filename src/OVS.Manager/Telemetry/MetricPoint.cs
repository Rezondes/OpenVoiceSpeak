namespace OVS.Manager.Telemetry;

/// <summary>Package M20: one metric data point of a server as it came in.</summary>
internal sealed class MetricPoint
{
    /// <summary>The row's id.</summary>
    public long Id { get; set; }

    /// <summary>The server's id (its <c>service.instance.id</c>).</summary>
    public string ServerId { get; set; } = "";

    /// <summary>The metric's name, such as <c>ovs.users.online</c>.</summary>
    public string Name { get; set; } = "";

    /// <summary>What the point measures.</summary>
    public MetricKind Kind { get; set; }

    /// <summary>When it was measured, in Unix milliseconds.</summary>
    public long AtUnixMs { get; set; }

    /// <summary>The start of a cumulative sum in Unix milliseconds; a new start means the counter began again (M21).</summary>
    public long StartUnixMs { get; set; }

    /// <summary>The value; of a histogram its sum.</summary>
    public double Value { get; set; }

    /// <summary>Of a histogram the number of values, else null.</summary>
    public long? Count { get; set; }

    /// <summary>The point's attributes as a JSON object of strings, "" when it has none.</summary>
    public string Attributes { get; set; } = "";
}
