using Bunit;
using Microsoft.Extensions.DependencyInjection;
using OVS.Manager.Components.Pages.Servers;
using OVS.Manager.Docker;
using OVS.Manager.Telemetry;
using OVS.Tests.TestSupport;
using static OVS.Tests.Manager.Telemetry.TelemetryTesting;

namespace OVS.Tests.Manager.Telemetry;

/// <summary>Package M22: the live figures of a server and their page.</summary>
public sealed class LiveFeedTests
{
    static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 30, TimeSpan.Zero);

    static async Task<LiveSnapshot> ReadAsync(ManagerFactory manager, string serverId)
    {
        await using var scope = manager.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<LiveFeed>().ReadAsync(serverId);
    }

    /// <summary>AC1: a new value and Docker's CPU reach the open page within 10 s, without a reload.</summary>
    [Fact]
    public async Task Page_NewPoint_ShownWithinTenSeconds()
    {
        var time = new ManualTimeProvider(Now);
        await using var manager = TelemetryManager(time);
        manager.CreateClient().Dispose();
        var id = (await ServerLinks.AddAsync(manager)).Id;
        await ServerLinks.RunAtAsync(manager, id, 7000);
        await AddAsync(manager, Gauge(id, "ovs.sessions", Now.AddSeconds(-20), 2));
        await using var context = ServerPages.Context(manager);
        context.Services.AddSingleton<TimeProvider>(time);
        var page = context.Render<LiveView>(parameters => parameters.Add(view => view.ServerId, id));
        Assert.Equal("2", page.Find("[data-tile=users] [data-value]").TextContent);

        await AddAsync(manager, Gauge(id, "ovs.sessions", Now, 4));
        manager.Docker.Usage["c-" + id] = new ContainerUsage(12.5, 256L * 1024 * 1024);
        time.Advance(LiveView.Every);

        page.WaitForAssertion(() => Assert.Equal("4", page.Find("[data-tile=users] [data-value]").TextContent), TimeSpan.FromSeconds(10));
        page.WaitForAssertion(() => Assert.Equal((12.5).ToString("0.#", System.Globalization.CultureInfo.CurrentCulture), page.Find("[data-tile=cpu] [data-value]").TextContent));
        Assert.Equal("256", page.Find("[data-tile=ram] [data-value]").TextContent);
    }

    /// <summary>AC1: the last minute's bytes are kbit/s in and out, and its drops per minute.</summary>
    [Fact]
    public async Task Minutes_BytesAndDrops_KbitAndPerMinute()
    {
        await using var manager = TelemetryManager(new ManualTimeProvider(Now));
        manager.CreateClient().Dispose();
        var id = (await ServerLinks.AddAsync(manager)).Id;
        var minute = Ms(Now.AddSeconds(-90)) / 60_000 * 60_000;
        await AddAsync(manager,
            new MetricRollup { ServerId = id, Name = "ovs.voice.bytes", Kind = MetricKind.Sum, PeriodUnixMs = minute, Value = 600_000, Attributes = "{\"ovs.direction\":\"in\"}" },
            new MetricRollup { ServerId = id, Name = "ovs.control.bytes", Kind = MetricKind.Sum, PeriodUnixMs = minute, Value = 150_000, Attributes = "{\"ovs.direction\":\"in\"}" },
            new MetricRollup { ServerId = id, Name = "ovs.voice.bytes", Kind = MetricKind.Sum, PeriodUnixMs = minute, Value = 75_000, Attributes = "{\"ovs.direction\":\"out\"}" },
            new MetricRollup { ServerId = id, Name = "ovs.voice.drops", Kind = MetricKind.Sum, PeriodUnixMs = minute, Value = 3, Attributes = "{\"ovs.drop.reason\":\"budget\"}" });

        var snapshot = await ReadAsync(manager, id);

        Assert.Equal((100.0, 10.0, 3.0, true), (snapshot.KbitIn, snapshot.KbitOut, snapshot.DropsPerMinute, snapshot.HasTelemetry));
        Assert.Single(snapshot.KbitInLine);
    }

    /// <summary>AC3: no data for 2 minutes is "keine Telemetrie" and no telemetry lines; a fresh point brings them back.</summary>
    [Fact]
    public async Task NoData_TwoMinutes_NoTelemetry()
    {
        await using var manager = TelemetryManager(new ManualTimeProvider(Now));
        manager.CreateClient().Dispose();
        var id = (await ServerLinks.AddAsync(manager)).Id;
        await AddAsync(manager, Gauge(id, "ovs.sessions", Now.AddMinutes(-3), 5));

        Assert.False((await ReadAsync(manager, id)).HasTelemetry);
        await using (var context = ServerPages.Context(manager))
        {
            var page = context.Render<LiveView>(parameters => parameters.Add(view => view.ServerId, id));
            Assert.NotEmpty(page.FindAll("[data-no-telemetry]"));
            Assert.Empty(page.FindAll("[data-chart=\"Nutzer online\"]"));
        }

        await AddAsync(manager, Gauge(id, "ovs.sessions", Now.AddSeconds(-30), 5));
        Assert.True((await ReadAsync(manager, id)).HasTelemetry);
    }
}
