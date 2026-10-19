using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OVS.Manager.Audit;
using OVS.Manager.Data;
using OVS.Manager.Servers;

namespace OVS.Manager.Control;

/// <summary>
/// Package M11 (AC1, AC3): one <see cref="ServerLink"/> for every server whose container runs, opened when the watcher
/// sees it run and ended when it stops or goes to the trash.
/// </summary>
internal sealed class ServerConnections(StatusWatcher watcher, IServiceScopeFactory scopes, ManagerOptions options, TimeProvider time, ILogger<ServerLink> logger)
    : IHostedService, IAsyncDisposable
{
    readonly ConcurrentDictionary<string, ServerLink> links = new();
    readonly Lock gate = new();
    bool stopped;

    /// <summary>Raised when a link's status or mirror changed; on any thread.</summary>
    public event Action? Changed;

    /// <summary>The link of <paramref name="serverId"/>, null while its container does not run.</summary>
    public ServerLink? LinkOf(string serverId) => links.GetValueOrDefault(serverId);

    /// <summary>Follows the watcher.</summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        watcher.Changed += Sync;
        Sync();
        return Task.CompletedTask;
    }

    /// <summary>Ends every link.</summary>
    public async Task StopAsync(CancellationToken cancellationToken) => await DisposeAsync();

    /// <summary>AC3: pins the certificate the server now shows, writes the audit line and connects again.</summary>
    public async Task AcceptCertificateAsync(string serverId, string actor, CancellationToken cancellation = default)
    {
        if (LinkOf(serverId) is not { OfferedCertificate: { } offered } link) return;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ManagerDbContext>();
            var server = await database.Servers.SingleAsync(row => row.Id == serverId, cancellation);
            server.CertificateFingerprint = offered;
            await database.SaveChangesAsync(cancellation);
            await scope.ServiceProvider.GetRequiredService<AuditLog>().WriteAsync(actor, AuditActions.CertificateAccepted, server.Name, server.Id, offered, cancellation);
        }
        if (links.TryRemove(new KeyValuePair<string, ServerLink>(serverId, link))) await EndAsync(link);
        Sync();
    }

    /// <summary>Ends every link and stops following the watcher.</summary>
    public async ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (stopped) return;
            stopped = true;
        }
        watcher.Changed -= Sync;
        foreach (var serverId in links.Keys)
            if (links.TryRemove(serverId, out var link)) await EndAsync(link);
    }

    /// <summary>Opens a link for each running container and ends the others.</summary>
    void Sync()
    {
        var running = watcher.Servers.Where(server => server.StartedAtUtc is not null).Select(server => server.Id).ToHashSet();
        lock (gate)
        {
            if (stopped) return;
            foreach (var serverId in running.Where(serverId => !links.ContainsKey(serverId)))
            {
                var link = new ServerLink(serverId, scopes, options, time, logger);
                link.Changed += OnLinkChanged;
                links[serverId] = link;
                link.Start();
            }
        }
        foreach (var serverId in links.Keys.Where(serverId => !running.Contains(serverId)))
            if (links.TryRemove(serverId, out var link)) _ = EndAsync(link);
    }

    async Task EndAsync(ServerLink link)
    {
        link.Changed -= OnLinkChanged;
        await link.DisposeAsync();
        Changed?.Invoke();
    }

    void OnLinkChanged() => Changed?.Invoke();
}
