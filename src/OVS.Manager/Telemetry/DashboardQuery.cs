using Microsoft.EntityFrameworkCore;

namespace OVS.Manager.Telemetry;

/// <summary>Package M23 (AC2, AC3): users online and bandwidth now over exactly the servers given (a manager's are the assigned ones).</summary>
internal sealed class DashboardQuery(TelemetryStore store, TimeProvider time)
{
    const long MinuteMs = 60_000;
    const string Sessions = "ovs.sessions";
    static readonly string[] ByteMetrics = ["ovs.voice.bytes", "ovs.control.bytes"];

    /// <summary>The figures of <paramref name="serverIds"/> now; a server without data in the last 2 minutes counts 0.</summary>
    public async Task<DashboardFigures> ReadAsync(IReadOnlyCollection<string> serverIds, CancellationToken cancellation = default)
    {
        var now = time.GetUtcNow().ToUnixTimeMilliseconds();
        var since = now - (long)LiveFeed.Silence.TotalMilliseconds;
        var raw = await store.MetricPoints.AsNoTracking().Where(point => serverIds.Contains(point.ServerId) && point.Name == Sessions && point.AtUnixMs >= since)
            .Select(point => new { point.ServerId, At = point.AtUnixMs, point.Value }).ToListAsync(cancellation);
        var lastMinute = now - now % MinuteMs - MinuteMs;
        var minutes = await store.MetricRollups.AsNoTracking().Where(row => serverIds.Contains(row.ServerId) && row.Level == RollupLevel.Minute && row.PeriodUnixMs >= since - MinuteMs
            && (row.Name == Sessions || ByteMetrics.Contains(row.Name))).ToListAsync(cancellation);
        var newestUsers = raw.Concat(minutes.Where(row => row.Name == Sessions).Select(row => new { row.ServerId, At = row.PeriodUnixMs, row.Value }))
            .GroupBy(point => point.ServerId).Sum(server => server.MaxBy(point => point.At)!.Value);
        var bytes = minutes.Where(row => row.PeriodUnixMs == lastMinute && ByteMetrics.Contains(row.Name)).ToList();
        return new DashboardFigures(newestUsers, Kbit(bytes, "in"), Kbit(bytes, "out"));
    }

    static double Kbit(List<MetricRollup> rows, string direction) =>
        rows.Where(row => row.Attributes.Contains($"\"ovs.direction\":\"{direction}\"", StringComparison.Ordinal)).Sum(row => row.Value) * 8 / 60 / 1000;
}
