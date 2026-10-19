using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OVS.Manager.Audit;
using OVS.Manager.Components.Pages.Servers;
using OVS.Manager.Data;
using OVS.Manager.Docker;
using OVS.Manager.Exceptions;
using OVS.Manager.Servers;
using OVS.Manager.Telemetry;
using OVS.Tests.Manager.Servers;
using OVS.Tests.TestSupport;
using static OVS.Tests.Manager.Telemetry.TelemetryTesting;

namespace OVS.Tests.Manager.Docker;

/// <summary>Package M25: CPU, RAM and volume of a server, its limits, and the free disk of Docker's data root.</summary>
public sealed class ResourceLimitsTests
{
    static readonly Dictionary<string, string> Variables = new() { ["OVS_PORT"] = "7000", ["TZ"] = "UTC" };

    /// <summary>AC1: CPU and RAM now from Docker, their hourly peaks of 24 hours from the sampler, and the volume's size.</summary>
    [Fact]
    public async Task Usage_Sampled_ShownWithVolumeSize()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 19, 12, 0, 30, TimeSpan.Zero));
        await using var manager = TelemetryManager(time);
        manager.CreateClient().Dispose();
        var id = (await ServerLinks.AddAsync(manager)).Id;
        await ServerLinks.RunAtAsync(manager, id, 7000);
        manager.Docker.Usage["c-" + id] = new ContainerUsage(37.5, 300L * 1024 * 1024);
        manager.Docker.VolumeSizes[ManagedLabels.VolumeName(id)] = 1_500_000_000;
        await Hosted<UsageSampler>(manager).SampleAsync();
        time.Advance(TimeSpan.FromMinutes(1));
        await Hosted<Rollup>(manager).RollAsync();

        await using var context = ServerPages.Context(manager);
        var page = context.Render<ResourceUse>(parameters => parameters.Add(resources => resources.Id, id));

        Assert.Equal(37.5.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture), page.Find("[data-tile=cpu] [data-value]").TextContent);
        Assert.Equal("300", page.Find("[data-tile=ram] [data-value]").TextContent);
        Assert.Equal(1.5.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture), page.Find("[data-tile=volume] [data-value]").TextContent);
        Assert.Single(page.FindAll("[data-chart=CPU] circle")); // the hour of the sample
    }

    /// <summary>AC1: the volume sizes come from Docker's system/df answer; a volume without usage data is left out.</summary>
    [Fact]
    public void VolumeUsage_Answer_SizesByName() =>
        Assert.Equal(new Dictionary<string, long> { ["ovs-ab12cd34-data"] = 1234 },
            VolumeUsage.Parse("""{"Volumes":[{"Name":"ovs-ab12cd34-data","UsageData":{"Size":1234,"RefCount":1}},{"Name":"other","UsageData":null}],"Images":[]}"""));

    /// <summary>AC2: limits at creation go into the container; a change after the confirmation makes the container again with them.</summary>
    [Fact]
    public async Task Limits_Change_Recreated()
    {
        await using var manager = StatusWatcherTests.WithAdmin();
        manager.CreateClient().Dispose();
        await using (var scope = manager.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ServerFactory>().CreateAsync(new CreateServerRequest("Raid", 20, null, null, null, null, new ResourceLimits(2, 1024)), "chef");
        Assert.Equal(new ResourceLimits(2, 1024), manager.Docker.Created.Single().Limits);
        var server = manager.Docker.Containers.Values.Single();
        manager.Docker.Inspected[server.Id] = new InspectedContainer("running", 0, 0, DateTimeOffset.UtcNow, Variables);
        await manager.Services.GetRequiredService<StatusWatcher>().RefreshAsync();

        await using var context = ServerPages.Context(manager);
        var page = context.Render<ResourceUse>(parameters => parameters.Add(resources => resources.Id, server.ServerId));
        await page.Find("[data-cpu]").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "1.5" });
        await page.Find("[data-memory]").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "512" });
        await page.Find("[data-change]").ClickAsync(new());
        Assert.Single(page.FindAll("[data-confirm-text]"));
        Assert.Single(manager.Docker.Created); // nothing yet before the confirmation
        await page.Find("[data-confirm]").ClickAsync(new());

        var recreated = manager.Docker.Created.Last();
        Assert.Equal((new ResourceLimits(1.5, 512), Variables), (recreated.Limits, recreated.Environment));
        Assert.False(manager.Docker.Containers.ContainsKey(server.Id));
        Assert.Equal("running", manager.Docker.Containers.Values.Single().State);
        await using var check = manager.Services.CreateAsyncScope();
        var row = await check.ServiceProvider.GetRequiredService<ManagerDbContext>().Servers.SingleAsync();
        Assert.Equal((1.5, 512), (row.CpuCores, row.MemoryMb));
        var lines = await check.ServiceProvider.GetRequiredService<AuditLog>().QueryAsync(new AuditFilter(ServerId: row.Id), servers: null);
        Assert.Contains(lines, line => line.Action == AuditActions.ServerLimitsChanged);
        await Assert.ThrowsAsync<ServerRequestException>(() => check.ServiceProvider.GetRequiredService<ServerResources>().SetLimitsAsync(row.Id, new ResourceLimits(0, null), "chef"));
    }

    /// <summary>AC3: the overview shows the data root's free disk and warns below 10 %.</summary>
    [Fact]
    public async Task Disk_BelowTenPercent_Warning()
    {
        await using var manager = StatusWatcherTests.WithAdmin();
        manager.CreateClient().Dispose();
        await using var context = ServerPages.Context(manager);

        var page = context.Render<ServerList>();
        Assert.Empty(page.FindAll("[data-disk-low]"));
        Assert.StartsWith("250", page.Find("[data-tile=disk] [data-value]").TextContent, StringComparison.Ordinal);

        manager.Disk.Space = new DiskSpace(40_000_000_000, 500_000_000_000);
        Assert.NotEmpty(context.Render<ServerList>().FindAll("[data-disk-low]"));
    }
}
