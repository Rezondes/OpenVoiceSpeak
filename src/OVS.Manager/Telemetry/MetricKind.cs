namespace OVS.Manager.Telemetry;

/// <summary>Package M20: what a metric point measures.</summary>
internal enum MetricKind
{
    /// <summary>A value at a time (OTLP gauge).</summary>
    Gauge,
    /// <summary>A sum, cumulative from its start time unless the server sends deltas (OTLP sum).</summary>
    Sum,
    /// <summary>A histogram; the point holds its sum and count (OTLP histogram).</summary>
    Histogram,
}
