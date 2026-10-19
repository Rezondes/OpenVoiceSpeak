using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using OVS.Manager.Control;
using OVS.Manager.Exceptions;
using OVS.Manager.Localization;
using OVS.Manager.Servers;

namespace OVS.Manager.Components.Pages.Servers;

/// <summary>
/// Package M8 (AC2): start, stop and restart of one server; off while an action on it runs. Package M19 (AC1, AC2): stop and
/// restart with a lead time announced in the server chat, and the pending action (also an update) to cancel.
/// </summary>
sealed partial class ServerActions : IDisposable
{
    [Inject]
    ServerLifecycle Lifecycle { get; set; } = null!;

    [Inject]
    Announcement Announcements { get; set; } = null!;

    [CascadingParameter]
    Task<AuthenticationState> Authentication { get; set; } = null!;

    /// <summary>The server's id.</summary>
    [Parameter, EditorRequired]
    public string ServerId { get; set; } = "";

    /// <summary>An action on the server runs, here or in another browser.</summary>
    [Parameter]
    public bool Busy { get; set; }

    /// <summary>Why the last action failed, null when it did not.</summary>
    string? Error { get; set; }

    /// <summary>Package M19 (AC1): the lead time of a stop or restart in minutes, 0 is at once.</summary>
    int LeadMinutes { get; set; }

    /// <summary>Package M19: the action announced for the server, null when none is.</summary>
    PendingAnnouncement? Pending => Announcements.PendingOf(ServerId);

    /// <summary>Busy, or an action is announced.</summary>
    bool Locked => Busy || Pending is not null;

    static IReadOnlyList<int> LeadOptions => Announcement.LeadOptions;

    /// <summary>Redraws when the server's pending action comes or goes.</summary>
    protected override void OnInitialized() => Announcements.Changed += OnAnnouncementChanged;

    /// <inheritdoc/>
    public void Dispose() => Announcements.Changed -= OnAnnouncementChanged;

    Task StartAsync() => RunAsync(Lifecycle.StartAsync);

    Task StopAsync() => LeadMinutes == 0 ? RunAsync(Lifecycle.StopAsync) : ScheduleAsync(AnnouncedAction.Stop);

    Task RestartAsync() => LeadMinutes == 0 ? RunAsync(Lifecycle.RestartAsync) : ScheduleAsync(AnnouncedAction.Restart);

    async Task ScheduleAsync(AnnouncedAction action)
    {
        Error = null;
        var actor = (await Authentication).User.Identity?.Name ?? "";
        var serverId = ServerId;
        if (!Announcements.Schedule(serverId, action, LeadMinutes, (services, cancellation) => action == AnnouncedAction.Stop
                ? services.GetRequiredService<ServerLifecycle>().StopAsync(serverId, actor, cancellation)
                : services.GetRequiredService<ServerLifecycle>().RestartAsync(serverId, actor, cancellation)))
            Error = Strings.Announce_AlreadyPending;
    }

    async Task CancelAsync() => await Announcements.CancelAsync(ServerId);

    void OnAnnouncementChanged(string serverId)
    {
        if (serverId == ServerId) _ = InvokeAsync(StateHasChanged);
    }

    static string LeadText(int minutes) => minutes switch
    {
        0 => Strings.Announce_Now,
        1 => Strings.Announce_InOneMinute,
        _ => TextFormat.Of(Strings.Announce_InMinutes, minutes),
    };

    static string PendingText(PendingAnnouncement pending) => TextFormat.Of(Strings.Announce_Pending, pending.Action switch
    {
        AnnouncedAction.Restart => Strings.Announce_ActionRestart,
        AnnouncedAction.Stop => Strings.Announce_ActionStop,
        _ => Strings.Announce_ActionUpdate,
    }, pending.DueUtc.ToLocalTime().ToString("HH:mm", System.Globalization.CultureInfo.CurrentCulture));

    async Task RunAsync(Func<string, string, CancellationToken, Task<bool>> action)
    {
        Error = null;
        var actor = (await Authentication).User.Identity?.Name ?? "";
        try
        {
            await action(ServerId, actor, CancellationToken.None);
        }
        catch (PortInUseException)
        {
            Error = Strings.Servers_PortInUseOnHost;
        }
        catch (ServerRequestException exception)
        {
            Error = ServerTexts.Problem(exception.Problem);
        }
        catch (DockerCallException)
        {
            Error = Strings.Docker_CallFailed;
        }
    }
}
