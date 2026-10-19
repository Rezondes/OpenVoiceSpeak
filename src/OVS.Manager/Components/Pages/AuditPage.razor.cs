using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using OVS.Manager.Audit;
using OVS.Manager.Servers;

namespace OVS.Manager.Components.Pages;

/// <summary>Package M5 (AC1, AC3): the audit lines in local time, filtered by user, server, action and days.</summary>
sealed partial class AuditPage
{
    [Inject]
    AuditLog Audit { get; set; } = null!;

    [Inject]
    TimeProvider Time { get; set; } = null!;

    [Inject]
    ServerAccess Access { get; set; } = null!;

    [CascadingParameter]
    HttpContext HttpContext { get; set; } = null!;

    [SupplyParameterFromQuery(Name = "user")]
    string? User { get; set; }

    [SupplyParameterFromQuery(Name = "server")]
    string? Server { get; set; }

    [SupplyParameterFromQuery(Name = "action")]
    string? Action { get; set; }

    [SupplyParameterFromQuery(Name = "from")]
    string? From { get; set; }

    [SupplyParameterFromQuery(Name = "to")]
    string? To { get; set; }

    /// <summary>The lines of the filter, newest first.</summary>
    List<AuditEntry> Lines { get; set; } = [];

    /// <summary>The same filter as CSV.</summary>
    string ExportLink => "/audit/export" + HttpContext.Request.QueryString;

    /// <summary>Loads the lines the user may read.</summary>
    protected override async Task OnInitializedAsync() =>
        Lines = await Audit.QueryAsync(AuditEndpoints.Filter(User, Server, Action, From, To, Time.LocalTimeZone), await Access.VisibleAsync(HttpContext.User));

    string LocalTime(AuditEntry entry) => TimeZoneInfo.ConvertTimeFromUtc(entry.AtUtc, Time.LocalTimeZone).ToString("g", CultureInfo.CurrentCulture);
}
