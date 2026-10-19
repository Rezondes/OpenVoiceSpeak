using Microsoft.AspNetCore.Components;
using OVS.Manager.Servers;

namespace OVS.Manager.Components.Pages.Servers;

/// <summary>Package M8 (AC1, AC2): one server's page with its status, live, and its buttons.</summary>
sealed partial class ServerPage
{
    /// <summary>The server's id from the address.</summary>
    [Parameter]
    public string Id { get; set; } = "";

    /// <summary>The server at the last check, null when the user may not see it or it is gone.</summary>
    ServerSnapshot? Server => VisibleServers.FirstOrDefault(server => server.Id == Id);

    /// <summary>Package M11 (AC3): the admin pins the server's new certificate.</summary>
    async Task AcceptCertificateAsync() => await Connections.AcceptCertificateAsync(Id, (await Authentication).User.Identity?.Name ?? "");
}
