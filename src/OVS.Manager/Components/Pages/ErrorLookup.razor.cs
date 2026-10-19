using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using OVS.Manager.Data;
using OVS.Manager.Localization;
using OVS.Manager.Servers;
using OVS.Manager.Telemetry;

namespace OVS.Manager.Components.Pages;

/// <summary>Package M24 (AC2): an error ID from a user opens its error and trace, on any server the user may see.</summary>
sealed partial class ErrorLookup
{
    [Inject]
    ErrorQuery Query { get; set; } = null!;

    [Inject]
    ServerAccess Access { get; set; } = null!;

    [Inject]
    ManagerDbContext Database { get; set; } = null!;

    [CascadingParameter]
    Task<AuthenticationState> Authentication { get; set; } = null!;

    /// <summary>The ID typed in.</summary>
    string ErrorId { get; set; } = "";

    /// <summary>A lookup ran.</summary>
    bool Searched { get; set; }

    /// <summary>The error found, null when none was.</summary>
    ErrorDetail? Found { get; set; }

    /// <summary>The name of the error's server.</summary>
    string ServerName { get; set; } = "";

    async Task LookupAsync()
    {
        Found = await Query.LookupAsync(ErrorId, await Access.VisibleAsync((await Authentication).User));
        Searched = true;
        if (Found is { } found)
            ServerName = await Database.Servers.AsNoTracking().Where(server => server.Id == found.ServerId).Select(server => server.Name).SingleOrDefaultAsync() ?? found.ServerId;
    }

    static string Clock(long unixMs) => DateTimeOffset.FromUnixTimeMilliseconds(unixMs).ToLocalTime().ToString("G", CultureInfo.CurrentCulture);

    static string Milliseconds(SpanRow span) => TextFormat.Of(Strings.ErrorLookup_Ms, span.DurationMs.ToString("0.#", CultureInfo.CurrentCulture));

    static string SpanStatus(SpanRow span) => span.Failed ? Strings.ErrorLookup_Failed : Strings.ErrorLookup_Ok;
}
