using Microsoft.EntityFrameworkCore;
using OVS.Manager.Docker;
using OVS.Manager.Exceptions;

namespace OVS.Manager.Telemetry;

/// <summary>
/// Package M22 (AC1, AC3): a server's newest figures from the telemetry store (the minute values of M21 and the newest raw
/// session count) and its container's CPU and RAM from Docker; the page asks every 5 s.
/// </summary>
internal sealed class LiveFeed(TelemetryStore store, IDockerEngine docker, TimeProvider time)
{
    /// <summary>The time the lines cover.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    /// <summary>AC3: no data for this long is "keine Telemetrie".</summary>
    public static readonly TimeSpan Silence = TimeSpan.FromMinutes(2);

    const long MinuteMs = 60_000;
    const string Sessions = "ovs.sessions";
    const string Drops = "ovs.voice.drops";
    static readonly string[] ByteMetrics = ["ovs.voice.bytes", "ovs.control.bytes"];

    /// <summary>The figures of <paramref name="serverId"/> now.</summary>
    public async Task<LiveSnapshot> ReadAsync(string serverId, CancellationToken cancellation = default)
    {
        var now = time.GetUtcNow().ToUnixTimeMilliseconds();
        var from = now - (long)Window.TotalMilliseconds;
        var minutes = await store.MetricRollups.AsNoTracking().Where(row => row.ServerId == serverId && row.Level == RollupLevel.Minute && row.PeriodUnixMs >= from
            && (row.Name == Sessions || row.Name == Drops || ByteMetrics.Contains(row.Name))).OrderBy(row => row.PeriodUnixMs).ToListAsync(cancellation);
        var newestUsers = await store.MetricPoints.AsNoTracking().Where(point => point.ServerId == serverId && point.Name == Sessions)
            .OrderByDescending(point => point.AtUnixMs).FirstOrDefaultAsync(cancellation);
        var newestRaw = await store.MetricPoints.Where(point => point.ServerId == serverId).MaxAsync(point => (long?)point.AtUnixMs, cancellation);
        long newest = Math.Max(newestRaw ?? 0, minutes.Count > 0 ? minutes[^1].PeriodUnixMs + MinuteMs : 0);

        var users = minutes.Where(row => row.Name == Sessions).Select(row => new ChartPoint(row.PeriodUnixMs, row.Value)).ToList();
        var kbitIn = Kbit(minutes, "in");
        var kbitOut = Kbit(minutes, "out");
        var drops = minutes.Where(row => row.Name == Drops).GroupBy(row => row.PeriodUnixMs).Select(group => new ChartPoint(group.Key, group.Sum(row => row.Value))).ToList();
        return new LiveSnapshot(newest >= now - (long)Silence.TotalMilliseconds, newestUsers?.Value ?? users.LastOrDefault()?.Value,
            kbitIn.LastOrDefault()?.Value ?? 0, kbitOut.LastOrDefault()?.Value ?? 0, drops.LastOrDefault()?.Value ?? 0, await UsageAsync(serverId, cancellation),
            users, kbitIn, kbitOut, drops, from, now);
    }

    /// <summary>kbit/s per minute in <paramref name="direction"/> ("in" or "out").</summary>
    static List<ChartPoint> Kbit(List<MetricRollup> minutes, string direction) =>
        [.. minutes.Where(row => ByteMetrics.Contains(row.Name) && row.Attributes.Contains($"\"ovs.direction\":\"{direction}\"", StringComparison.Ordinal))
            .GroupBy(row => row.PeriodUnixMs).Select(group => new ChartPoint(group.Key, group.Sum(row => row.Value) * 8 / 60 / 1000))];

    async Task<ContainerUsage?> UsageAsync(string serverId, CancellationToken cancellation)
    {
        try
        {
            return (await docker.ListAsync(cancellation)).FirstOrDefault(container => container.ServerId == serverId) is { State: "running" } container
                ? await docker.StatsAsync(container.Id, cancellation) : null;
        }
        catch (Exception exception) when (exception is DockerCallException or ForeignContainerException or HttpRequestException)
        {
            return null; // the tiles show no CPU and RAM then
        }
    }
}
