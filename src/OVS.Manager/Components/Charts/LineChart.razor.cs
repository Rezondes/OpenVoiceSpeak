using System.Globalization;
using Microsoft.AspNetCore.Components;
using OVS.Manager.Telemetry;

namespace OVS.Manager.Components.Charts;

/// <summary>Package M22 (AC2, MA1): an SVG line chart with axes in its unit, a tooltip per point and the colours of the M2 tokens.</summary>
sealed partial class LineChart
{
    const double Width = 600, Height = 180, Left = 56, Right = 8, Top = 8, Bottom = 24;

    /// <summary>The chart's title.</summary>
    [Parameter, EditorRequired]
    public string Title { get; set; } = "";

    /// <summary>The unit of the values, on the axis and in the tooltips.</summary>
    [Parameter, EditorRequired]
    public string Unit { get; set; } = "";

    /// <summary>The lines.</summary>
    [Parameter, EditorRequired]
    public IReadOnlyList<ChartSeries> Series { get; set; } = [];

    /// <summary>The left end of the time axis in Unix milliseconds.</summary>
    [Parameter, EditorRequired]
    public long FromUnixMs { get; set; }

    /// <summary>The right end of the time axis in Unix milliseconds.</summary>
    [Parameter, EditorRequired]
    public long ToUnixMs { get; set; }

    double MaxValue => NiceMax(Series.SelectMany(series => series.Points).Select(point => point.Value).DefaultIfEmpty(0).Max());

    IEnumerable<AxisTick> YTicks => new[] { 0, MaxValue / 2, MaxValue }.Select(value => new AxisTick(Number(Y(value)), $"{Value(value)} {Unit}"));

    /// <summary>A label every 5 minutes, in local time.</summary>
    IEnumerable<AxisTick> XTicks
    {
        get
        {
            const long Step = 5 * 60_000;
            for (long at = (FromUnixMs + Step - 1) / Step * Step; at <= ToUnixMs; at += Step)
                yield return new AxisTick(Number(X(at)), Clock(at, "HH:mm"));
        }
    }

    IEnumerable<ChartLine> Lines => Series.Select(series => new ChartLine(series.Color,
        string.Join(" ", series.Points.Select(point => $"{Number(X(point.AtUnixMs))},{Number(Y(point.Value))}")),
        [.. series.Points.Select(point => new ChartDot(Number(X(point.AtUnixMs)), Number(Y(point.Value)), $"{series.Name} {Clock(point.AtUnixMs, "HH:mm:ss")}: {Value(point.Value)} {Unit}"))]));

    /// <summary>Package M23 (AC4): one row per point, with its series and its time in UTC.</summary>
    string CsvHref => ChartCsv.Href(["series", "time_utc", "value"], Series.SelectMany(series => series.Points.Select(point =>
        (IEnumerable<string>)[series.Name, DateTimeOffset.FromUnixTimeMilliseconds(point.AtUnixMs).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture), ChartCsv.Number(point.Value)])));

    string CsvName => ChartCsv.FileName(Title);

    double X(long at) => Left + (at - FromUnixMs) / (double)Math.Max(1, ToUnixMs - FromUnixMs) * (Width - Left - Right);

    double Y(double value) => Top + (1 - value / MaxValue) * (Height - Top - Bottom);

    static string LegendStyle(ChartSeries series) => $"color: {series.Color}";

    static string Number(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    static string Value(double value) => value.ToString("0.#", CultureInfo.CurrentCulture);

    static string Clock(long at, string format) => DateTimeOffset.FromUnixTimeMilliseconds(at).ToLocalTime().ToString(format, CultureInfo.CurrentCulture);

    /// <summary>The top of the value axis: 1, 2 or 5 times a power of ten, at least the largest value.</summary>
    static double NiceMax(double max)
    {
        if (max <= 0) return 1;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(max)));
        return new[] { 1.0, 2, 5, 10 }.Select(step => step * magnitude).First(top => top >= max);
    }

    /// <summary>A tick: where it sits and its label.</summary>
    sealed record AxisTick(string Position, string Label);

    /// <summary>A line as drawn: its colour, its polyline points and its dots.</summary>
    sealed record ChartLine(string Color, string Points, IReadOnlyList<ChartDot> Dots);

    /// <summary>A dot with its tooltip.</summary>
    sealed record ChartDot(string Across, string Down, string Tooltip);
}
