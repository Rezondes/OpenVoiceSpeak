using System.Globalization;
using Microsoft.AspNetCore.Components;
using OVS.Manager.Localization;
using OVS.Manager.Telemetry;

namespace OVS.Manager.Components.Pages.Servers;

/// <summary>
/// Package M22 (AC1, AC3): a server's tiles and lines of the last 15 minutes, read again every 5 s without a reload. CPU and
/// RAM come from Docker and are kept here from the page's opening on.
/// </summary>
sealed partial class LiveView : IAsyncDisposable
{
    /// <summary>How often the page reads the feed.</summary>
    public static readonly TimeSpan Every = TimeSpan.FromSeconds(5);

    readonly CancellationTokenSource stop = new();
    readonly List<ChartPoint> cpu = [];
    readonly List<ChartPoint> memory = [];
    Task? following;

    [Inject]
    LiveFeed Feed { get; set; } = null!;

    [Inject]
    TimeProvider Time { get; set; } = null!;

    /// <summary>The server's id.</summary>
    [Parameter, EditorRequired]
    public string ServerId { get; set; } = "";

    /// <summary>The newest figures, null before the first read.</summary>
    LiveSnapshot? Snapshot { get; set; }

    IReadOnlyList<ChartSeries> UsersLines => [new(Strings.Live_Users, "var(--accent)", Snapshot!.Users)];

    IReadOnlyList<ChartSeries> BandwidthLines => [new(Strings.Live_In, "var(--accent)", Snapshot!.KbitInLine), new(Strings.Live_Out, "var(--muted)", Snapshot.KbitOutLine)];

    IReadOnlyList<ChartSeries> DropsLines => [new(Strings.Live_Drops, "var(--accent)", Snapshot!.DropsLine)];

    IReadOnlyList<ChartSeries> CpuLines => [new(Strings.Live_Cpu, "var(--accent)", cpu)];

    IReadOnlyList<ChartSeries> MemoryLines => [new(Strings.Live_Ram, "var(--accent)", memory)];

    /// <summary>Reads once and then every <see cref="Every"/>.</summary>
    protected override async Task OnInitializedAsync()
    {
        await ReadAsync();
        following = FollowAsync();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await stop.CancelAsync();
        if (following is not null) await following;
        stop.Dispose();
    }

    async Task FollowAsync()
    {
        using var timer = new PeriodicTimer(Every, Time);
        try
        {
            while (await timer.WaitForNextTickAsync(stop.Token))
            {
                await ReadAsync();
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException)
        {
            // the page left
        }
    }

    async Task ReadAsync()
    {
        var snapshot = await Feed.ReadAsync(ServerId, stop.Token);
        if (snapshot.Usage is { } usage)
        {
            cpu.Add(new ChartPoint(snapshot.ToUnixMs, usage.CpuPercent));
            memory.Add(new ChartPoint(snapshot.ToUnixMs, usage.MemoryBytes / 1048576.0));
        }
        cpu.RemoveAll(point => point.AtUnixMs < snapshot.FromUnixMs);
        memory.RemoveAll(point => point.AtUnixMs < snapshot.FromUnixMs);
        Snapshot = snapshot;
    }

    static double? MemoryMb(LiveSnapshot snapshot) => snapshot.Usage?.MemoryBytes / 1048576.0;

    static string Number(double? value) => value is { } number ? number.ToString("0.#", CultureInfo.CurrentCulture) : "-";
}
