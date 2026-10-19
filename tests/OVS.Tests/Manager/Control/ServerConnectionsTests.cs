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
    /// <summary>AC1: a running server gets its service session and gets it again after a restart of the server.</summary>
    [Fact]
    public async Task Running_ServerRestarted_ConnectedAgain()
    {
        await using var manager = ServerLinks.Manager();
        manager.CreateClient().Dispose();
        var (id, admin) = await ServerLinks.AddAsync(manager);
        var server = await TestServer.StartAsync(serverAdminFingerprint: admin);
        await ServerLinks.RunAtAsync(manager, id, server.Port);
        await ServerLinks.ConnectedAsync(manager, id);
        var link = ServerLinks.Link(manager, id);
        bool lost = false;
        link.Changed += () => lost |= link.Status != ServerLinkStatus.Connected;

        await using var again = await server.RestartAsync(samePort: true); // the same data folder and port, as a container restart keeps its volume
        await AsyncWait.UntilAsync(() => lost && link.Status == ServerLinkStatus.Connected, ServerLinks.Within);
        Assert.Same(link, ServerLinks.Link(manager, id));
    }

    /// <summary>AC2: a server of another protocol is "veraltet" with its version; no session.</summary>
    [Fact]
    public async Task VersionMismatch_Server_Outdated()
    {
        await using var manager = ServerLinks.Manager();
        manager.CreateClient().Dispose();
        var (id, _) = await ServerLinks.AddAsync(manager);
        var dataDir = Path.Combine(Path.GetTempPath(), "ovs-stub-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDir);
        await using var stub = HandshakeStub.Start(dataDir, new Rejected(ReasonCodes.VersionMismatch, "Server spricht Protokollversion 22"));
        await ServerLinks.RunAtAsync(manager, id, stub.Port);

        await AsyncWait.UntilAsync(() => ServerLinks.Link(manager, id).Status == ServerLinkStatus.Outdated, ServerLinks.Within);
        Assert.Equal(22, ServerLinks.Link(manager, id).OutdatedProtocol);
        Assert.Equal(0, ServerLinks.Link(manager, id).Read(mirror => 1, 0));
    }

    /// <summary>AC3: the first certificate is pinned; another one stops the session until the admin accepts it, with an audit line.</summary>
    [Fact]
    public async Task Certificate_Changed_StoppedUntilAccepted()
    {
        await using var manager = ServerLinks.Manager();
        manager.CreateClient().Dispose();
        var (id, admin) = await ServerLinks.AddAsync(manager);
        await using (var first = await TestServer.StartAsync(serverAdminFingerprint: admin))
        {
            await ServerLinks.RunAtAsync(manager, id, first.Port);
            await ServerLinks.ConnectedAsync(manager, id);
        }
        await using var other = await TestServer.StartAsync(serverAdminFingerprint: admin); // a new data folder: a new certificate
        await ServerLinks.RunAtAsync(manager, id, other.Port);
        await AsyncWait.UntilAsync(() => ServerLinks.Link(manager, id).Status == ServerLinkStatus.CertificateChanged, ServerLinks.Within);
        var offered = ServerLinks.Link(manager, id).OfferedCertificate;
        Assert.NotNull(offered);

        await manager.Services.GetRequiredService<ServerConnections>().AcceptCertificateAsync(id, "chef");
        await ServerLinks.ConnectedAsync(manager, id);
        await using var scope = manager.Services.CreateAsyncScope();
        Assert.Equal(offered, (await scope.ServiceProvider.GetRequiredService<ManagerDbContext>().Servers.SingleAsync()).CertificateFingerprint);
        var line = Assert.Single(await scope.ServiceProvider.GetRequiredService<AuditLog>().QueryAsync(new AuditFilter(Action: AuditActions.CertificateAccepted), servers: null));
        Assert.Equal(("chef", id, offered), (line.UserName, line.ServerId, line.Detail));
    }

    /// <summary>AC4: the mirror follows the users online.</summary>
    [Fact]
    public async Task Mirror_UserJoinsAndLeaves_Followed()
    {
        await using var manager = ServerLinks.Manager();
        manager.CreateClient().Dispose();
        var (id, admin) = await ServerLinks.AddAsync(manager);
        await using var server = await TestServer.StartAsync(serverAdminFingerprint: admin);
        await ServerLinks.RunAtAsync(manager, id, server.Port);
        await ServerLinks.ConnectedAsync(manager, id);
        bool Online(string nickname) => ServerLinks.Link(manager, id).Read(mirror => mirror.Users.Values.Any(user => user.Nickname == nickname), false);

        var anna = await TestClient.ConnectAsync(server, "anna");
        await AsyncWait.UntilAsync(() => Online("anna"), ServerLinks.Within);
        Assert.Equal("Testserver", ServerLinks.Link(manager, id).Read(mirror => mirror.Settings.Name, ""));
        Assert.NotEmpty(ServerLinks.Link(manager, id).Read(mirror => mirror.Channels, []));
        await anna.DisposeAsync();
        await AsyncWait.UntilAsync(() => !Online("anna"), ServerLinks.Within);
    }

    /// <summary>AC5: a user on the server never sees the manager's session.</summary>
    [Fact]
    public async Task Service_UserOnServer_NeverSeesIt()
    {
        await using var manager = ServerLinks.Manager();
        manager.CreateClient().Dispose();
        var (id, admin) = await ServerLinks.AddAsync(manager);
        await using var server = await TestServer.StartAsync(serverAdminFingerprint: admin);
        await using var anna = await TestClient.ConnectAsync(server, "anna");
        await ServerLinks.RunAtAsync(manager, id, server.Port);
        await ServerLinks.ConnectedAsync(manager, id);

        await anna.AssertNoMessageAsync<UserJoined>();
        await using var bert = await TestClient.ConnectAsync(server, "bert");
        Assert.DoesNotContain(bert.Welcome.Snapshot.Users, user => user.Nickname == ServerLink.Nickname);
    }
}
