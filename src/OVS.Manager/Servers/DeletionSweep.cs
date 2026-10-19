using Microsoft.Extensions.Logging;
using OVS.Manager.Exceptions;

namespace OVS.Manager.Servers;

/// <summary>Package M9 (AC3): once a day, and at the start, removes the servers that are longer than 7 days in the trash.</summary>
internal sealed partial class DeletionSweep(IServiceScopeFactory scopes, TimeProvider time, ILogger<DeletionSweep> logger) : BackgroundService
{
    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield(); // the first sweep must not hold up the host's start
        using var daily = new PeriodicTimer(TimeSpan.FromDays(1), time);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                int purged = await scope.ServiceProvider.GetRequiredService<ServerDeletion>().SweepAsync(stoppingToken);
                if (purged > 0) LogPurged(logger, purged);
            }
            catch (DockerCallException exception)
            {
                LogFailed(logger, exception); // the next day tries again
            }
        }
        while (await daily.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} deleted servers removed for good")]
    static partial void LogPurged(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The sweep of deleted servers failed")]
    static partial void LogFailed(ILogger logger, Exception exception);
}
