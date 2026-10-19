using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OVS.Manager.Accounts;
using OVS.Manager.Control;
using OVS.Manager.Data;
using OVS.Manager.Docker;
using OVS.Manager.Servers;
using OVS.Tests.Manager.Accounts;

namespace OVS.Tests.TestSupport;

/// <summary>Package M11: the manager's service sessions to in-process servers on 127.0.0.1 (<c>OVS_MANAGER_SERVER_HOST</c>).</summary>
internal static class ServerLinks
{
    /// <summary>How long a test waits for a link's state.</summary>
    public static readonly TimeSpan Within = TimeSpan.FromSeconds(15);

    /// <summary>The manager with the admin "chef" that reaches every server on 127.0.0.1.</summary>
    public static ManagerFactory Manager() =>
        new((FirstAdmin.UserVariable, "chef"), (FirstAdmin.PasswordVariable, FirstAdminTests.Password), ("OVS_MANAGER_SERVER_HOST", "127.0.0.1"));

    /// <summary>A managed server row "Raid" with the manager's identity for it, its port not yet known (-1 until <see cref="RunAtAsync"/>).</summary>
    public static async Task<AddedServer> AddAsync(ManagerFactory manager)
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
    public static async Task RunAtAsync(ManagerFactory manager, string id, int port)
    {
        await using (var scope = manager.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ManagerDbContext>().Servers.Where(row => row.Id == id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Port, port));
        manager.Docker.Containers["c-" + id] = new ManagedContainer("c-" + id, id, port, "test", "running", "image:test");
        manager.Docker.Inspected["c-" + id] = new InspectedContainer("running", 0, 0, DateTimeOffset.UtcNow);
        await manager.Services.GetRequiredService<StatusWatcher>().RefreshAsync();
    }

    /// <summary>The link of <paramref name="id"/>.</summary>
    public static ServerLink Link(ManagerFactory manager, string id) => manager.Services.GetRequiredService<ServerConnections>().LinkOf(id)!;

    /// <summary>Waits until the service session to <paramref name="id"/> is open.</summary>
    public static Task ConnectedAsync(ManagerFactory manager, string id) =>
        AsyncWait.UntilAsync(() => manager.Services.GetRequiredService<ServerConnections>().LinkOf(id)?.Status == ServerLinkStatus.Connected, Within);

    /// <summary>A started in-process server with the manager as its server admin and the manager's open service session to it.</summary>
    public static async Task<LinkedServer> StartAsync(ManagerFactory manager)
    {
        var added = await AddAsync(manager);
        var server = await TestServer.StartAsync(serverAdminFingerprint: added.AdminFingerprint);
        await RunAtAsync(manager, added.Id, server.Port);
        await ConnectedAsync(manager, added.Id);
        return new LinkedServer(added.Id, server);
    }
}
