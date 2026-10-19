namespace OVS.Manager.Data;

/// <summary>Package M33 (AC4): checks the manager's backup schedule every ten minutes.</summary>
internal sealed partial class ManagerBackupRunner(IServiceScopeFactory scopes, TimeProvider time, ILogger<ManagerBackupRunner> logger) : BackgroundService
{
    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield(); // the first check must not hold up the host's start
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(10), time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ManagerBackupSchedule>().RunDueAsync(stoppingToken);
            }
            catch (IOException exception)
            {
                LogFailed(logger, exception.Message); // a full disk or a locked file; the next check tries again
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The scheduled backup of the manager failed: {Reason}")]
    static partial void LogFailed(ILogger logger, string reason);
}
