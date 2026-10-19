using Microsoft.EntityFrameworkCore;
using OVS.Manager.Docker;
using OVS.Manager.Exceptions;

namespace OVS.Manager.Telemetry;

/// <summary>Package M25 (AC1): what a server's container uses now (Docker), the hourly peaks of the last 24 hours (the sampler's gauges) and its volume's size.</summary>
internal sealed class ResourceQuery(TelemetryStore store, IDockerEngine docker, TimeProvider time)
{
    /// <summary>The figures of <paramref name="serverId"/>.</summary>
    public async Task<ResourceSnapshot> ReadAsync(string serverId, CancellationToken cancellation = default)
    {
        var now = time.GetUtcNow().ToUnixTimeMilliseconds();
        var from = now - (long)TimeSpan.FromHours(24).TotalMilliseconds;
        var hours = await store.MetricRollups.AsNoTracking().Where(row => row.ServerId == serverId && row.Level == RollupLevel.Hour && row.PeriodUnixMs >= from - 3_600_000
            && (row.Name == UsageSampler.CpuMetric || row.Name == UsageSampler.MemoryMetric)).OrderBy(row => row.PeriodUnixMs).ToListAsync(cancellation);
        ContainerUsage? usage = null;
        long? volume = null;
        try
        {
            if ((await docker.ListAsync(cancellation)).FirstOrDefault(container => container.ServerId == serverId) is { State: "running" } container)
                usage = await docker.StatsAsync(container.Id, cancellation);
            volume = (await docker.VolumeSizesAsync(cancellation)).TryGetValue(ManagedLabels.VolumeName(serverId), out var bytes) ? bytes : null;
        }
        catch (Exception exception) when (exception is DockerCallException or ForeignContainerException or HttpRequestException)
        {
            // the page shows "-" for what Docker did not tell
        }
        return new ResourceSnapshot(usage, volume, Line(hours, UsageSampler.CpuMetric), Line(hours, UsageSampler.MemoryMetric), from, now);
    }

    static List<ChartPoint> Line(List<MetricRollup> hours, string name) =>
        [.. hours.Where(row => row.Name == name).Select(row => new ChartPoint(row.PeriodUnixMs, row.Max))];
}
