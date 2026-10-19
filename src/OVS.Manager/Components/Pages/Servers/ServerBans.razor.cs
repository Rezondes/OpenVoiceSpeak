using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using OVS.Manager.Control;
using OVS.Manager.Localization;
using OVS.Shared.Protocol.Messages.Moderation;

namespace OVS.Manager.Components.Pages.Servers;

/// <summary>Package M13 (AC3): the bans with reason, end and who banned; an active one is lifted here.</summary>
sealed partial class ServerBans
{
    [Inject]
    ModerationCommands Moderation { get; set; } = null!;

    [Inject]
    TimeProvider Time { get; set; } = null!;

    [CascadingParameter]
    Task<AuthenticationState> Authentication { get; set; } = null!;

    /// <summary>The server's id from the address.</summary>
    [Parameter]
    public string Id { get; set; } = "";

    /// <summary>The bans of the last load, newest first.</summary>
    IReadOnlyList<BanInfo> Bans { get; set; } = [];

    /// <summary>Why the last load or action failed, null when it did not.</summary>
    string? Error { get; set; }

    /// <summary>Loads the bans.</summary>
    protected override async Task OnInitializedAsync() => Show(await Moderation.BansAsync(Id));

    void Show(BanPage page)
    {
        Error = page.Error;
        if (page.Error is null) Bans = [.. page.Bans.OrderByDescending(ban => ban.CreatedAt)];
    }

    Func<Task> Lift(BanInfo ban) => async () => Show(await Moderation.UnbanAsync(Id, ban, (await Authentication).User.Identity?.Name ?? ""));

    bool IsActive(BanInfo ban) => ban.LiftedAt is null && (ban.ExpiresAt is null || ban.ExpiresAt > Time.GetUtcNow());

    string StateText(BanInfo ban) => ban.LiftedAt is not null ? Strings.Bans_Lifted : IsActive(ban) ? Strings.Bans_Active : Strings.Bans_Expired;

    string Ends(BanInfo ban) => ban.ExpiresAt is { } end
        ? TimeZoneInfo.ConvertTime(end, Time.LocalTimeZone).ToString("g", CultureInfo.CurrentCulture)
        : Strings.Bans_Permanent;
}
