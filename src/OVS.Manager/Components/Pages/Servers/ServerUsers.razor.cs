using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using OVS.Manager.Control;
using OVS.Manager.Localization;
using OVS.Shared.Protocol.Messages.Groups;
using OVS.Shared.Protocol.Messages.Users;

namespace OVS.Manager.Components.Pages.Servers;

/// <summary>
/// Package M13 (AC1, AC2): the users online, live from the mirror, with kick, ban, server mute and move; the known users
/// with their logins, times and groups, which are given and taken here.
/// </summary>
sealed partial class ServerUsers
{
    [Inject]
    ModerationCommands Moderation { get; set; } = null!;

    [Inject]
    GroupCommands GroupCommands { get; set; } = null!;

    [Inject]
    ServerConnections Connections { get; set; } = null!;

    [Inject]
    TimeProvider Time { get; set; } = null!;

    [CascadingParameter]
    Task<AuthenticationState> Authentication { get; set; } = null!;

    /// <summary>The server's id from the address.</summary>
    [Parameter]
    public string Id { get; set; } = "";

    IReadOnlyList<UserInfo> Online => Moderation.Online(Id);

    IReadOnlyDictionary<Guid, string> Channels => Moderation.Channels(Id);

    IReadOnlyList<GroupInfo> Groups => GroupCommands.Groups(Id);

    /// <summary>The known users of the last load.</summary>
    IReadOnlyList<KnownUserInfo> Known { get; set; } = [];

    /// <summary>The user online the action panel is for.</summary>
    UserInfo? Selected { get; set; }

    string Reason { get; set; } = "";

    int? Minutes { get; set; }

    bool IncludeIp { get; set; }

    Guid MoveTo { get; set; }

    Guid AssignChoice { get; set; }

    /// <summary>The known user whose deletion waits for the second click.</summary>
    string? DeleteConfirm { get; set; }

    /// <summary>Why the last action failed, null when it did not.</summary>
    string? Error { get; set; }

    /// <summary>Loads the known users and follows the server.</summary>
    protected override async Task OnInitializedAsync()
    {
        Connections.Changed += Redraw;
        await LoadAsync();
    }

    /// <summary>Stops following the server.</summary>
    public void Dispose() => Connections.Changed -= Redraw;

    void Redraw() => _ = InvokeAsync(StateHasChanged);

    async Task LoadAsync()
    {
        Show(await Moderation.KnownUsersAsync(Id));
        AssignChoice = Groups.Count > 0 ? Groups[0].Id : Guid.Empty;
    }

    void Show(KnownUserPage page)
    {
        Error = page.Error;
        if (page.Error is null) Known = [.. page.Users.OrderBy(user => user.LastNickname, StringComparer.OrdinalIgnoreCase)];
    }

    Action Select(UserInfo user) => () =>
    {
        Selected = user;
        MoveTo = user.ChannelId;
    };

    Task KickAsync() => RunAsync(actor => Moderation.KickAsync(Id, Selected!, Reason, actor));

    Task BanAsync() => RunAsync(actor => Moderation.BanAsync(Id, Selected!, Reason, Minutes, IncludeIp, actor));

    Task ToggleMuteAsync() => RunAsync(actor => Moderation.MuteAsync(Id, Selected!, !Selected!.ServerMuted, Minutes, actor));

    Task MoveAsync() => RunAsync(actor => Moderation.MoveAsync(Id, Selected!, MoveTo, actor));

    async Task RunAsync(Func<string, Task<string?>> action)
    {
        Error = await action(await ActorAsync());
        if (Error is not null) return;
        Selected = null;
        Reason = "";
        Minutes = null;
        await LoadAsync();
    }

    Func<Task> Assign(KnownUserInfo user) => async () =>
    {
        if (Groups.FirstOrDefault(group => group.Id == AssignChoice) is { } group) await AfterAsync(await GroupCommands.AssignAsync(Id, user, group, await ActorAsync()));
    };

    Func<Task> Unassign(KnownUserInfo user, GroupInfo group) => async () => await AfterAsync(await GroupCommands.UnassignAsync(Id, user, group, await ActorAsync()));

    Action AskDelete(KnownUserInfo user) => () => DeleteConfirm = user.Fingerprint;

    Func<Task> Delete(KnownUserInfo user) => async () =>
    {
        DeleteConfirm = null;
        await AfterAsync(await Moderation.DeleteKnownUserAsync(Id, user, await ActorAsync()));
    };

    /// <summary>The refusal of a change, or the known users again after it.</summary>
    async Task AfterAsync(string? refusal)
    {
        Error = refusal;
        if (refusal is null) await LoadAsync();
    }

    IEnumerable<GroupInfo> GroupsOf(KnownUserInfo user) => Groups.Where(group => user.GroupIds.Contains(group.Id));

    string GroupNames(IReadOnlyList<Guid> groupIds) => string.Join(", ", Groups.Where(group => groupIds.Contains(group.Id)).Select(group => group.Name));

    static string MuteText(UserInfo user) => user.ServerMuted ? Strings.Users_Unmute : Strings.Users_Mute;

    string MutedText(UserInfo user) => !user.ServerMuted ? "" : user.ServerMutedUntil is { } until
        ? TextFormat.Of(Strings.Users_MutedUntil, Local(until))
        : Strings.Users_MutedYes;

    string Since(UserInfo user) => Local(Known.FirstOrDefault(known => known.Fingerprint == user.Fingerprint)?.LastLogin);

    string Local(DateTimeOffset? at) => at is { } value && value != default ? TimeZoneInfo.ConvertTime(value, Time.LocalTimeZone).ToString("g", CultureInfo.CurrentCulture) : "";

    static string Hours(TimeSpan span) => TextFormat.Of(Strings.Users_Hours, (int)span.TotalHours, span.Minutes);

    async Task<string> ActorAsync() => (await Authentication).User.Identity?.Name ?? "";
}
