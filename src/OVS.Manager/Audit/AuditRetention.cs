using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace OVS.Manager.Audit;

/// <summary>Package M5 (AC4): with <c>OVS_MANAGER_AUDIT_DAYS</c> the lines older than that are deleted once a day.</summary>
internal sealed partial class AuditRetention(IServiceScopeFactory scopes, ManagerOptions options, TimeProvider time, ILogger<AuditRetention> logger)
    : BackgroundService
{
    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.AuditDays is not { } days) return;
        using var daily = new PeriodicTimer(TimeSpan.FromDays(1), time);
        do
        {
            await using var scope = scopes.CreateAsyncScope();
            int deleted = await scope.ServiceProvider.GetRequiredService<AuditLog>().DeleteOlderThanAsync(TimeSpan.FromDays(days), stoppingToken);
            if (deleted > 0) LogDeleted(logger, deleted, days);
        }
        while (await daily.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} audit lines older than {Days} days deleted")]
    static partial void LogDeleted(ILogger logger, int count, int days);
}
