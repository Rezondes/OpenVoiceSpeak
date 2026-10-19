using Microsoft.AspNetCore.Components;
using OVS.Manager.Docker;

namespace OVS.Manager.Components.Pages;

/// <summary>Package M6 (AC4): the start page says when Docker is not reachable; the server overview comes with M8.</summary>
sealed partial class Home
{
    [Inject]
    IDockerEngine Docker { get; set; } = null!;

    /// <summary>Docker answered the last check.</summary>
    bool DockerReachable { get; set; } = true;

    /// <summary>Checks Docker once per page.</summary>
    protected override async Task OnInitializedAsync() => DockerReachable = await Docker.IsReachableAsync();
}
