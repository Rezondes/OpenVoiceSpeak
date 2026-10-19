using Microsoft.Extensions.Logging;
using OVS.Manager.Docker;
using OVS.Manager.Exceptions;

namespace OVS.Manager.Telemetry;

/// <summary>
/// Package M25 (AC1): once a minute, Docker's CPU and memory of every running managed container go into the telemetry store
/// as the gauges <see cref="CpuMetric"/> and <see cref="MemoryMetric"/>, so the rollup of M21 keeps them for days.
/// </summary>
internal sealed partial class UsageSampler(IServiceScopeFactory scopes, IDockerEngine docker, TimeProvider time, ILogger<UsageSampler> logger) : BackgroundService
{
    /// <summary>The container's CPU in percent of a core.</summary>
    public const string CpuMetric = "container.cpu.percent";

    /// <summary>The container's memory in MB.</summary>
    public const string MemoryMetric = "container.memory.mb";

    /// <summary>One sample of every running managed container.</summary>
    public async Task SampleAsync(CancellationToken cancellation = default)
    {
        var at = time.GetUtcNow().ToUnixTimeMilliseconds();
        var points = new List<MetricPoint>();
        try
        {
            foreach (var container in (await docker.ListAsync(cancellation)).Where(container => container.State == "running"))
            {
                var usage = await docker.StatsAsync(container.Id, cancellation);
                points.Add(new MetricPoint { ServerId = container.ServerId, Name = CpuMetric, Kind = MetricKind.Gauge, AtUnixMs = at, Value = usage.CpuPercent });
                points.Add(new MetricPoint { ServerId = container.ServerId, Name = MemoryMetric, Kind = MetricKind.Gauge, AtUnixMs = at, Value = usage.MemoryBytes / 1048576.0 });
            }
        }
        catch (Exception exception) when (exception is DockerCallException or ForeignContainerException or HttpRequestException)
        {
            LogFailed(logger, exception); // the next minute tries again; the samples so far are kept
        }
        if (points.Count == 0) return;
        await using var scope = scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<TelemetryStore>();
        store.MetricPoints.AddRange(points);
        await store.SaveChangesAsync(cancellation);
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield(); // the first sample must not hold up the host's start
        using var minutely = new PeriodicTimer(TimeSpan.FromMinutes(1), time);
        while (await minutely.WaitForNextTickAsync(stoppingToken)) await SampleAsync(stoppingToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sampling the containers' CPU and memory failed")]
    static partial void LogFailed(ILogger logger, Exception exception);
}
