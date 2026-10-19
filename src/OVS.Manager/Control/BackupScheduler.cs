using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OVS.Manager.Data;

namespace OVS.Manager.Control;

/// <summary>Package M15 (AC2): once a minute, makes the due scheduled backups of the connected servers.</summary>
internal sealed partial class BackupScheduler(IServiceScopeFactory scopes, TimeProvider time, ILogger<BackupScheduler> logger) : BackgroundService
{
    /// <summary>Makes every due backup now; returns how many were made.</summary>
    public async Task<int> RunDueAsync(CancellationToken cancellation = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ManagerDbContext>();
        var commands = scope.ServiceProvider.GetRequiredService<BackupCommands>();
        var now = time.GetUtcNow().UtcDateTime;
        int made = 0;
        var schedules = await database.BackupSchedules.Where(schedule => schedule.Interval != BackupInterval.Off).ToListAsync(cancellation);
        foreach (var schedule in schedules)
        {
            var server = await database.Servers.AsNoTracking().SingleOrDefaultAsync(row => row.Id == schedule.ServerId && row.DeletedAtUtc == null, cancellation);
            if (server is null || !TimeZoneInfo.TryFindSystemTimeZoneById(server.TimeZone, out var zone) || !schedule.IsDue(now, zone, server.CreatedAtUtc)) continue;
            var result = await commands.CreateScheduledAsync(schedule.ServerId, schedule.Keep, cancellation);
            if (result.Error is not null)
            {
                LogFailed(logger, schedule.ServerId, result.Error); // tried again at the next check
                continue;
            }
            schedule.LastRunUtc = now;
            await database.SaveChangesAsync(cancellation);
            made++;
        }
        return made;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield(); // the first check must not hold up the host's start
        using var minutely = new PeriodicTimer(TimeSpan.FromMinutes(1), time);
        while (await minutely.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RunDueAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is InvalidOperationException or TimeoutException)
            {
                LogFailed(logger, "?", exception.Message); // a link that dropped meanwhile; the next check tries again
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The scheduled backup of server {ServerId} failed: {Reason}")]
    static partial void LogFailed(ILogger logger, string serverId, string reason);
}
