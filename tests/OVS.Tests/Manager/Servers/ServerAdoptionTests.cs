using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OVS.Manager.Audit;
using OVS.Manager.Components.Pages.Servers;
using OVS.Manager.Control;
using OVS.Manager.Data;
using OVS.Manager.Docker;
using OVS.Manager.Exceptions;
using OVS.Manager.Servers;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Manager.Servers;

/// <summary>Package M29: a foreign OVS container taken over with its data.</summary>
public sealed class ServerAdoptionTests
{
    const string OldId = "0123456789abcdef0123456789abcdef";

    static ForeignServer Old(string? data = "ovs-old-data") => new(OldId, "mein-ovs", "ghcr.io/rezondes/openvoicespeak-server:191026.ab", "running", data,
        new Dictionary<string, string> { ["OVS_SERVER_NAME"] = "Gilde", ["OVS_MAX_USERS"] = "30", ["TZ"] = "UTC", ["OVS_PASSWORD"] = "geheim" });

    static ManagerFactory Manager() => ServerLinks.Manager(("OVS_MANAGER_UPDATE_HEALTH_SECONDS", "2"));

    static async Task<string?> AdoptAsync(ManagerFactory manager, int port = 7005)
    {
        await using var scope = manager.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ServerAdoption>().AdoptAsync(OldId, port, "chef");
    }

    /// <summary>AC1: the admin sees the foreign server; adopting needs a port in range and a container with data.</summary>
    [Fact]
    public async Task Foreign_Listed_AdoptNeedsPort()
    {
        await using var manager = Manager();
        manager.CreateClient().Dispose();
        manager.Docker.Foreign[OldId] = Old();
        await using var context = ServerPages.Context(manager);

        var page = context.Render<AdoptServer>();

        Assert.Single(page.FindAll($"tr[data-foreign='{OldId}']"));
        Assert.Equal("7000", page.Find("[data-port]").GetAttribute("value"));
        var problem = await Assert.ThrowsAsync<ServerRequestException>(() => AdoptAsync(manager, 6000));
        Assert.Equal(ServerProblem.PortOutOfRange, problem.Problem);
        manager.Docker.Foreign[OldId] = Old(data: null);
        Assert.Equal(ServerProblem.NoDataVolume, (await Assert.ThrowsAsync<ServerRequestException>(() => AdoptAsync(manager))).Problem);
    }

    /// <summary>AC2, AC4: the new container mounts the old data as it is with the manager's variables; the old one goes after the health check, with an audit line.</summary>
    [Fact]
    public async Task Adopt_VolumeKept_OldRemovedAfterHealth()
    {
        await using var manager = Manager();
        manager.CreateClient().Dispose();
        manager.Docker.Foreign[OldId] = Old();

        Assert.Null(await AdoptAsync(manager));

        var spec = Assert.Single(manager.Docker.Created);
        Assert.Equal(("ovs-old-data", 7005, "191026.ab"), (spec.DataSource, spec.Port, spec.Version));
        Assert.Equal(("Gilde", "30", "geheim"), (spec.Environment["OVS_SERVER_NAME"], spec.Environment["OVS_MAX_USERS"], spec.Environment["OVS_PASSWORD"]));
        Assert.Empty(manager.Docker.Foreign);
        await using var scope = manager.Services.CreateAsyncScope();
        var row = await scope.ServiceProvider.GetRequiredService<ManagerDbContext>().Servers.SingleAsync();
        Assert.Equal(row.AdminFingerprint, spec.Environment["OVS_SERVER_ADMIN_FINGERPRINT"]);
        Assert.Equal(("ovs-old-data", "ovs-old-data"), (row.DataSource, row.Volume));
        Assert.Null(new ManagedServer { Id = row.Id, DataSource = "/srv/ovs" }.Volume);
        var lines = await scope.ServiceProvider.GetRequiredService<AuditLog>().QueryAsync(new AuditFilter(ServerId: row.Id), servers: null);
        Assert.Contains(lines, line => line.Action == AuditActions.ServerAdopted && line.Detail!.StartsWith("0123456789ab -> ", StringComparison.Ordinal));
    }

    /// <summary>AC2: a self-built server of another image name runs on with exactly its image and version, without a pull, and keeps them for a restart.</summary>
    [Fact]
    public async Task Adopt_SelfBuiltImage_ImageAndVersionKept()
    {
        await using var manager = Manager();
        manager.CreateClient().Dispose();
        manager.Docker.Foreign[OldId] = Old() with { Image = "openvoicespeak/server:local" };

        Assert.Null(await AdoptAsync(manager));

        var spec = Assert.Single(manager.Docker.Created);
        Assert.Equal(("openvoicespeak/server", "local"), (spec.Image, spec.Version));
        Assert.Empty(manager.Docker.Pulled);
        await using var scope = manager.Services.CreateAsyncScope();
        var row = await scope.ServiceProvider.GetRequiredService<ManagerDbContext>().Servers.SingleAsync();
        Assert.Equal(("openvoicespeak/server", "local"), (row.Image, row.Version));
    }

    /// <summary>AC2: a server whose image predates the manager (no protocol label) or names no version is refused before anything changes.</summary>
    [Fact]
    public async Task Adopt_UnknownVersion_RefusedAsTooOld()
    {
        await using var manager = Manager();
        manager.CreateClient().Dispose();

        foreach (var old in new[] { Old() with { KnowsManager = false }, Old() with { Image = "sha256:0123456789abcdef" } })
        {
            manager.Docker.Foreign[OldId] = old;
            Assert.Equal(ServerProblem.TooOldForManager, (await Assert.ThrowsAsync<ServerRequestException>(() => AdoptAsync(manager))).Problem);
        }
        Assert.Empty(manager.Docker.Created);
        Assert.Equal("running", manager.Docker.Foreign[OldId].State);
    }

    /// <summary>AC2: a failed health check removes the new container and starts the old one again.</summary>
    [Fact]
    public async Task Adopt_HealthFails_OldStartedAgain()
    {
        await using var manager = Manager();
        manager.CreateClient().Dispose();
        manager.Docker.Foreign[OldId] = Old();
        manager.Probe.Silent["127.0.0.1:7005"] = 0;

        Assert.NotNull(await AdoptAsync(manager));

        Assert.Equal("running", manager.Docker.Foreign[OldId].State);
        Assert.Empty(manager.Docker.Containers);
        await using var scope = manager.Services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<ManagerDbContext>().Servers.AnyAsync());
    }

    /// <summary>AC3: the confirmation says what happens to the old server admin; after the adoption the service session is there.</summary>
    [Fact]
    public async Task Confirm_OldServerAdmin_NamedThenReplaced()
    {
        await using var manager = Manager();
        manager.CreateClient().Dispose();
        manager.Docker.Foreign[OldId] = Old();
        await using var context = ServerPages.Context(manager);
        var page = context.Render<AdoptServer>();

        await page.Find("[data-adopt]").ClickAsync(new());
        Assert.Contains("Server-Admin", page.Find("[data-confirm]").TextContent, StringComparison.Ordinal);
        await page.Find("[data-confirm-adopt]").ClickAsync(new());

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-result]")));
        var id = manager.Docker.Containers.Values.Single().ServerId;
        Assert.NotNull(manager.Services.GetRequiredService<ServerConnections>().LinkOf(id));
    }
}
