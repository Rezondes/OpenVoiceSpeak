using System.Globalization;
using Microsoft.AspNetCore.Components;
using OVS.Manager.Telemetry;

namespace OVS.Manager.Components.Charts;

/// <summary>Package M23 (AC1, AC4): grouped SVG bars per label (a day or a month) with a value axis in its unit, tooltips and a CSV download.</summary>
sealed partial class BarChart
{
    const double Width = 600, Height = 200, Left = 56, Right = 8, Top = 8, Bottom = 24;

    /// <summary>The chart's title.</summary>
    [Parameter, EditorRequired]
    public string Title { get; set; } = "";

    /// <summary>The unit of the values.</summary>
    [Parameter, EditorRequired]
    public string Unit { get; set; } = "";

    /// <summary>The column label of the CSV for the labels, such as "Tag".</summary>
    [Parameter, EditorRequired]
    public string LabelName { get; set; } = "";

    /// <summary>The labels along the bottom, one per group of bars.</summary>
    [Parameter, EditorRequired]
    public IReadOnlyList<string> Labels { get; set; } = [];

    /// <summary>The series, each with a value per label.</summary>
    [Parameter, EditorRequired]
    public IReadOnlyList<BarSeries> Series { get; set; } = [];

    double MaxValue => NiceMax(Series.SelectMany(series => series.Values).DefaultIfEmpty(0).Max());

    double GroupWidth => (Width - Left - Right) / Math.Max(1, Labels.Count);

    IEnumerable<AxisTick> YTicks => new[] { 0, MaxValue / 2, MaxValue }.Select(value => new AxisTick(Number(Y(value)), $"{Value(value)} {Unit}"));

    /// <summary>At most about 12 labels, so they stay readable.</summary>
    IEnumerable<AxisTick> XLabels
    {
        get
        {
            int every = Math.Max(1, (int)Math.Ceiling(Labels.Count / 12.0));
            for (int index = 0; index < Labels.Count; index += every)
                yield return new AxisTick(Number(Left + (index + 0.5) * GroupWidth), Labels[index]);
        }
    }

    IEnumerable<ChartBar> Bars
    {
        get
        {
            double wide = GroupWidth * 0.8 / Math.Max(1, Series.Count);
            for (int index = 0; index < Labels.Count; index++)
                for (int part = 0; part < Series.Count; part++)
                {
                    var value = index < Series[part].Values.Count ? Series[part].Values[index] : 0;
                    yield return new ChartBar(Number(Left + index * GroupWidth + GroupWidth * 0.1 + part * wide), Number(Y(value)), Number(wide), Number(Y(0) - Y(value)),
                        Series[part].Color, $"{Series[part].Name} {Labels[index]}: {Value(value)} {Unit}");
                }
        }
    }

    string CsvHref => ChartCsv.Href([LabelName, .. Series.Select(series => series.Name)],
        Labels.Select((label, index) => (IEnumerable<string>)[label, .. Series.Select(series => ChartCsv.Number(index < series.Values.Count ? series.Values[index] : 0))]));

    string CsvName => ChartCsv.FileName(Title);

    double Y(double value) => Top + (1 - value / MaxValue) * (Height - Top - Bottom);

    static string LegendStyle(BarSeries series) => $"color: {series.Color}";

    static string Number(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    static string Value(double value) => value.ToString("0.#", CultureInfo.CurrentCulture);

    static double NiceMax(double max)
    {
        if (max <= 0) return 1;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(max)));
        return new[] { 1.0, 2, 5, 10 }.Select(step => step * magnitude).First(top => top >= max);
    }

    /// <summary>A tick or label: where it sits and its text.</summary>
    sealed record AxisTick(string Position, string Label);

    /// <summary>A bar as drawn.</summary>
    sealed record ChartBar(string Across, string Down, string Wide, string High, string Color, string Tooltip);
}
