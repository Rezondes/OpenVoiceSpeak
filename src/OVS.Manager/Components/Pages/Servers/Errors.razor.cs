using System.Globalization;
using Microsoft.AspNetCore.Components;
using OVS.Manager.Telemetry;

namespace OVS.Manager.Components.Pages.Servers;

/// <summary>Package M24 (AC1): a server's errors grouped by message with their count; one opens with its whole stack trace.</summary>
sealed partial class Errors
{
    [Inject]
    ErrorQuery Query { get; set; } = null!;

    /// <summary>The server's id.</summary>
    [Parameter]
    public string Id { get; set; } = "";

    IReadOnlyList<ErrorGroup> Groups { get; set; } = [];

    /// <summary>The error shown with its stack trace, null before one is opened.</summary>
    ErrorEntry? Opened { get; set; }

    /// <summary>Loads the groups.</summary>
    protected override async Task OnParametersSetAsync() => Groups = await Query.GroupsAsync(Id);

    Action OpenFor(ErrorGroup group) => () => Opened = group.Entries[0];

    static string NewestId(ErrorGroup group) => group.Entries[0].ErrorId;

    static string Clock(ErrorEntry entry) => DateTimeOffset.FromUnixTimeMilliseconds(entry.AtUnixMs).ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
}
