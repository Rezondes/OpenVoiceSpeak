using Bunit;
using Microsoft.Extensions.DependencyInjection;
using OVS.Manager.Components.Charts;
using OVS.Manager.Components.Pages.Servers;
using OVS.Manager.Telemetry;
using OVS.Tests.TestSupport;
using static OVS.Tests.Manager.Telemetry.TelemetryTesting;

namespace OVS.Tests.Manager.Components;

/// <summary>Package M23: a server's history per day and month, and the CSV of a chart.</summary>
public sealed class HistoryTests
{
    static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    static async Task<IReadOnlyList<HistoryRow>> ReadAsync(ManagerFactory manager, string serverId, DateOnly from, DateOnly to, bool byMonth)
    {
        await using var scope = manager.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<HistoryQuery>().ReadAsync(serverId, from, to, byMonth);
    }

    /// <summary>AC1: days come from the daily statistics with their peak, months add them up, a day without data is 0; the page draws the four charts.</summary>
    [Fact]
    public async Task History_Ranges_ValuesFromDailyStats()
    {
        await using var manager = TelemetryManager(new ManualTimeProvider(Now));
        manager.CreateClient().Dispose();
        var id = (await ServerLinks.AddAsync(manager)).Id; // time zone UTC
        DateOnly first = new(2026, 9, 30), second = new(2026, 10, 1);
        await AddAsync(manager,
            new DailyStats { ServerId = id, Day = first, Connections = 10, Users = 4, NewUsers = 2, BytesIn = 3_000_000, BytesOut = 1_000_000 },
            new DailyStats { ServerId = id, Day = second, Connections = 5, Users = 6, NewUsers = 1, BytesIn = 2_000_000 },
            new MetricRollup { ServerId = id, Name = "ovs.sessions", Kind = MetricKind.Gauge, Level = RollupLevel.Hour, PeriodUnixMs = Ms(new DateTimeOffset(2026, 10, 1, 20, 0, 0, TimeSpan.Zero)), Max = 3 });

        var days = await ReadAsync(manager, id, first, second.AddDays(1), byMonth: false);
        Assert.Equal([(10L, 4L, 2L, 0L, 3.0, 1.0), (5L, 6L, 1L, 3L, 2.0, 0.0), (0L, 0L, 0L, 0L, 0.0, 0.0)],
            days.Select(row => (row.Connections, row.Users, row.NewUsers, row.PeakUsers, row.MegabytesIn, row.MegabytesOut)));
        var months = await ReadAsync(manager, id, first, second, byMonth: true);
        Assert.Equal(2, months.Count);

        await using var context = ServerPages.Context(manager);
        context.Services.AddSingleton<TimeProvider>(new ManualTimeProvider(Now));
        var page = context.Render<History>(parameters => parameters.Add(history => history.Id, id));
        Assert.Equal(4, page.FindAll("[data-chart]").Count);
        Assert.Equal(7, page.FindComponents<BarChart>()[0].Instance.Labels.Count);
        await page.Find("[data-range='12']").ClickAsync(new());
        Assert.Equal(12, page.FindComponents<BarChart>()[0].Instance.Labels.Count);
    }

    /// <summary>AC4: a chart's CSV link holds its labels and the values of each series.</summary>
    [Fact]
    public void Csv_Download_LabelsAndValues()
    {
        using var context = new BunitContext();
        var chart = context.Render<BarChart>(parameters => parameters.Add(bar => bar.Title, "Bandbreite pro Tag").Add(bar => bar.Unit, "MB").Add(bar => bar.LabelName, "Tag")
            .Add(bar => bar.Labels, ["30.09.2026", "01.10.2026"]).Add(bar => bar.Series, [new BarSeries("Ein", "var(--accent)", [3, 2.5]), new BarSeries("Aus", "var(--muted)", [1, 0])]));

        var link = chart.Find("a[data-csv]");
        Assert.Equal("Bandbreite-pro-Tag.csv", link.GetAttribute("download"));
        var href = link.GetAttribute("href")!;
        Assert.StartsWith("data:text/csv;charset=utf-8,", href, StringComparison.Ordinal);
        Assert.Equal("Tag,Ein,Aus\r\n30.09.2026,3,1\r\n01.10.2026,2.5,0\r\n", Uri.UnescapeDataString(href["data:text/csv;charset=utf-8,".Length..]));
        Assert.Equal(4, chart.FindAll("rect[data-bar] title").Count);
    }
}
