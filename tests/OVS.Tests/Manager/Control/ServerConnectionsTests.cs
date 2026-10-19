using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OVS.Manager.Accounts;
using OVS.Manager.Audit;
using OVS.Manager.Control;
using OVS.Manager.Data;
using OVS.Manager.Docker;
using OVS.Manager.Servers;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.Users;
using OVS.Tests.Manager.Accounts;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Manager.Control;

/// <summary>Package M11 (A205): the manager's invisible service session to each running server, against in-process servers.</summary>
public sealed class ServerConnectionsTests
{
    static readonly TimeSpan Within = TimeSpan.FromSeconds(15);

    static ManagerFactory WithAdmin() =>
        new((FirstAdmin.UserVariable, "chef"), (FirstAdmin.PasswordVariable, FirstAdminTests.Password), ("OVS_MANAGER_SERVER_HOST", "127.0.0.1"));

    /// <summary>A managed server row with the manager's identity for it, its port not yet known (0 until <see cref="RunAtAsync"/>).</summary>
    static async Task<AddedServer> AddServerAsync(ManagerFactory manager)
    {
        await using var scope = manager.Services.CreateAsyncScope();
        var id = Guid.NewGuid().ToString("N")[..8];
        using var identity = scope.ServiceProvider.GetRequiredService<ServerIdentityStore>().Create(id);
        var database = scope.ServiceProvider.GetRequiredService<ManagerDbContext>();
        database.Servers.Add(new ManagedServer
        {
            Id = id, Name = "Raid", Port = -1, Version = "test", TimeZone = "UTC", MaxUsers = 20, AdminFingerprint = identity.Fingerprint,
            ContainerId = "c-" + id, CreatedAtUtc = DateTime.UtcNow,
        });
        await database.SaveChangesAsync();
        return new AddedServer(id, identity.Fingerprint);
    }

    /// <summary>Gives the server the port <paramref name="port"/> and a running container, and lets the watcher see it.</summary>
    static async Task RunAtAsync(ManagerFactory manager, string id, int port)
    {
        await using (var scope = manager.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ManagerDbContext>().Servers.Where(row => row.Id == id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Port, port));
        manager.Docker.Containers["c-" + id] = new ManagedContainer("c-" + id, id, port, "test", "running", "image:test");
        manager.Docker.Inspected["c-" + id] = new InspectedContainer("running", 0, 0, DateTimeOffset.UtcNow);
        await manager.Services.GetRequiredService<StatusWatcher>().RefreshAsync();
    }

    static ServerLink Link(ManagerFactory manager, string id) => manager.Services.GetRequiredService<ServerConnections>().LinkOf(id)!;

    static Task ConnectedAsync(ManagerFactory manager, string id) =>
        AsyncWait.UntilAsync(() => manager.Services.GetRequiredService<ServerConnections>().LinkOf(id)?.Status == ServerLinkStatus.Connected, Within);

    /// <summary>AC1: a running server gets its service session and gets it again after a restart of the server.</summary>
    [Fact]
    public async Task Running_ServerRestarted_ConnectedAgain()
    {
        await using var manager = WithAdmin();
        manager.CreateClient().Dispose();
        var (id, admin) = await AddServerAsync(manager);
        var server = await TestServer.StartAsync(serverAdminFingerprint: admin);
        await RunAtAsync(manager, id, server.Port);
        await ConnectedAsync(manager, id);
        var link = Link(manager, id);
        bool lost = false;
        link.Changed += () => lost |= link.Status != ServerLinkStatus.Connected;

        await using var again = await server.RestartAsync(samePort: true); // the same data folder and port, as a container restart keeps its volume
        await AsyncWait.UntilAsync(() => lost && link.Status == ServerLinkStatus.Connected, Within);
        Assert.Same(link, Link(manager, id));
    }

    /// <summary>AC2: a server of another protocol is "veraltet" with its version; no session.</summary>
    [Fact]
    public async Task VersionMismatch_Server_Outdated()
    {
        await using var manager = WithAdmin();
        manager.CreateClient().Dispose();
        var (id, _) = await AddServerAsync(manager);
        var dataDir = Path.Combine(Path.GetTempPath(), "ovs-stub-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDir);
        await using var stub = HandshakeStub.Start(dataDir, new Rejected(ReasonCodes.VersionMismatch, "Server spricht Protokollversion 22"));
        await RunAtAsync(manager, id, stub.Port);

        await AsyncWait.UntilAsync(() => Link(manager, id).Status == ServerLinkStatus.Outdated, Within);
        Assert.Equal(22, Link(manager, id).OutdatedProtocol);
        Assert.Equal(0, Link(manager, id).Read(mirror => 1, 0));
    }

    /// <summary>AC3: the first certificate is pinned; another one stops the session until the admin accepts it, with an audit line.</summary>
    [Fact]
    public async Task Certificate_Changed_StoppedUntilAccepted()
    {
        await using var manager = WithAdmin();
        manager.CreateClient().Dispose();
        var (id, admin) = await AddServerAsync(manager);
        await using (var first = await TestServer.StartAsync(serverAdminFingerprint: admin))
        {
            await RunAtAsync(manager, id, first.Port);
            await ConnectedAsync(manager, id);
        }
        await using var other = await TestServer.StartAsync(serverAdminFingerprint: admin); // a new data folder: a new certificate
        await RunAtAsync(manager, id, other.Port);
        await AsyncWait.UntilAsync(() => Link(manager, id).Status == ServerLinkStatus.CertificateChanged, Within);
        var offered = Link(manager, id).OfferedCertificate;
        Assert.NotNull(offered);

        await manager.Services.GetRequiredService<ServerConnections>().AcceptCertificateAsync(id, "chef");
        await ConnectedAsync(manager, id);
        await using var scope = manager.Services.CreateAsyncScope();
        Assert.Equal(offered, (await scope.ServiceProvider.GetRequiredService<ManagerDbContext>().Servers.SingleAsync()).CertificateFingerprint);
        var line = Assert.Single(await scope.ServiceProvider.GetRequiredService<AuditLog>().QueryAsync(new AuditFilter(Action: AuditActions.CertificateAccepted), servers: null));
        Assert.Equal(("chef", id, offered), (line.UserName, line.ServerId, line.Detail));
    }

    /// <summary>AC4: the mirror follows the users online.</summary>
    [Fact]
    public async Task Mirror_UserJoinsAndLeaves_Followed()
    {
        await using var manager = WithAdmin();
        manager.CreateClient().Dispose();
        var (id, admin) = await AddServerAsync(manager);
        await using var server = await TestServer.StartAsync(serverAdminFingerprint: admin);
        await RunAtAsync(manager, id, server.Port);
        await ConnectedAsync(manager, id);
        bool Online(string nickname) => Link(manager, id).Read(mirror => mirror.Users.Values.Any(user => user.Nickname == nickname), false);

        var anna = await TestClient.ConnectAsync(server, "anna");
        await AsyncWait.UntilAsync(() => Online("anna"), Within);
        Assert.Equal("Testserver", Link(manager, id).Read(mirror => mirror.Settings.Name, ""));
        Assert.NotEmpty(Link(manager, id).Read(mirror => mirror.Channels, []));
        await anna.DisposeAsync();
        await AsyncWait.UntilAsync(() => !Online("anna"), Within);
    }

    /// <summary>AC5: a user on the server never sees the manager's session.</summary>
    [Fact]
    public async Task Service_UserOnServer_NeverSeesIt()
    {
        await using var manager = WithAdmin();
        manager.CreateClient().Dispose();
        var (id, admin) = await AddServerAsync(manager);
        await using var server = await TestServer.StartAsync(serverAdminFingerprint: admin);
        await using var anna = await TestClient.ConnectAsync(server, "anna");
        await RunAtAsync(manager, id, server.Port);
        await ConnectedAsync(manager, id);

        await anna.AssertNoMessageAsync<UserJoined>();
        await using var bert = await TestClient.ConnectAsync(server, "bert");
        Assert.DoesNotContain(bert.Welcome.Snapshot.Users, user => user.Nickname == ServerLink.Nickname);
    }

    /// <summary>A server row and the fingerprint of the manager's identity for it.</summary>
    sealed record AddedServer(string Id, string AdminFingerprint);
}
