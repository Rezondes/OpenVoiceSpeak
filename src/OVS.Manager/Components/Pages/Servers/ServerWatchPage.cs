using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using OVS.Manager.Control;
using OVS.Manager.Servers;

namespace OVS.Manager.Components.Pages.Servers;

/// <summary>Package M8 (AC1): a page with the servers the user may see, drawn again after every check of the watcher.</summary>
#pragma warning disable CA1515 // public: Razor generates the pages that inherit it as public classes
public abstract class ServerWatchPage : ComponentBase, IDisposable
#pragma warning restore CA1515
{
    IReadOnlySet<string>? visible = new HashSet<string>();

    /// <summary>The status of every server.</summary>
    [Inject]
    internal StatusWatcher Watcher { get; set; } = null!;

    /// <summary>The clock of the uptime.</summary>
    [Inject]
    internal TimeProvider Time { get; set; } = null!;

    /// <summary>Package M11: the service sessions with the servers' mirrors.</summary>
    [Inject]
    internal ServerConnections Connections { get; set; } = null!;

    [Inject]
    ServerAccess Access { get; set; } = null!;

    /// <summary>Who looks at the page.</summary>
    [CascadingParameter]
    internal Task<AuthenticationState> Authentication { get; set; } = null!;

    /// <summary>The servers of the last check the user may see: the admin all, a manager their assigned ones (M10).</summary>
    internal IEnumerable<ServerSnapshot> VisibleServers => Watcher.Servers.Where(server => visible is null || visible.Contains(server.Id));

    /// <summary>Finds what the user may see and follows the watcher.</summary>
    protected override async Task OnInitializedAsync()
    {
        visible = await Access.VisibleAsync((await Authentication).User);
        Watcher.Changed += Redraw;
        Connections.Changed += Redraw;
    }

    /// <summary>Stops following the watcher.</summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Stops following the watcher.</summary>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing) return;
        Watcher.Changed -= Redraw;
        Connections.Changed -= Redraw;
    }

    void Redraw() => _ = InvokeAsync(StateHasChanged);
}
