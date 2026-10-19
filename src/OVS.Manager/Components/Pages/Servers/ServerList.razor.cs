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
    }
}
