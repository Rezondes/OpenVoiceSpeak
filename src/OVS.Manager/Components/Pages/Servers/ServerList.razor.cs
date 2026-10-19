using Microsoft.AspNetCore.Components;
using OVS.Manager.Docker;
using OVS.Manager.Localization;
using OVS.Manager.Servers;

namespace OVS.Manager.Components.Pages.Servers;

/// <summary>
/// Package M8 (AC1, AC2): the overview, the start page: every visible server with status, version, port, users online
/// and uptime, live and with its buttons. Package M6 (AC4): it says when Docker is not reachable.
/// </summary>
sealed partial class ServerList
{
    [Inject]
    IDockerEngine Docker { get; set; } = null!;

    [Inject]
    ReleaseCatalog Catalog { get; set; } = null!;

    [Inject]
    ServerUpdate Updates { get; set; } = null!;

    [Inject]
    OVS.Manager.Telemetry.TelemetryLimit Limit { get; set; } = null!;

    [Inject]
    OVS.Manager.ManagerOptions Options { get; set; } = null!;

    [Inject]
    OVS.Manager.Telemetry.DashboardQuery Dashboard { get; set; } = null!;

    [Inject]
    OVS.Manager.Telemetry.ErrorQuery Errors { get; set; } = null!;

    [Inject]
    IHostDisk Disk { get; set; } = null!;

    /// <summary>Package M25 (AC3): the free space of Docker's data root, such as "120 GB (24 %)".</summary>
    string DiskText => Disk.Read() is { } space ? TextFormat.Of(Strings.Resources_DiskFreeValue, space.FreeBytes / 1e9, space.FreeShare * 100) : "-";

    static string DiskLowText(DiskSpace space) => TextFormat.Of(Strings.Resources_DiskLow, space.FreeShare * 100);

    /// <summary>Package M24 (AC3): each visible server's errors of the last 24 hours, when the page opened.</summary>
    IReadOnlyDictionary<string, int> ErrorCounts { get; set; } = new Dictionary<string, int>();

    string ErrorCount(ServerSnapshot server) => ErrorCounts.GetValueOrDefault(server.Id).ToString(System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>Package M23 (AC2): users online and bandwidth over the visible servers, when the page opened.</summary>
    OVS.Manager.Telemetry.DashboardFigures? Figures { get; set; }

    string UsersText => Figures is { } figures ? figures.UsersOnline.ToString("0", System.Globalization.CultureInfo.CurrentCulture) : "-";

    string BandwidthText => Figures is { } figures
        ? $"{figures.KbitIn.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture)} / {figures.KbitOut.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture)}" : "-";

    /// <summary>Package M23 (AC2): the ports the visible servers take, such as "37 / 100".</summary>
    string PortsText => $"{VisibleServers.Count()} / {PortPool.Count}";

    /// <summary>Package M23 (AC2): how many visible servers are in each state.</summary>
    string StatusText => string.Join(", ", VisibleServers.GroupBy(server => server.Status).OrderBy(group => group.Key)
        .Select(group => $"{group.Count()} {ServerTexts.Status(group.First() with { ExitCode = null })}"));

    /// <summary>Package M23 (AC2): how many visible servers have an update.</summary>
    string UpdatesText => VisibleServers.Count(server => Catalog.UpdateFor(server.Version) is not null).ToString(System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>Package M21 (AC4): the warning when the telemetry database is at its limit.</summary>
    string LimitText => TextFormat.Of(Strings.Telemetry_LimitReached, Options.TelemetryMaxBytes / (1024 * 1024));

    /// <summary>Package M18 (AC3): "Alle aktualisieren" runs.</summary>
    bool UpdatingAll { get; set; }

    /// <summary>Package M18 (AC3): how "Alle aktualisieren" ended, null before it ran.</summary>
    string? UpdateAllText { get; set; }

    /// <summary>Docker answered the check when the page opened.</summary>
    bool DockerReachable { get; set; } = true;

    /// <summary>Package M17 (AC1): "Update auf X verfügbar" when a newer release of the manager's protocol exists.</summary>
    string OfferText(ServerSnapshot server) => Catalog.UpdateFor(server.Version) is { } update ? TextFormat.Of(Strings.Versions_UpdateAvailable, update.Version) : "";

    /// <summary>Package M18 (AC3): updates every server with an offer, one after the other, until the first failure.</summary>
    async Task UpdateAllAsync()
    {
        UpdatingAll = true;
        var result = await Updates.UpdateAllAsync(null, (await Authentication).User.Identity?.Name ?? "");
        UpdatingAll = false;
        UpdateAllText = result.Error ?? TextFormat.Of(Strings.Update_AllDone, result.Updated);
    }

    /// <summary>Checks Docker once per page.</summary>
    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        DockerReachable = await Docker.IsReachableAsync();
        Figures = await Dashboard.ReadAsync([.. VisibleServers.Select(server => server.Id)]);
        ErrorCounts = await Errors.CountsAsync([.. VisibleServers.Select(server => server.Id)]);
    }
}
