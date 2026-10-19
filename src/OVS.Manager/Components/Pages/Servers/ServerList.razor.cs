using Microsoft.AspNetCore.Components;
using OVS.Manager.Docker;

namespace OVS.Manager.Components.Pages.Servers;

/// <summary>
/// Package M8 (AC1, AC2): the overview, the start page: every visible server with status, version, port, users online
/// and uptime, live and with its buttons. Package M6 (AC4): it says when Docker is not reachable.
/// </summary>
sealed partial class ServerList
{
    [Inject]
    IDockerEngine Docker { get; set; } = null!;

    /// <summary>Docker answered the check when the page opened.</summary>
    bool DockerReachable { get; set; } = true;

    /// <summary>Checks Docker once per page.</summary>
    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        DockerReachable = await Docker.IsReachableAsync();
    }
}
