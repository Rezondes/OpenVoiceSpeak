using Microsoft.AspNetCore.Components;
using OVS.Manager.Servers;

namespace OVS.Manager.Components.Pages.Servers;

/// <summary>A server's tabs to its sub-pages, with the way back to the server page on each sub-page: one bar for the server page and all its sub-pages.</summary>
sealed partial class ServerNav
{
    [Inject]
    StatusWatcher Watcher { get; set; } = null!;

    /// <summary>The server's id.</summary>
    [Parameter, EditorRequired]
    public string ServerId { get; set; } = "";

    /// <summary>Shows the way back to the server page; the server page itself leaves it out.</summary>
    [Parameter]
    public bool WithBack { get; set; } = true;

    /// <summary>The server's name at the watcher's last check, its id when the watcher does not know it.</summary>
    string Name => Watcher.Servers.FirstOrDefault(server => server.Id == ServerId)?.Name ?? ServerId;

    string Tab(string page) => $"/servers/{ServerId}/{page}";
}
