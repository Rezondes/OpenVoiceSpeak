using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using OVS.Manager.Accounts;
using OVS.Manager.Data;
using OVS.Manager.Localization;
using OVS.Manager.Notifications;
using OVS.Manager.Servers;

namespace OVS.Manager.Components.Pages.Settings;

/// <summary>
/// Package M27 (AC1, AC3, AC4): each user picks per event and channel; the admin also the manager's events. A failed send
/// shows at its event. The thresholds of the visible servers are set below.
/// </summary>
sealed partial class NotificationSettings
{
    [Inject]
    ManagerDbContext Database { get; set; } = null!;

    [Inject]
    ServerAccess Access { get; set; } = null!;

    [CascadingParameter]
    Task<AuthenticationState> Authentication { get; set; } = null!;

    string UserId { get; set; } = "";

    string WebhookUrl { get; set; } = "";

    string DiscordUrl { get; set; } = "";

    List<EventRow> Rows { get; set; } = [];

    List<ThresholdRow> Thresholds { get; set; } = [];

    string? Result { get; set; }

    bool Failed { get; set; }

    string ResultClass => Failed ? "form-error" : "form-ok";

    /// <summary>Loads the user's rules and the visible servers' thresholds.</summary>
    protected override async Task OnInitializedAsync()
    {
        var user = (await Authentication).User;
        UserId = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "";
        bool admin = user.IsInRole(ManagerRoles.Admin);
        var rules = await Database.NotificationRules.AsNoTracking().Where(rule => rule.UserId == UserId).ToListAsync();
        WebhookUrl = rules.FirstOrDefault(rule => rule.Channel == NotificationChannelKind.Webhook)?.Target ?? "";
        DiscordUrl = rules.FirstOrDefault(rule => rule.Channel == NotificationChannelKind.Discord)?.Target ?? "";
        Rows = [.. Enum.GetValues<NotificationEvent>().Where(@event => admin || !Notifier.ManagerEvents.Contains(@event)).Select(@event => new EventRow
        {
            Event = @event,
            Email = rules.Any(rule => rule.Event == @event && rule.Channel == NotificationChannelKind.Email),
            Webhook = rules.Any(rule => rule.Event == @event && rule.Channel == NotificationChannelKind.Webhook),
            Discord = rules.Any(rule => rule.Event == @event && rule.Channel == NotificationChannelKind.Discord),
            LastError = string.Join(" ", rules.Where(rule => rule.Event == @event && rule.LastError is not null).Select(rule => $"{rule.Channel}: {rule.LastError}")),
        })];
        var visible = await Access.VisibleAsync(user);
        var settings = await Database.ServerAlertSettings.AsNoTracking().ToDictionaryAsync(row => row.ServerId);
        Thresholds = [.. (await Database.Servers.AsNoTracking().Where(server => server.DeletedAtUtc == null).OrderBy(server => server.Name).ToListAsync())
            .Where(server => visible is null || visible.Contains(server.Id)).Select(server => new ThresholdRow
            {
                ServerId = server.Id, Name = server.Name,
                DropsPercent = settings.GetValueOrDefault(server.Id)?.DropsPercent ?? ServerAlertSettings.DefaultDropsPercent,
                DropsMinutes = settings.GetValueOrDefault(server.Id)?.DropsMinutes ?? ServerAlertSettings.DefaultDropsMinutes,
            })];
    }

    async Task SaveAsync()
    {
        bool wantsWebhook = Rows.Any(row => row.Webhook), wantsDiscord = Rows.Any(row => row.Discord);
        if ((wantsWebhook && !PublicOnly.IsWebhookUrl(WebhookUrl)) || (wantsDiscord && !PublicOnly.IsWebhookUrl(DiscordUrl)))
        {
            (Failed, Result) = (true, Strings.NotifySettings_InvalidUrl);
            return;
        }
        var old = await Database.NotificationRules.Where(rule => rule.UserId == UserId).ToListAsync();
        Database.NotificationRules.RemoveRange(old);
        foreach (var row in Rows)
        {
            if (row.Email) Database.NotificationRules.Add(new NotificationRule { UserId = UserId, Event = row.Event, Channel = NotificationChannelKind.Email });
            if (row.Webhook) Database.NotificationRules.Add(new NotificationRule { UserId = UserId, Event = row.Event, Channel = NotificationChannelKind.Webhook, Target = WebhookUrl.Trim() });
            if (row.Discord) Database.NotificationRules.Add(new NotificationRule { UserId = UserId, Event = row.Event, Channel = NotificationChannelKind.Discord, Target = DiscordUrl.Trim() });
        }
        await Database.SaveChangesAsync();
        Database.ChangeTracker.Clear();
        (Failed, Result) = (false, Strings.Settings_Saved);
    }

    async Task SaveThresholdsAsync()
    {
        if (Thresholds.Any(row => row.DropsPercent is <= 0 or > 100 || row.DropsMinutes is < 1 or > 60))
        {
            (Failed, Result) = (true, Strings.NotifySettings_InvalidThreshold);
            return;
        }
        foreach (var row in Thresholds)
        {
            var existing = await Database.ServerAlertSettings.SingleOrDefaultAsync(settings => settings.ServerId == row.ServerId);
            if (existing is null) Database.ServerAlertSettings.Add(new ServerAlertSettings { ServerId = row.ServerId, DropsPercent = row.DropsPercent, DropsMinutes = row.DropsMinutes });
            else (existing.DropsPercent, existing.DropsMinutes) = (row.DropsPercent, row.DropsMinutes);
        }
        await Database.SaveChangesAsync();
        Database.ChangeTracker.Clear();
        (Failed, Result) = (false, Strings.Settings_Saved);
    }

    static string EventName(NotificationEvent @event) => Strings.ResourceManager.GetString($"NotifyEvent_{@event}", Strings.Culture) ?? @event.ToString();

    /// <summary>One event's channels in the form.</summary>
    sealed class EventRow
    {
        public NotificationEvent Event { get; set; }
        public bool Email { get; set; }
        public bool Webhook { get; set; }
        public bool Discord { get; set; }
        public string LastError { get; set; } = "";
    }

    /// <summary>One server's thresholds in the form.</summary>
    sealed class ThresholdRow
    {
        public string ServerId { get; set; } = "";
        public string Name { get; set; } = "";
        public double DropsPercent { get; set; }
        public int DropsMinutes { get; set; }
    }
}
