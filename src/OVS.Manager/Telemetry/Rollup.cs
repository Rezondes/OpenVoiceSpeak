using Microsoft.EntityFrameworkCore;
using OVS.Manager.Data;

namespace OVS.Manager.Telemetry;

/// <summary>
/// Package M21 (AC1, AC3, MA7): once a minute, turns the raw points of every finished minute into minute values (deltas
/// of cumulative counters, also across a server restart), keeps each touched hour's value in step, and writes the daily
/// statistics of every finished day in the server's time zone. The raw points are deleted once rolled up.
/// </summary>
internal sealed class Rollup(IServiceScopeFactory scopes, TimeProvider time) : BackgroundService
{
    /// <summary>How many raw points one pass takes; the next pass takes the rest.</summary>
    const int Batch = 50_000;

    const long MinuteMs = 60_000;
    const long HourMs = 3_600_000;

    /// <summary>How many days back finished days are looked for (the minute values' default retention).</summary>
    const int DayLookback = 7;

    /// <summary>One pass: raw points to minutes, the touched hours, and the finished days.</summary>
    public async Task RollAsync(CancellationToken cancellation = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<TelemetryStore>();
        var now = time.GetUtcNow().ToUnixTimeMilliseconds();
        var raw = await store.MetricPoints.Where(point => point.AtUnixMs < now - now % MinuteMs).OrderBy(point => point.AtUnixMs).ThenBy(point => point.Id)
            .Take(Batch).ToListAsync(cancellation);
        if (raw.Count > 0)
        {
            var touched = await ToMinutesAsync(store, raw, cancellation);
            store.MetricPoints.RemoveRange(raw);
            await store.SaveChangesAsync(cancellation);
            await ToHoursAsync(store, touched, cancellation);
        }
        await WriteDaysAsync(store, scope.ServiceProvider.GetRequiredService<ManagerDbContext>(), now, cancellation);
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield(); // the first pass must not hold up the host's start
        using var minutely = new PeriodicTimer(TimeSpan.FromMinutes(1), time);
        while (await minutely.WaitForNextTickAsync(stoppingToken)) await RollAsync(stoppingToken);
    }

    /// <summary>Adds <paramref name="raw"/> to the minute values; returns the touched minute rows.</summary>
    static async Task<List<MetricRollup>> ToMinutesAsync(TelemetryStore store, List<MetricPoint> raw, CancellationToken cancellation)
    {
        var servers = raw.Select(point => point.ServerId).Distinct().ToList();
        var first = raw[0].AtUnixMs - raw[0].AtUnixMs % MinuteMs;
        var states = (await store.CounterStates.Where(state => servers.Contains(state.ServerId)).ToListAsync(cancellation))
            .ToDictionary(state => (state.ServerId, state.Name, state.Attributes));
        var minutes = (await store.MetricRollups.Where(row => row.Level == RollupLevel.Minute && row.PeriodUnixMs >= first && servers.Contains(row.ServerId))
            .ToListAsync(cancellation)).ToDictionary(row => (row.ServerId, row.Name, row.Attributes, row.PeriodUnixMs));
        var touched = new HashSet<MetricRollup>();
        foreach (var point in raw)
        {
            var minute = point.AtUnixMs - point.AtUnixMs % MinuteMs;
            var increase = point.Kind == MetricKind.Gauge ? new Increase(point.Value, null) : Delta(store, states, point);
            var (value, count) = (increase.Value, increase.Count);
            if (!minutes.TryGetValue((point.ServerId, point.Name, point.Attributes, minute), out var row))
            {
                row = new MetricRollup
                {
                    ServerId = point.ServerId, Name = point.Name, Attributes = point.Attributes, Kind = point.Kind, Level = RollupLevel.Minute, PeriodUnixMs = minute,
                    Min = value, Max = value,
                };
                store.MetricRollups.Add(row);
                minutes[(point.ServerId, point.Name, point.Attributes, minute)] = row;
            }
            else if (point.Kind == MetricKind.Gauge)
            {
                row.Min = Math.Min(row.Min, value);
                row.Max = Math.Max(row.Max, value);
            }
            if (point.Kind == MetricKind.Gauge) row.Value = value; // the points come in time order: the last one stays
            else
            {
                row.Value += value;
                if (count is not null) row.Count = (row.Count ?? 0) + count;
            }
            touched.Add(row);
        }
        return [.. touched];
    }

    /// <summary>AC1: what a cumulative point added since the series' last one; after a restart (other start, lower value) all of it.</summary>
    static Increase Delta(TelemetryStore store, Dictionary<(string, string, string), CounterState> states, MetricPoint point)
    {
        if (!states.TryGetValue((point.ServerId, point.Name, point.Attributes), out var state))
        {
            state = new CounterState { ServerId = point.ServerId, Name = point.Name, Attributes = point.Attributes, StartUnixMs = point.StartUnixMs };
            store.CounterStates.Add(state);
            states[(point.ServerId, point.Name, point.Attributes)] = state;
        }
        else if (state.StartUnixMs == point.StartUnixMs && point.Value >= state.LastValue)
        {
            var value = point.Value - state.LastValue;
            long? count = point.Count - state.LastCount;
            Remember(state, point);
            return new Increase(value, point.Count is null ? null : count);
        }
        Remember(state, point);
        return new Increase(point.Value, point.Count);
    }

    static void Remember(CounterState state, MetricPoint point)
    {
        state.StartUnixMs = point.StartUnixMs;
        state.LastValue = point.Value;
        state.LastCount = point.Count;
    }

    /// <summary>Makes each hour of <paramref name="minutes"/> again from all its minute values.</summary>
    static async Task ToHoursAsync(TelemetryStore store, List<MetricRollup> minutes, CancellationToken cancellation)
    {
        var servers = minutes.Select(row => row.ServerId).Distinct().ToList();
        var first = minutes.Min(row => row.PeriodUnixMs) / HourMs * HourMs;
        var last = minutes.Max(row => row.PeriodUnixMs) / HourMs * HourMs + HourMs;
        var all = await store.MetricRollups.Where(row => row.PeriodUnixMs >= first && row.PeriodUnixMs < last && servers.Contains(row.ServerId)).ToListAsync(cancellation);
        var hours = all.Where(row => row.Level == RollupLevel.Hour).ToDictionary(row => (row.ServerId, row.Name, row.Attributes, row.PeriodUnixMs));
        var inHour = all.Where(row => row.Level == RollupLevel.Minute).ToLookup(row => (row.ServerId, row.Name, row.Attributes, row.PeriodUnixMs / HourMs * HourMs));
        foreach (var key in minutes.Select(row => (row.ServerId, row.Name, row.Attributes, row.PeriodUnixMs / HourMs * HourMs)).Distinct())
        {
            var parts = inHour[key].OrderBy(row => row.PeriodUnixMs).ToList();
            if (!hours.TryGetValue(key, out var hour))
            {
                hour = new MetricRollup { ServerId = key.ServerId, Name = key.Name, Attributes = key.Attributes, Kind = parts[0].Kind, Level = RollupLevel.Hour, PeriodUnixMs = key.Item4 };
                store.MetricRollups.Add(hour);
            }
            bool gauge = hour.Kind == MetricKind.Gauge;
            hour.Value = gauge ? parts[^1].Value : parts.Sum(row => row.Value);
            hour.Min = parts.Min(row => row.Min);
            hour.Max = parts.Max(row => row.Max);
            hour.Count = parts.Any(row => row.Count is not null) ? parts.Sum(row => row.Count ?? 0) : null;
        }
        await store.SaveChangesAsync(cancellation);
    }

    /// <summary>AC3: the statistics of each finished day of the last week in each server's time zone, once.</summary>
    static async Task WriteDaysAsync(TelemetryStore store, ManagerDbContext database, long now, CancellationToken cancellation)
    {
        var servers = await database.Servers.AsNoTracking().Select(server => new { server.Id, server.TimeZone }).ToListAsync(cancellation);
        foreach (var server in servers)
        {
            var zone = TimeZoneInfo.TryFindSystemTimeZoneById(server.TimeZone, out var found) ? found : TimeZoneInfo.Utc;
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTimeOffset.FromUnixTimeMilliseconds(now).UtcDateTime, zone));
            var written = await store.DailyStats.Where(day => day.ServerId == server.Id && day.Day >= today.AddDays(-DayLookback)).Select(day => day.Day).ToListAsync(cancellation);
            for (var day = today.AddDays(-DayLookback); day < today; day = day.AddDays(1))
            {
                if (written.Contains(day)) continue;
                var start = StartUnixMs(day, zone);
                var end = StartUnixMs(day.AddDays(1), zone);
                var rows = await store.MetricRollups.AsNoTracking().Where(row => row.ServerId == server.Id && row.Level == RollupLevel.Minute
                    && row.PeriodUnixMs >= start && row.PeriodUnixMs < end).ToListAsync(cancellation);
                if (rows.Count == 0) continue;
                store.DailyStats.Add(new DailyStats
                {
                    ServerId = server.Id, Day = day,
                    Connections = (long)rows.Where(row => row.Name == "ovs.connections").Sum(row => row.Value),
                    Users = (long)rows.Where(row => row.Name == "ovs.users.daily").Select(row => row.Max).DefaultIfEmpty().Max(),
                    NewUsers = (long)rows.Where(row => row.Name == "ovs.users.daily.new").Select(row => row.Max).DefaultIfEmpty().Max(),
                    BytesIn = Bytes(rows, "in"), BytesOut = Bytes(rows, "out"),
                });
            }
        }
        await store.SaveChangesAsync(cancellation);
    }

    static long Bytes(List<MetricRollup> rows, string direction) =>
        (long)rows.Where(row => row.Name is "ovs.voice.bytes" or "ovs.control.bytes" && row.Attributes.Contains($"\"ovs.direction\":\"{direction}\"", StringComparison.Ordinal))
            .Sum(row => row.Value);

    static long StartUnixMs(DateOnly day, TimeZoneInfo zone) =>
        new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), zone)).ToUnixTimeMilliseconds();

    /// <summary>What a point adds to its minute: a gauge's value, or a counter's delta with a histogram's count.</summary>
    sealed record Increase(double Value, long? Count);
}
