using Microsoft.EntityFrameworkCore;

namespace OVS.Manager.Telemetry;

/// <summary>
/// Package M21 (AC2, AC4, MA7): once an hour, deletes telemetry older than its retention, and while the database holds more
/// than <see cref="ManagerOptions.TelemetryMaxBytes"/> the oldest day of raw data (spans, log lines, raw points, minute
/// values); hourly values and daily statistics stay.
/// </summary>
internal sealed class Retention(IServiceScopeFactory scopes, ManagerOptions options, TelemetryLimit limit, TimeProvider time) : BackgroundService
{
    const long DayMs = 86_400_000;

    /// <summary>One pass; true when raw data had to go for the size (the overview's warning).</summary>
    public async Task<bool> TrimAsync(CancellationToken cancellation = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<TelemetryStore>();
        var now = time.GetUtcNow().ToUnixTimeMilliseconds();
        long minutes = now - options.RetentionMinuteDays * DayMs, hours = now - options.RetentionHourDays * DayMs;
        long logs = now - options.RetentionLogDays * DayMs, spans = now - options.RetentionTraceDays * DayMs, raw = now - DayMs;
        await store.MetricRollups.Where(row => row.Level == RollupLevel.Minute && row.PeriodUnixMs < minutes).ExecuteDeleteAsync(cancellation);
        await store.MetricRollups.Where(row => row.Level == RollupLevel.Hour && row.PeriodUnixMs < hours).ExecuteDeleteAsync(cancellation);
        await store.LogRecords.Where(line => line.AtUnixMs < logs).ExecuteDeleteAsync(cancellation);
        await store.Spans.Where(span => span.StartUnixMs < spans).ExecuteDeleteAsync(cancellation);
        await store.MetricPoints.Where(point => point.AtUnixMs < raw).ExecuteDeleteAsync(cancellation); // never rolled up (a stopped rollup)

        bool trimmed = false;
        while (await UsedBytesAsync(store, cancellation) > options.TelemetryMaxBytes && await OldestRawAsync(store, cancellation) is { } oldest)
        {
            var cut = oldest + DayMs;
            await store.Spans.Where(span => span.StartUnixMs < cut).ExecuteDeleteAsync(cancellation);
            await store.LogRecords.Where(line => line.AtUnixMs < cut).ExecuteDeleteAsync(cancellation);
            await store.MetricPoints.Where(point => point.AtUnixMs < cut).ExecuteDeleteAsync(cancellation);
            await store.MetricRollups.Where(row => row.Level == RollupLevel.Minute && row.PeriodUnixMs < cut).ExecuteDeleteAsync(cancellation);
            trimmed = true;
        }
        limit.Reached = trimmed;
        return trimmed;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield(); // the first pass must not hold up the host's start
        using var hourly = new PeriodicTimer(TimeSpan.FromHours(1), time);
        while (await hourly.WaitForNextTickAsync(stoppingToken)) await TrimAsync(stoppingToken);
    }

    /// <summary>The bytes the database's pages hold; deleted rows free pages for reuse (no VACUUM, the file keeps its size).</summary>
    static Task<long> UsedBytesAsync(TelemetryStore store, CancellationToken cancellation) =>
        store.Database.SqlQueryRaw<long>("SELECT (page_count - freelist_count) * page_size AS Value FROM pragma_page_count(), pragma_freelist_count(), pragma_page_size()")
            .SingleAsync(cancellation);

    /// <summary>The oldest time of any raw data in Unix milliseconds, null when there is none.</summary>
    static async Task<long?> OldestRawAsync(TelemetryStore store, CancellationToken cancellation)
    {
        long?[] oldest =
        [
            await store.Spans.MinAsync(span => (long?)span.StartUnixMs, cancellation),
            await store.LogRecords.MinAsync(line => (long?)line.AtUnixMs, cancellation),
            await store.MetricPoints.MinAsync(point => (long?)point.AtUnixMs, cancellation),
            await store.MetricRollups.Where(row => row.Level == RollupLevel.Minute).MinAsync(row => (long?)row.PeriodUnixMs, cancellation),
        ];
        return oldest.Min();
    }
}
