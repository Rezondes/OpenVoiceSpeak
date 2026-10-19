using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OVS.Manager.Data;

namespace OVS.Manager.Servers;

/// <summary>Package M18 (AC4): once a minute, updates the servers whose maintenance window is open and that have an update.</summary>
internal sealed partial class UpdateWindowRunner(IServiceScopeFactory scopes, ReleaseCatalog catalog, TimeProvider time, ILogger<UpdateWindowRunner> logger) : BackgroundService
{
    /// <summary>The servers whose window is open, with automatic updates on, not yet run in this window, and an offered release.</summary>
    public async Task<IReadOnlyList<UpdateDue>> DueAsync(CancellationToken cancellation = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ManagerDbContext>();
        var now = time.GetUtcNow().UtcDateTime;
        var windows = await database.UpdateWindows.AsNoTracking().Where(window => window.AutoUpdate).ToListAsync(cancellation);
        var due = new List<UpdateDue>();
        foreach (var window in windows)
        {
            var server = await database.Servers.AsNoTracking().SingleOrDefaultAsync(row => row.Id == window.ServerId && row.DeletedAtUtc == null, cancellation);
            if (server is null || !TimeZoneInfo.TryFindSystemTimeZoneById(server.TimeZone, out var zone) || !window.IsDue(now, zone)) continue;
            if (catalog.UpdateFor(server.Version) is { } release) due.Add(new UpdateDue(server.Id, release.Version));
        }
        return due;
    }

    /// <summary>Runs the due updates now, one after the other.</summary>
    public async Task RunDueAsync(CancellationToken cancellation = default)
    {
        foreach (var due in await DueAsync(cancellation))
        {
            await using var scope = scopes.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<ManagerDbContext>();
            var window = await database.UpdateWindows.SingleAsync(row => row.ServerId == due.ServerId, cancellation);
            window.LastRunUtc = time.GetUtcNow().UtcDateTime;
            await database.SaveChangesAsync(cancellation);
            if (await scope.ServiceProvider.GetRequiredService<ServerUpdate>().UpdateAsync(due.ServerId, due.Version, ServerUpdate.WindowActor, cancellation: cancellation) is { } failed)
                LogFailed(logger, due.ServerId, failed);
        }
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield(); // the first check must not hold up the host's start
        using var minutely = new PeriodicTimer(TimeSpan.FromMinutes(1), time);
        while (await minutely.WaitForNextTickAsync(stoppingToken)) await RunDueAsync(stoppingToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The update of server {ServerId} in its maintenance window failed: {Reason}")]
    static partial void LogFailed(ILogger logger, string serverId, string reason);
}
