using Bunit;
using OVS.Manager.Components.Charts;
using OVS.Manager.Telemetry;

namespace OVS.Tests.Manager.Components;

/// <summary>Package M22: the own SVG charts and tiles.</summary>
public sealed class ChartTests
{
    static readonly long From = new DateTimeOffset(2026, 10, 19, 10, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

    static IRenderedComponent<LineChart> Chart(BunitContext context, params ChartSeries[] series) =>
        context.Render<LineChart>(parameters => parameters.Add(chart => chart.Title, "Bandbreite").Add(chart => chart.Unit, "kbit/s")
            .Add(chart => chart.Series, series).Add(chart => chart.FromUnixMs, From).Add(chart => chart.ToUnixMs, From + 15 * 60_000));

    static ChartSeries Line(string name, string color, params double[] values) =>
        new(name, color, [.. values.Select((value, index) => new ChartPoint(From + index * 60_000, value))]);

    /// <summary>AC1: a tile shows its label, its figure and its unit.</summary>
    [Fact]
    public void Tiles_Values_LabelValueUnit()
    {
        using var context = new BunitContext();
        var tile = context.Render<StatTile>(parameters => parameters.Add(tile => tile.Name, "in").Add(tile => tile.Label, "Eingehend").Add(tile => tile.Value, "12,5").Add(tile => tile.Unit, "kbit/s"));
        Assert.Equal("12,5", tile.Find("[data-tile=in] [data-value]").TextContent);
        Assert.Contains("Eingehend", tile.Markup, StringComparison.Ordinal);
        Assert.Contains("kbit/s", tile.Markup, StringComparison.Ordinal);
    }

    /// <summary>AC2: the value axis carries the unit, the time axis clock times, and every point has a tooltip with its value.</summary>
    [Fact]
    public void Axes_UnitsAndTooltip_EachPoint()
    {
        using var context = new BunitContext();
        var chart = Chart(context, Line("Eingehend", "var(--accent)", 3, 7.5, 12));

        var ticks = chart.FindAll("[data-y-tick]").Select(tick => tick.TextContent).ToList();
        Assert.Equal(3, ticks.Count);
        Assert.All(ticks, tick => Assert.EndsWith(" kbit/s", tick, StringComparison.Ordinal));
        Assert.Contains(ticks, tick => tick.StartsWith("20 ", StringComparison.Ordinal)); // 12 rounds up to a top of 20
        Assert.Equal(4, chart.FindAll("[data-x-tick]").Count); // every 5 minutes over 15
        var tooltips = chart.FindAll("circle title").Select(title => title.TextContent).ToList();
        Assert.Equal(3, tooltips.Count);
        Assert.All(tooltips, tooltip => Assert.EndsWith(" kbit/s", tooltip, StringComparison.Ordinal));
        Assert.Contains(tooltips, tooltip => tooltip.StartsWith("Eingehend ", StringComparison.Ordinal) && tooltip.Contains("12", StringComparison.Ordinal));
    }

    /// <summary>AC2: every colour comes from a token of M2, so the chart reads in light and dark.</summary>
    [Fact]
    public void Colors_FromTokens_NoFixedColor()
    {
        using var context = new BunitContext();
        var chart = Chart(context, Line("Eingehend", "var(--accent)", 1, 2), Line("Ausgehend", "var(--muted)", 2, 1));

        var colors = chart.FindAll("[stroke], [fill]").SelectMany(element => new[] { element.GetAttribute("stroke"), element.GetAttribute("fill") })
            .Where(color => color is not null and not "none").ToList();
        Assert.NotEmpty(colors);
        Assert.All(colors, color => Assert.StartsWith("var(--", color, StringComparison.Ordinal));
        Assert.DoesNotContain("#", chart.Markup, StringComparison.Ordinal);
    }
}
