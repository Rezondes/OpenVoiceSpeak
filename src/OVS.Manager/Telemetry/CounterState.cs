namespace OVS.Manager.Telemetry;

/// <summary>Package M21 (AC1): the last cumulative value of a sum or histogram series, to take deltas from.</summary>
internal sealed class CounterState
{
    /// <summary>The server's id.</summary>
    public string ServerId { get; set; } = "";

    /// <summary>The metric's name.</summary>
    public string Name { get; set; } = "";

    /// <summary>The series' attributes.</summary>
    public string Attributes { get; set; } = "";

    /// <summary>The counter's start in Unix milliseconds; another start means the server began counting again.</summary>
    public long StartUnixMs { get; set; }

    /// <summary>The last cumulative value (of a histogram its sum).</summary>
    public double LastValue { get; set; }

    /// <summary>The last cumulative count of a histogram, else null.</summary>
    public long? LastCount { get; set; }
}
