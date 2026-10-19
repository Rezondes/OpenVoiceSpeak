namespace OVS.Manager.Telemetry;

/// <summary>
/// Package M21 (MA7): a metric series over a minute or an hour. Sums and histograms hold what was added in the period
/// (the delta of the cumulative counter, across restarts); gauges hold the last value with the lowest and highest.
/// </summary>
internal sealed class MetricRollup
{
    /// <summary>The row's id.</summary>
    public long Id { get; set; }

    /// <summary>The server's id.</summary>
    public string ServerId { get; set; } = "";

    /// <summary>The metric's name.</summary>
    public string Name { get; set; } = "";

    /// <summary>The series' attributes as in <see cref="MetricPoint.Attributes"/>.</summary>
    public string Attributes { get; set; } = "";

    /// <summary>What the metric measures.</summary>
    public MetricKind Kind { get; set; }

    /// <summary>Minute or hour.</summary>
    public RollupLevel Level { get; set; }

    /// <summary>The start of the period in Unix milliseconds.</summary>
    public long PeriodUnixMs { get; set; }

    /// <summary>A gauge's last value; what a sum or histogram (its sum) added in the period.</summary>
    public double Value { get; set; }

    /// <summary>A gauge's lowest value in the period.</summary>
    public double Min { get; set; }

    /// <summary>A gauge's highest value in the period.</summary>
    public double Max { get; set; }

    /// <summary>Of a histogram the values added in the period, else null.</summary>
    public long? Count { get; set; }
}
