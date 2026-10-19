namespace OVS.Manager.Telemetry;

#pragma warning disable CA1515 // public: a parameter of a Razor chart component, which Razor makes public
/// <summary>Package M23: one series of a bar chart, a value per label.</summary>
/// <param name="Name">What the bars show, in the legend, the tooltips and the CSV.</param>
/// <param name="Color">A colour token of M2, such as <c>var(--accent)</c>.</param>
/// <param name="Values">One value per label of the chart.</param>
public sealed record BarSeries(string Name, string Color, IReadOnlyList<double> Values);
#pragma warning restore CA1515
