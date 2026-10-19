using Microsoft.AspNetCore.Components;
using OVS.Manager.Localization;
using OVS.Manager.Telemetry;

namespace OVS.Manager.Components.Pages.Servers;

/// <summary>Package M23 (AC1): a server's connections, users, peak and bandwidth per day for 7 or 30 days or a free range, per month for 12 months.</summary>
sealed partial class History
{
    [Inject]
    HistoryQuery Query { get; set; } = null!;

    [Inject]
    TimeProvider Time { get; set; } = null!;

    /// <summary>The server's id.</summary>
    [Parameter]
    public string Id { get; set; } = "";

    /// <summary>The free range's first day.</summary>
    DateOnly From { get; set; }

    /// <summary>The free range's last day.</summary>
    DateOnly To { get; set; }

    /// <summary>The rows shown, a day or a month each.</summary>
    IReadOnlyList<HistoryRow> Rows { get; set; } = [];

    bool ByMonth { get; set; }

    string BucketName => ByMonth ? Strings.History_Month : Strings.History_Day;

    IReadOnlyList<string> Labels => [.. Rows.Select(row => row.Label)];

    IReadOnlyList<BarSeries> ConnectionsSeries => [new(Strings.History_Connections, "var(--accent)", [.. Rows.Select(row => (double)row.Connections)])];

    IReadOnlyList<BarSeries> UsersSeries =>
        [new(Strings.History_UsersTotal, "var(--accent)", [.. Rows.Select(row => (double)row.Users)]), new(Strings.History_UsersNew, "var(--muted)", [.. Rows.Select(row => (double)row.NewUsers)])];

    IReadOnlyList<BarSeries> PeakSeries => [new(Strings.History_Peak, "var(--accent)", [.. Rows.Select(row => (double)row.PeakUsers)])];

    IReadOnlyList<BarSeries> BandwidthSeries =>
        [new(Strings.Live_In, "var(--accent)", [.. Rows.Select(row => row.MegabytesIn)]), new(Strings.Live_Out, "var(--muted)", [.. Rows.Select(row => row.MegabytesOut)])];

    /// <summary>The finished days up to yesterday.</summary>
    DateOnly Yesterday => DateOnly.FromDateTime(Time.GetUtcNow().UtcDateTime).AddDays(-1);

    /// <summary>Shows the last 7 days.</summary>
    protected override Task OnInitializedAsync() => ShowWeekAsync();

    Task ShowWeekAsync() => ShowAsync(Yesterday.AddDays(-6), Yesterday, byMonth: false);

    Task ShowMonthAsync() => ShowAsync(Yesterday.AddDays(-29), Yesterday, byMonth: false);

    Task ShowYearAsync() => ShowAsync(new DateOnly(Yesterday.Year, Yesterday.Month, 1).AddMonths(-11), Yesterday, byMonth: true);

    /// <summary>The free range; longer than about two months goes by month.</summary>
    Task ShowRangeAsync() => To < From ? Task.CompletedTask : ShowAsync(From, To, byMonth: To.DayNumber - From.DayNumber > 62);

    async Task ShowAsync(DateOnly from, DateOnly to, bool byMonth)
    {
        (From, To, ByMonth) = (from, to, byMonth);
        Rows = await Query.ReadAsync(Id, from, to, byMonth);
    }
}
