using System.Globalization;
using Microsoft.AspNetCore.Components;
using OVS.Manager.Localization;
using OVS.Manager.Servers;

namespace OVS.Manager.Components.Layout;

/// <summary>
/// Package M17 (AC2, AC4): says when a newer manager is out, with the compose command to update it, and when the version
/// check could not reach GitHub (with the age of the last known catalog).
/// </summary>
sealed partial class ManagerUpdateBanner
{
    /// <summary>The compose command that updates the manager (deploy/manager/docker-compose.yml).</summary>
    const string UpdateCommand = "docker compose pull ovs-manager && docker compose up -d ovs-manager";

    [Inject]
    ReleaseCatalog Catalog { get; set; } = null!;

    [Inject]
    TimeProvider Time { get; set; } = null!;

    /// <summary>The state of the version check, null while it is fresh.</summary>
    string? CheckText => !Catalog.LastFetchFailed ? null : Catalog.FetchedAt is { } fetched
        ? TextFormat.Of(Strings.Versions_Stale, (int)(Time.GetUtcNow() - fetched).TotalHours)
        : Strings.Versions_CheckFailed;

    /// <summary>Follows the catalog.</summary>
    protected override void OnInitialized() => Catalog.Changed += Redraw;

    /// <summary>Stops following the catalog.</summary>
    public void Dispose() => Catalog.Changed -= Redraw;

    void Redraw() => _ = InvokeAsync(StateHasChanged);

    static string UpdateText(Release update) => TextFormat.Of(Strings.Versions_ManagerUpdate, update.Version.ToString(CultureInfo.InvariantCulture));
}
