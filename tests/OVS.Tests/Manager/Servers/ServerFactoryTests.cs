using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OVS.Manager.Accounts;
using OVS.Manager.Audit;
using OVS.Manager.Data;
using OVS.Manager.Exceptions;
using OVS.Manager.Servers;
using OVS.Shared;
using OVS.Tests.Manager.Accounts;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Manager.Servers;

/// <summary>Package M7 (D3, D6, MA4, MA6): a new server's container, its identity and who may make one.</summary>
public sealed class ServerFactoryTests
{
    static ManagerFactory WithAdmin() =>
        new((FirstAdmin.UserVariable, "chef"), (FirstAdmin.PasswordVariable, FirstAdminTests.Password), ("TZ", "Europe/Berlin"));

    static Task<ManagedServer> CreateAsync(IServiceProvider services, string password = "") =>
        services.GetRequiredService<ServerFactory>().CreateAsync(new CreateServerRequest("Raid", 20, password, null, null, null), "chef");

    /// <summary>A locally built manager gives a new server the newest server image built on the host, whatever its name; a released one its own release.</summary>
    [Fact]
    public async Task Create_NoVersion_NewestLocalImageForLocalBuild()
    {
        await using var manager = WithAdmin();
        manager.CreateClient().Dispose();
        manager.Docker.NewestServerImage = "openvoicespeak/server:local";
        await using var scope = manager.Services.CreateAsyncScope();

        var server = await CreateAsync(scope.ServiceProvider);

        var spec = Assert.Single(manager.Docker.Created);
        if (BuildInfo.Current.IsCi)
            Assert.Equal((null, "ghcr.io/rezondes/openvoicespeak-server", BuildInfo.Current.Version), (server.Image, spec.Image, spec.Version));
        else
            Assert.Equal(("openvoicespeak/server", "openvoicespeak/server", "local"), (server.Image, spec.Image, spec.Version));
    }

    /// <summary>An image the host lacks is pulled first; one no registry has stops the creation with a text, before anything is made.</summary>
    [Fact]
    public async Task Create_ImageNotOnHost_PulledOrRefused()
    {
        await using var manager = WithAdmin();
        manager.CreateClient().Dispose();
        await using var scope = manager.Services.CreateAsyncScope();
        var image = $"ghcr.io/rezondes/openvoicespeak-server:{ServerFactory.DefaultVersion}";
        manager.Docker.NotOnHost[image] = true;

        await CreateAsync(scope.ServiceProvider);
        Assert.Equal([image], manager.Docker.Pulled);

        manager.Docker.MissingImages["ghcr.io/rezondes/openvoicespeak-server:010101.xx"] = true;
        var refused = await Assert.ThrowsAsync<ServerRequestException>(() => scope.ServiceProvider.GetRequiredService<ServerFactory>()
            .CreateAsync(new CreateServerRequest("Lobby", 20, null, "010101.xx", null, null), "chef"));
        Assert.Equal(ServerProblem.ImageMissing, refused.Problem);
        Assert.Single(manager.Docker.Created);
    }

    /// <summary>AC3: exactly these variables, the image of the manager's version, the port, the network; started.</summary>
    [Fact]
    public async Task Create_ContainerSpec_ExactVariablesAndMappings()
    {
        await using var manager = WithAdmin();
        manager.CreateClient().Dispose();
        await using var scope = manager.Services.CreateAsyncScope();
        var server = await CreateAsync(scope.ServiceProvider);

        var spec = Assert.Single(manager.Docker.Created);
        Assert.Equal((server.Id, "ghcr.io/rezondes/openvoicespeak-server", ServerFactory.DefaultVersion, 7000, "ovs-managed"),
            (spec.ServerId, spec.Image, spec.Version, spec.Port, spec.Network));
        Assert.Equal(new Dictionary<string, string>
        {
            ["OVS_PORT"] = "7000",
            ["OVS_SERVER_NAME"] = "Raid",
            ["OVS_MAX_USERS"] = "20",
            ["TZ"] = "Europe/Berlin",
            ["OVS_SERVER_ADMIN_FINGERPRINT"] = server.AdminFingerprint,
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://ovs-manager:4318",
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
            ["OTEL_SERVICE_NAME"] = "ovs-server",
            ["OTEL_RESOURCE_ATTRIBUTES"] = $"service.instance.id={server.Id}",
        }, spec.Environment.OrderBy(pair => pair.Key).ToDictionary());
        Assert.Equal("running", manager.Docker.Containers[server.ContainerId!].State);

        var withPassword = await CreateAsync(scope.ServiceProvider, "geheim");
        Assert.Equal("geheim", manager.Docker.Created.Last().Environment["OVS_PASSWORD"]);
        Assert.Equal(7001, withPassword.Port);
    }

    /// <summary>AC3: the server identity's private key is in the database only encrypted.</summary>
    [Fact]
    public async Task Identity_PrivateKey_StoredEncrypted()
    {
        await using var manager = WithAdmin();
        manager.CreateClient().Dispose();
        await using var scope = manager.Services.CreateAsyncScope();
        var server = await CreateAsync(scope.ServiceProvider);

        var store = scope.ServiceProvider.GetRequiredService<ServerIdentityStore>();
        using var identity = await store.LoadAsync(server.Id);
        Assert.Equal(server.AdminFingerprint, identity.Fingerprint);
        var stored = await scope.ServiceProvider.GetRequiredService<ManagerDbContext>().ServerIdentities.SingleAsync(row => row.ServerId == server.Id);
        Assert.NotEqual(identity.ExportPkcs8(), stored.ProtectedKey);
        Assert.True(stored.ProtectedKey.AsSpan().IndexOf(identity.ExportPkcs8()) < 0);
    }

    /// <summary>AC4: only the admin creates servers.</summary>
    [Fact]
    public async Task Create_Manager_Forbidden()
    {
        await using var manager = WithAdmin();
        manager.CreateClient().Dispose();
        await ManagerSignIn.CreateAsync(manager, "anna");
        using var client = await ManagerSignIn.LoginAsync(manager, "anna");
        using var page = await client.GetAsync(new Uri("/servers/new", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Forbidden, page.StatusCode);
    }

    /// <summary>AC4: the creation writes its audit line with the server.</summary>
    [Fact]
    public async Task Create_Admin_AuditLine()
    {
        await using var manager = WithAdmin();
        using var client = manager.CreateClient();
        (await LoginTests.LoginAsync(client, "chef", FirstAdminTests.Password)).Dispose();
        using var created = await ManagerForm.SubmitAsync(client, "/servers/new", "create-server",
            ("Model.Name", "Raid"), ("Model.MaxUsers", "20"), ("Model.Port", "7005"));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        await using var scope = manager.Services.CreateAsyncScope();
        var line = Assert.Single(await scope.ServiceProvider.GetRequiredService<AuditLog>().QueryAsync(new AuditFilter(Action: AuditActions.ServerCreated), servers: null));
        Assert.Equal(("chef", "Raid"), (line.UserName, line.Target));
        Assert.Equal(7005, Assert.Single(manager.Docker.Created).Port);
        Assert.Equal(line.ServerId, manager.Docker.Created.Single().ServerId);
    }
}
