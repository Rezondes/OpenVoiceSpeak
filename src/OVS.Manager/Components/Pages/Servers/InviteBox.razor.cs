using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using OVS.Manager.Data;
using OVS.Manager.Localization;
using OVS.Manager.Servers;
using OVS.Shared.Net;

namespace OVS.Manager.Components.Pages.Servers;

/// <summary>Package M14 (AC1, AC2): the server's join link and web link to copy, and the pinned certificate fingerprint beside them.</summary>
sealed partial class InviteBox
{
    [Inject]
    ManagerOptions Options { get; set; } = null!;

    [Inject]
    ManagerDbContext Database { get; set; } = null!;

    [Inject]
    NavigationManager Navigation { get; set; } = null!;

    /// <summary>The server's id.</summary>
    [Parameter, EditorRequired]
    public string ServerId { get; set; } = "";

    /// <summary>The server's port.</summary>
    [Parameter, EditorRequired]
    public int Port { get; set; }

    /// <summary>The link, null for a host a link cannot carry.</summary>
    JoinLink? Link { get; set; }

    /// <summary>The fingerprint pinned at the first connect (M11), "–" before it.</summary>
    string FingerprintText { get; set; } = "";

    /// <summary>Builds the link and loads the fingerprint.</summary>
    protected override async Task OnParametersSetAsync()
    {
        Link = InviteLink.For(Options, new Uri(Navigation.BaseUri).Host, Port);
        var pinned = await Database.Servers.AsNoTracking().Where(server => server.Id == ServerId).Select(server => server.CertificateFingerprint).SingleOrDefaultAsync();
        FingerprintText = pinned ?? Strings.Link_Offline;
    }
}
