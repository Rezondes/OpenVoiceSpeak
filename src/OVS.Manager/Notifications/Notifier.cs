using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OVS.Manager.Accounts;
using OVS.Manager.Control;
using OVS.Manager.Data;
using OVS.Manager.Exceptions;
using OVS.Manager.Localization;
using OVS.Manager.Mail;
using OVS.Manager.Servers;
using OVS.Manager.Telemetry;

namespace OVS.Manager.Notifications;

/// <summary>
/// Package M27: once a minute, looks at every server and the manager, and tells the users who chose an event through their
/// channels. An event goes out once and again only after it was over, then with "wieder ok" (AC2); a failed send is tried
/// 3 more times and stays shown at the rule (AC4).
/// </summary>
internal sealed partial class Notifier(IServiceScopeFactory scopes, StatusWatcher watcher, ServerConnections connections, ReleaseCatalog catalog, IHostDisk disk,
    IEnumerable<INotificationChannel> channels, TimeProvider time, ILogger<Notifier> logger) : BackgroundService
{
    /// <summary>The key of the manager's own events.</summary>
    public const string ManagerKey = "manager";

    /// <summary>The name of the webhooks' HTTP client (public addresses only, no redirects).</summary>
    public const string HttpClientName = "webhooks";

    /// <summary>How long a server must not answer before it counts.</summary>
    public static readonly TimeSpan NotReachableFor = TimeSpan.FromMinutes(5);

    /// <summary>AC4: the waits before the 3 retries. ponytail: short and fixed, add a backoff queue if webhooks need minutes.</summary>
    static readonly TimeSpan[] Retries = [TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1)];

    /// <summary>The manager's events, which only the admin can choose.</summary>
    public static readonly IReadOnlySet<NotificationEvent> ManagerEvents = new HashSet<NotificationEvent> { NotificationEvent.DiskLow, NotificationEvent.ManagerUpdate };

    readonly Dictionary<string, DateTimeOffset> notReachableSince = [];
    readonly SemaphoreSlim gate = new(1, 1);

    /// <summary>One look at every server of <see cref="StatusWatcher"/> and at the manager.</summary>
    public Task EvaluateAsync(CancellationToken cancellation = default) => EvaluateAsync(watcher.Servers, cancellation);

    /// <summary>One look at <paramref name="servers"/> and at the manager.</summary>
    public async Task EvaluateAsync(IReadOnlyList<ServerSnapshot> servers, CancellationToken cancellation = default)
    {
        await gate.WaitAsync(cancellation);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<ManagerDbContext>();
            var store = scope.ServiceProvider.GetRequiredService<TelemetryStore>();
            var rows = await database.Servers.AsNoTracking().Where(server => server.DeletedAtUtc == null).ToDictionaryAsync(server => server.Id, cancellation);
            var thresholds = await database.ServerAlertSettings.AsNoTracking().ToDictionaryAsync(settings => settings.ServerId, cancellation);
            var now = time.GetUtcNow();
            foreach (var server in servers.Where(server => rows.ContainsKey(server.Id)))
            {
                bool silent = server.Status == ServerStatus.NotReachable;
                if (!silent) notReachableSince.Remove(server.Id);
                else if (!notReachableSince.ContainsKey(server.Id)) notReachableSince[server.Id] = now;
                await ApplyAsync(scope, server.Id, server.Name, NotificationEvent.Crashed, server.Status == ServerStatus.Crashed, cancellation);
                await ApplyAsync(scope, server.Id, server.Name, NotificationEvent.RestartLoop, server.Status == ServerStatus.RestartLoop, cancellation);
                await ApplyAsync(scope, server.Id, server.Name, NotificationEvent.NotReachable, silent && now - notReachableSince[server.Id] >= NotReachableFor, cancellation);
                await ApplyAsync(scope, server.Id, server.Name, NotificationEvent.Full, IsFull(server.Id, rows[server.Id].MaxUsers), cancellation);
                await ApplyAsync(scope, server.Id, server.Name, NotificationEvent.Drops,
                    await DropsAsync(store, server.Id, thresholds.GetValueOrDefault(server.Id) ?? new ServerAlertSettings { ServerId = server.Id }, now, cancellation), cancellation);
                await ApplyAsync(scope, server.Id, server.Name, NotificationEvent.UpdateAvailable, catalog.UpdateFor(server.Version) is not null, cancellation);
            }
            await ApplyAsync(scope, ManagerKey, Strings.Notify_Manager, NotificationEvent.DiskLow, disk.Read() is { IsLow: true }, cancellation);
            await ApplyAsync(scope, ManagerKey, Strings.Notify_Manager, NotificationEvent.ManagerUpdate, catalog.ManagerUpdate is not null, cancellation);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>M18: an update of <paramref name="serverId"/> failed (true) or went well (false).</summary>
    public async Task ReportUpdateAsync(string serverId, string serverName, bool failed, CancellationToken cancellation = default)
    {
        await gate.WaitAsync(cancellation);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await ApplyAsync(scope, serverId, serverName, NotificationEvent.UpdateFailed, failed, cancellation);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        gate.Dispose();
        base.Dispose();
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield(); // the first look must not hold up the host's start
        using var minutely = new PeriodicTimer(TimeSpan.FromMinutes(1), time);
        while (await minutely.WaitForNextTickAsync(stoppingToken)) await EvaluateAsync(stoppingToken);
    }

    /// <summary>AC2: sends <paramref name="event"/> when it starts and was not sent, and "wieder ok" when it ends and was sent.</summary>
    async Task ApplyAsync(AsyncServiceScope scope, string key, string subject, NotificationEvent @event, bool active, CancellationToken cancellation)
    {
        var database = scope.ServiceProvider.GetRequiredService<ManagerDbContext>();
        var sent = await database.AlertStates.SingleOrDefaultAsync(state => state.Key == key && state.Event == @event, cancellation);
        if (active == (sent is not null)) return;
        if (active) database.AlertStates.Add(new AlertState { Key = key, Event = @event, SentAtUtc = time.GetUtcNow().UtcDateTime });
        else database.AlertStates.Remove(sent!);
        await database.SaveChangesAsync(cancellation);
        await DeliverAsync(scope, key, subject, @event, resolved: !active, cancellation);
    }

    /// <summary>AC1: to every rule of <paramref name="event"/> whose user sees <paramref name="key"/> (the manager's events: admins only).</summary>
    async Task DeliverAsync(AsyncServiceScope scope, string key, string subject, NotificationEvent @event, bool resolved, CancellationToken cancellation)
    {
        var database = scope.ServiceProvider.GetRequiredService<ManagerDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ManagerUser>>();
        foreach (var byUser in (await database.NotificationRules.Where(rule => rule.Event == @event).ToListAsync(cancellation)).GroupBy(rule => rule.UserId))
        {
            if (await users.FindByIdAsync(byUser.Key) is not { } user) continue;
            bool admin = await users.IsInRoleAsync(user, ManagerRoles.Admin);
            if (key == ManagerKey ? !admin : !admin && !await database.ServerAssignments.AnyAsync(row => row.UserId == user.Id && row.ServerId == key, cancellation)) continue;
            var text = MailTemplates.Localized(resolved ? nameof(Strings.Notify_Resolved) : $"Notify_{@event}", user.Language);
            var notification = new Notification(@event, subject, resolved, TextFormat.Of(resolved ? TextFormat.Of(text, Describe(@event, subject, user.Language)) : text, subject), time.GetUtcNow());
            foreach (var rule in byUser)
            {
                var target = rule.Channel == NotificationChannelKind.Email ? user.Email ?? "" : rule.Target;
                if (target.Length == 0 || channels.FirstOrDefault(channel => channel.Kind == rule.Channel) is not { } channel) continue;
                var failure = await SendWithRetriesAsync(channel, target, notification, cancellation);
                rule.LastError = failure;
                rule.LastErrorAtUtc = failure is null ? null : time.GetUtcNow().UtcDateTime;
            }
        }
        await database.SaveChangesAsync(cancellation);
    }

    static string Describe(NotificationEvent @event, string subject, string? language) => TextFormat.Of(MailTemplates.Localized($"Notify_{@event}", language), subject);

    /// <summary>AC4: the send and 3 retries; null when one went through, else the last error.</summary>
    async Task<string?> SendWithRetriesAsync(INotificationChannel channel, string target, Notification notification, CancellationToken cancellation)
    {
        string? failure = null;
        for (int attempt = 0; attempt <= Retries.Length; attempt++)
        {
            if (attempt > 0) await Task.Delay(Retries[attempt - 1], time, cancellation);
            try
            {
                await channel.SendAsync(target, notification, cancellation);
                return null;
            }
            catch (Exception exception) when (exception is HttpRequestException or MailSendException or TaskCanceledException or UriFormatException)
            {
                failure = exception.Message;
            }
        }
        LogFailed(logger, channel.Kind, notification.Event, failure ?? "");
        return failure;
    }

    /// <summary>The server has as many users (without the service session) as its limit.</summary>
    bool IsFull(string serverId, int maxUsers) =>
        connections.LinkOf(serverId)?.Read(mirror => mirror.Users.Keys.Count(session => session != mirror.SelfId) >= (mirror.Settings.Limits?.MaxUsers ?? maxUsers), false) ?? false;

    /// <summary>AC3: each of the last finished minutes of the server's threshold dropped more than its percent of the voice packets received.</summary>
    static async Task<bool> DropsAsync(TelemetryStore store, string serverId, ServerAlertSettings settings, DateTimeOffset now, CancellationToken cancellation)
    {
        const long MinuteMs = 60_000;
        var end = now.ToUnixTimeMilliseconds() / MinuteMs * MinuteMs;
        var start = end - settings.DropsMinutes * MinuteMs;
        var rows = await store.MetricRollups.AsNoTracking().Where(row => row.ServerId == serverId && row.Level == RollupLevel.Minute && row.PeriodUnixMs >= start && row.PeriodUnixMs < end
            && (row.Name == "ovs.voice.drops" || row.Name == "ovs.voice.packets")).ToListAsync(cancellation);
        return Enumerable.Range(0, settings.DropsMinutes).Select(index => start + index * MinuteMs).All(minute =>
        {
            var received = rows.Where(row => row.PeriodUnixMs == minute && row.Name == "ovs.voice.packets" && row.Attributes.Contains("\"ovs.direction\":\"in\"", StringComparison.Ordinal)).Sum(row => row.Value);
            var dropped = rows.Where(row => row.PeriodUnixMs == minute && row.Name == "ovs.voice.drops").Sum(row => row.Value);
            return received > 0 && dropped / received * 100 > settings.DropsPercent;
        });
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A notification ({Event}) through {Channel} did not go out after its retries: {Reason}")]
    static partial void LogFailed(ILogger logger, NotificationChannelKind channel, NotificationEvent @event, string reason);
}
