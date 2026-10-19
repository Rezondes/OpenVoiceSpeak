namespace OVS.Manager.Telemetry;

#pragma warning disable CA1515 // public: a parameter of a Razor chart component, which Razor makes public
/// <summary>Package M22: one line of a chart.</summary>
/// <param name="Name">What the line shows, in the legend and the tooltips.</param>
/// <param name="Color">A colour token of M2, such as <c>var(--accent)</c>, so it reads in light and dark.</param>
/// <param name="Points">The points in time order.</param>
public sealed record ChartSeries(string Name, string Color, IReadOnlyList<ChartPoint> Points);
#pragma warning restore CA1515
