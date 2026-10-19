using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using OVS.Manager.Audit;
using OVS.Manager.Data;
using OVS.Manager.Localization;
using OVS.Manager.Servers;

namespace OVS.Manager.Components.Pages.Servers;

/// <summary>
/// Package M18 (AC1, AC4, AC5): the offered update of one server with its steps live, and the admin's maintenance window
/// (automatic updates off until the admin turns them on).
/// </summary>
sealed partial class UpdateDialog
{
    static readonly DayOfWeek[] DayList = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];

    [Inject]
    ServerUpdate Updates { get; set; } = null!;

    [Inject]
    ReleaseCatalog Catalog { get; set; } = null!;

    [Inject]
    ManagerDbContext Database { get; set; } = null!;

    [Inject]
    AuditLog Audit { get; set; } = null!;

    [CascadingParameter]
    Task<AuthenticationState> Authentication { get; set; } = null!;

    /// <summary>The server's id.</summary>
    [Parameter, EditorRequired]
    public string ServerId { get; set; } = "";

    /// <summary>The version the server runs.</summary>
    [Parameter, EditorRequired]
    public string Version { get; set; } = "";

    /// <summary>The release offered, null when there is none.</summary>
    Release? Offer => Catalog.UpdateFor(Version);

    /// <summary>The steps done so far.</summary>
    List<UpdateStep> Steps { get; } = [];

    /// <summary>An update runs.</summary>
    bool Running { get; set; }

    /// <summary>How the last update ended, null before one.</summary>
    string? Result { get; set; }

    /// <summary>The last update failed.</summary>
    bool Failed { get; set; }

    /// <summary>The server's maintenance window in the form.</summary>
    UpdateWindow Window { get; set; } = new();

    static IReadOnlyList<DayOfWeek> Days => DayList;

    string ResultClass => Failed ? "form-error" : "form-ok";

    /// <summary>Loads the window.</summary>
    protected override async Task OnParametersSetAsync() =>
        Window = await Database.UpdateWindows.AsNoTracking().SingleOrDefaultAsync(row => row.ServerId == ServerId) ?? new UpdateWindow { ServerId = ServerId };

    async Task UpdateAsync()
    {
        if (Offer is not { } offer) return;
        Running = true;
        Steps.Clear();
        Result = null;
        var progress = new Progress<UpdateStep>(step =>
        {
            Steps.Add(step);
            _ = InvokeAsync(StateHasChanged);
        });
        var failed = await Updates.UpdateAsync(ServerId, offer.Version, await ActorAsync(), progress);
        Running = false;
        Failed = failed is not null;
        Result = failed ?? TextFormat.Of(Strings.Update_Done, offer.Version);
    }

    async Task SaveWindowAsync()
    {
        Window.ServerId = ServerId;
        if (await Database.UpdateWindows.AnyAsync(row => row.ServerId == ServerId)) Database.UpdateWindows.Update(Window);
        else Database.UpdateWindows.Add(Window);
        await Database.SaveChangesAsync();
        Database.ChangeTracker.Clear();
        await Audit.WriteAsync(await ActorAsync(), AuditActions.UpdateWindowChanged, Window.AutoUpdate ? "on" : "off", ServerId, $"{Window.Day} {Window.Time:HH\\:mm}");
        Result = Strings.Settings_Saved;
        Failed = false;
    }

    static string OfferText(Release offer) => TextFormat.Of(Strings.Update_To, offer.Version);

    static string StepText(UpdateStep step) => Strings.ResourceManager.GetString("UpdateStep_" + step, Strings.Culture) ?? step.ToString();

    static string DayName(DayOfWeek day) => CultureInfo.CurrentCulture.DateTimeFormat.GetDayName(day);

    async Task<string> ActorAsync() => (await Authentication).User.Identity?.Name ?? "";
}
