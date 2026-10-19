namespace OVS.Manager.Telemetry;

#pragma warning disable CA1515 // public: a parameter of a Razor chart component, which Razor makes public
/// <summary>Package M22: a value at a time, for a chart.</summary>
/// <param name="AtUnixMs">When, in Unix milliseconds.</param>
/// <param name="Value">The value in the chart's unit.</param>
public sealed record ChartPoint(long AtUnixMs, double Value);
#pragma warning restore CA1515
