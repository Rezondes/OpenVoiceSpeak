using Microsoft.EntityFrameworkCore;
using OVS.Manager.Telemetry;
using OVS.Tests.TestSupport;
using static OVS.Tests.Manager.Telemetry.TelemetryTesting;

namespace OVS.Tests.Manager.Telemetry;

/// <summary>Package M21: raw points to minute and hour values with counter deltas, and the daily statistics.</summary>
public sealed class RollupTests
{
    static readonly DateTimeOffset Ten = new(2026, 10, 19, 10, 0, 0, TimeSpan.Zero);

    /// <summary>AC1: a cumulative counter becomes per-minute deltas, also across a restart that starts it again at 0.</summary>
    [Fact]
    public async Task Counter_Restart_DeltaCorrect()
    {
        var time = new ManualTimeProvider(Ten.AddMinutes(3));
        await using var manager = TelemetryManager(time);
        manager.CreateClient().Dispose();
        var server = await ServerLinks.AddAsync(manager);
        var first = Ten.AddHours(-1);
        var second = Ten.AddSeconds(85); // the server started again
        await AddAsync(manager,
            Sum(server.Id, "ovs.connections", Ten.AddSeconds(10), first, 5), Sum(server.Id, "ovs.connections", Ten.AddSeconds(40), first, 8),
            Sum(server.Id, "ovs.connections", Ten.AddSeconds(70), first, 10), Sum(server.Id, "ovs.connections", Ten.AddSeconds(90), second, 2),
            Sum(server.Id, "ovs.connections", Ten.AddSeconds(130), second, 4));

        await Hosted<Rollup>(manager).RollAsync();

        var minutes = await ReadAsync(manager, store => store.MetricRollups.Where(row => row.Level == RollupLevel.Minute).OrderBy(row => row.PeriodUnixMs).ToListAsync());
        Assert.Equal([(Ms(Ten), 8.0), (Ms(Ten.AddMinutes(1)), 4.0), (Ms(Ten.AddMinutes(2)), 2.0)], minutes.Select(row => (row.PeriodUnixMs, row.Value)));
        Assert.Equal(0, await ReadAsync(manager, store => store.MetricPoints.CountAsync()));

        await AddAsync(manager, Sum(server.Id, "ovs.connections", Ten.AddSeconds(190), second, 9)); // the next pass goes on from 4
        time.Advance(TimeSpan.FromMinutes(1));
        await Hosted<Rollup>(manager).RollAsync();
        var hour = await ReadAsync(manager, store => store.MetricRollups.SingleAsync(row => row.Level == RollupLevel.Hour));
        Assert.Equal((Ms(Ten), 19.0), (hour.PeriodUnixMs, hour.Value));
    }

    /// <summary>AC1: gauges keep last, lowest and highest per minute and per hour; a raw point of a running minute waits.</summary>
    [Fact]
    public async Task Minute_ToHour_GaugeLastMinMax()
    {
        var time = new ManualTimeProvider(Ten.AddMinutes(61).AddSeconds(30));
        await using var manager = TelemetryManager(time);
        manager.CreateClient().Dispose();
        var server = await ServerLinks.AddAsync(manager);
        await AddAsync(manager,
            Gauge(server.Id, "ovs.sessions", Ten.AddMinutes(5), 3), Gauge(server.Id, "ovs.sessions", Ten.AddMinutes(5).AddSeconds(30), 9),
            Gauge(server.Id, "ovs.sessions", Ten.AddMinutes(20), 7), Gauge(server.Id, "ovs.sessions", Ten.AddMinutes(59), 5),
            Gauge(server.Id, "ovs.sessions", Ten.AddMinutes(61).AddSeconds(10), 1)); // the running minute

        await Hosted<Rollup>(manager).RollAsync();

        var rows = await ReadAsync(manager, store => store.MetricRollups.ToListAsync());
        var minute = Assert.Single(rows, row => row.Level == RollupLevel.Minute && row.PeriodUnixMs == Ms(Ten.AddMinutes(5)));
        Assert.Equal((9.0, 3.0, 9.0), (minute.Value, minute.Min, minute.Max));
        var hour = Assert.Single(rows, row => row.Level == RollupLevel.Hour);
        Assert.Equal((Ms(Ten), 5.0, 3.0, 9.0), (hour.PeriodUnixMs, hour.Value, hour.Min, hour.Max));
        Assert.Equal(1, await ReadAsync(manager, store => store.MetricPoints.CountAsync()));
    }

    /// <summary>AC3: a finished day in the server's time zone gets its statistics once; a running day gets none.</summary>
    [Fact]
    public async Task DailyStats_DayEnded_WrittenOnce()
    {
        var day = new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.Zero);
        var time = new ManualTimeProvider(day.AddHours(23).AddMinutes(59));
        await using var manager = TelemetryManager(time);
        manager.CreateClient().Dispose();
        var server = await ServerLinks.AddAsync(manager); // its time zone is UTC
        var start = day.AddDays(-1);
        const string In = "{\"ovs.direction\":\"in\",\"ovs.voice.path\":\"udp\"}";
        const string Out = "{\"ovs.direction\":\"out\",\"ovs.voice.path\":\"udp\"}";
        await AddAsync(manager,
            Sum(server.Id, "ovs.connections", day.AddHours(1), start, 3), Sum(server.Id, "ovs.connections", day.AddHours(5), start, 7),
            Gauge(server.Id, "ovs.users.daily", day.AddHours(2), 2), Gauge(server.Id, "ovs.users.daily", day.AddHours(6), 4),
            Gauge(server.Id, "ovs.users.daily.new", day.AddHours(6), 1),
            Sum(server.Id, "ovs.voice.bytes", day.AddHours(1), day, 100, In), Sum(server.Id, "ovs.voice.bytes", day.AddHours(3), day, 250, In),
            Sum(server.Id, "ovs.voice.bytes", day.AddHours(3), day, 50, Out));

        await Hosted<Rollup>(manager).RollAsync();
        Assert.Equal(0, await ReadAsync(manager, store => store.DailyStats.CountAsync()));
        time.Advance(TimeSpan.FromMinutes(2));
        await Hosted<Rollup>(manager).RollAsync();
        await Hosted<Rollup>(manager).RollAsync();

        var stats = Assert.Single(await ReadAsync(manager, store => store.DailyStats.ToListAsync()));
        Assert.Equal((server.Id, DateOnly.FromDateTime(day.UtcDateTime), 7L, 4L, 1L, 250L, 50L),
            (stats.ServerId, stats.Day, stats.Connections, stats.Users, stats.NewUsers, stats.BytesIn, stats.BytesOut));
    }
}
