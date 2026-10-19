using System.Globalization;
using Microsoft.EntityFrameworkCore;
using OVS.Manager.Data;

namespace OVS.Manager.Telemetry;

/// <summary>
/// Package M23 (AC1): a server's figures per day or per month from <see cref="DailyStats"/> (never deleted) and its peak of
/// users online from the hourly <c>ovs.sessions</c> values, in the server's time zone.
/// </summary>
internal sealed class HistoryQuery(TelemetryStore store, ManagerDbContext database)
{
    const long HourMs = 3_600_000;

    /// <summary>The days <paramref name="from"/> to <paramref name="to"/>, each day or each month a row; days without data are 0.</summary>
    public async Task<IReadOnlyList<HistoryRow>> ReadAsync(string serverId, DateOnly from, DateOnly to, bool byMonth, CancellationToken cancellation = default)
    {
        var zoneId = await database.Servers.AsNoTracking().Where(server => server.Id == serverId).Select(server => server.TimeZone).SingleOrDefaultAsync(cancellation);
        var zone = zoneId is not null && TimeZoneInfo.TryFindSystemTimeZoneById(zoneId, out var found) ? found : TimeZoneInfo.Utc;
        var stats = (await store.DailyStats.AsNoTracking().Where(day => day.ServerId == serverId && day.Day >= from && day.Day <= to).ToListAsync(cancellation))
            .ToDictionary(day => day.Day);
        long start = StartUnixMs(from, zone), end = StartUnixMs(to.AddDays(1), zone);
        var hours = await store.MetricRollups.AsNoTracking().Where(row => row.ServerId == serverId && row.Level == RollupLevel.Hour && row.Name == "ovs.sessions"
            && row.PeriodUnixMs >= start && row.PeriodUnixMs < end).Select(row => new { row.PeriodUnixMs, row.Max }).ToListAsync(cancellation);
        var peaks = hours.GroupBy(hour => LocalDay(hour.PeriodUnixMs, zone)).ToDictionary(group => group.Key, group => (long)group.Max(hour => hour.Max));

        var days = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Select(from.AddDays).Select(day =>
        {
            var row = stats.GetValueOrDefault(day);
            return new { Day = day, Row = new HistoryRow(day.ToString("d", CultureInfo.CurrentCulture), row?.Connections ?? 0, row?.Users ?? 0, row?.NewUsers ?? 0,
                peaks.GetValueOrDefault(day), (row?.BytesIn ?? 0) / 1e6, (row?.BytesOut ?? 0) / 1e6) };
        }).ToList();
        if (!byMonth) return [.. days.Select(day => day.Row)];
        return [.. days.GroupBy(day => new DateOnly(day.Day.Year, day.Day.Month, 1)).Select(month => new HistoryRow(month.Key.ToString("MMM yyyy", CultureInfo.CurrentCulture),
            month.Sum(day => day.Row.Connections), month.Max(day => day.Row.Users), month.Sum(day => day.Row.NewUsers), month.Max(day => day.Row.PeakUsers),
            month.Sum(day => day.Row.MegabytesIn), month.Sum(day => day.Row.MegabytesOut)))];
    }

    static DateOnly LocalDay(long unixMs, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTimeOffset.FromUnixTimeMilliseconds(unixMs / HourMs * HourMs).UtcDateTime, zone));

    static long StartUnixMs(DateOnly day, TimeZoneInfo zone) =>
        new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), zone)).ToUnixTimeMilliseconds();
}
