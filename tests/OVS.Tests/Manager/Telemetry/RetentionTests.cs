using Bunit;
using Microsoft.EntityFrameworkCore;
using OVS.Manager.Components.Pages.Servers;
using OVS.Manager.Telemetry;
using OVS.Tests.TestSupport;
using static OVS.Tests.Manager.Telemetry.TelemetryTesting;

namespace OVS.Tests.Manager.Telemetry;

/// <summary>Package M21: telemetry deleted by age and by the size of the database.</summary>
public sealed class RetentionTests
{
    static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    static LogRecordRow Log(string serverId, DateTimeOffset at, string body = "line") => new() { ServerId = serverId, AtUnixMs = Ms(at), Body = body };

    static MetricRollup Rolled(string serverId, RollupLevel level, DateTimeOffset at) =>
        new() { ServerId = serverId, Name = "ovs.sessions", Level = level, PeriodUnixMs = Ms(at), Kind = MetricKind.Gauge };

    /// <summary>AC2: each kind goes after its retention, one of them set by its variable; daily statistics stay.</summary>
    [Fact]
    public async Task Old_Deleted_ByVariable()
    {
        await using var manager = TelemetryManager(new ManualTimeProvider(Now), ("OVS_MANAGER_RETENTION_LOG_DAYS", "2"));
        manager.CreateClient().Dispose();
        var id = (await ServerLinks.AddAsync(manager)).Id;
        await AddAsync(manager,
            Log(id, Now.AddDays(-3), "old"), Log(id, Now.AddDays(-1), "kept"),
            new SpanRow { ServerId = id, StartUnixMs = Ms(Now.AddDays(-8)), Name = "old" }, new SpanRow { ServerId = id, StartUnixMs = Ms(Now.AddDays(-6)), Name = "kept" },
            Rolled(id, RollupLevel.Minute, Now.AddDays(-8)), Rolled(id, RollupLevel.Minute, Now.AddDays(-6)),
            Rolled(id, RollupLevel.Hour, Now.AddDays(-401)), Rolled(id, RollupLevel.Hour, Now.AddDays(-300)),
            new DailyStats { ServerId = id, Day = new DateOnly(2020, 1, 1) });

        Assert.False(await Hosted<Retention>(manager).TrimAsync());

        Assert.Equal(["kept"], await ReadAsync(manager, store => store.LogRecords.Select(line => line.Body).ToListAsync()));
        Assert.Equal(["kept"], await ReadAsync(manager, store => store.Spans.Select(span => span.Name).ToListAsync()));
        var rolled = await ReadAsync(manager, store => store.MetricRollups.ToListAsync());
        Assert.Equal([(RollupLevel.Minute, Ms(Now.AddDays(-6))), (RollupLevel.Hour, Ms(Now.AddDays(-300)))], rolled.OrderBy(row => row.Level).Select(row => (row.Level, row.PeriodUnixMs)));
        Assert.Equal(1, await ReadAsync(manager, store => store.DailyStats.CountAsync()));
    }

    /// <summary>AC4: over the size limit the oldest raw data goes first until it fits, hourly values stay, and the overview warns.</summary>
    [Fact]
    public async Task OverSize_OldestRawFirst_Warning()
    {
        await using var manager = TelemetryManager(new ManualTimeProvider(Now), ("OVS_MANAGER_TELEMETRY_MAX_MB", "1"));
        manager.CreateClient().Dispose();
        var id = (await ServerLinks.AddAsync(manager)).Id;
        var text = new string('x', 1000);
        await AddAsync(manager, [.. Enumerable.Range(0, 3000).Select(index => Log(id, Now.AddMinutes(-index * 10), text + index)), Rolled(id, RollupLevel.Hour, Now.AddDays(-100))]);

        Assert.True(await Hosted<Retention>(manager).TrimAsync());

        var used = await ReadAsync(manager, store => store.Database.SqlQueryRaw<long>(
            "SELECT (page_count - freelist_count) * page_size AS Value FROM pragma_page_count(), pragma_freelist_count(), pragma_page_size()").SingleAsync());
        Assert.InRange(used, 0, 1024 * 1024);
        var bodies = await ReadAsync(manager, store => store.LogRecords.Select(line => line.Body).ToListAsync());
        Assert.Contains(text + 0, bodies);
        Assert.DoesNotContain(text + 2999, bodies);
        Assert.Equal(1, await ReadAsync(manager, store => store.MetricRollups.CountAsync(row => row.Level == RollupLevel.Hour)));
        using var context = ServerPages.Context(manager);
        Assert.NotEmpty(context.Render<ServerList>().FindAll("[data-telemetry-limit]"));
    }
}
