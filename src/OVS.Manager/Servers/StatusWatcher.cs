using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using OVS.Manager.Data;
using OVS.Manager.Docker;
using OVS.Manager.Exceptions;

namespace OVS.Manager.Servers;

/// <summary>
/// Package M8 (AC1, AC3 to AC5): the status of every server, checked every 5 seconds. The socket proxy passes no Docker
/// events, so the watcher lists and inspects the managed containers and finds each server's by its label, also after the
/// manager's own restart. A running container counts as running only when the server finishes a TLS handshake.
/// </summary>
internal sealed class StatusWatcher(IDockerEngine docker, IServerProbe probe, IServiceScopeFactory scopes, TimeProvider time) : BackgroundService
{
    /// <summary>How often the status is checked.</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(5);

    /// <summary>How long a started container may go without answering before it is "not reachable".</summary>
    public static readonly TimeSpan StartGrace = TimeSpan.FromSeconds(30);

    /// <summary>AC3: the window in which more than <see cref="LoopRestarts"/> restarts are a restart loop.</summary>
    public static readonly TimeSpan LoopWindow = TimeSpan.FromMinutes(10);

    /// <summary>AC3: more restarts than this within <see cref="LoopWindow"/> are a restart loop.</summary>
    public const int LoopRestarts = 3;

    readonly Lock gate = new();
    readonly Dictionary<string, RestartHistory> restarts = [];
    readonly ConcurrentDictionary<string, byte> busy = new();
    IReadOnlyList<ServerSnapshot> servers = [];

    /// <summary>Raised after every check and when an action on a server begins or ends; on any thread.</summary>
    public event Action? Changed;

    /// <summary>Every server at the last check, by name.</summary>
    public IReadOnlyList<ServerSnapshot> Servers => Volatile.Read(ref servers);

    /// <summary>AC2: an action runs on <paramref name="serverId"/>, so its buttons are off.</summary>
    public bool IsBusy(string serverId) => busy.ContainsKey(serverId);

    /// <summary>AC2: marks an action on <paramref name="serverId"/>; false when one already runs.</summary>
    public bool TryBeginAction(string serverId)
    {
        if (!busy.TryAdd(serverId, 0)) return false;
        Changed?.Invoke();
        return true;
    }

    /// <summary>AC2: the action on <paramref name="serverId"/> is over.</summary>
    public void EndAction(string serverId)
    {
        busy.TryRemove(serverId, out _);
        Changed?.Invoke();
    }

    /// <summary>Checks every server now.</summary>
    /// <exception cref="DockerCallException">Docker is not reachable.</exception>
    public async Task RefreshAsync(CancellationToken cancellation = default)
    {
        List<ManagedServer> rows;
        await using (var scope = scopes.CreateAsyncScope())
            rows = await scope.ServiceProvider.GetRequiredService<ManagerDbContext>().Servers.AsNoTracking().OrderBy(server => server.Name).ToListAsync(cancellation);
        var containers = (await docker.ListAsync(cancellation)).DistinctBy(container => container.ServerId).ToDictionary(container => container.ServerId);
        ServerSnapshot[] checkedServers = await Task.WhenAll(rows.Select(row => CheckAsync(row, containers.GetValueOrDefault(row.Id), cancellation)));
        Volatile.Write(ref servers, checkedServers);
        Changed?.Invoke();
    }

    /// <summary>Checks until the manager stops; a failed check keeps the last status (the start page says Docker is not reachable).</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield(); // the first check must not hold up the host's start
        using var timer = new PeriodicTimer(CheckInterval, time);
        do
        {
            try
            {
                await RefreshAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is DockerCallException or ForeignContainerException)
            {
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    async Task<ServerSnapshot> CheckAsync(ManagedServer row, ManagedContainer? container, CancellationToken cancellation)
    {
        if (container is null) return new ServerSnapshot(row.Id, row.Name, row.Port, row.Version, ServerStatus.NotReachable, null, null);
        var inspected = await docker.InspectAsync(container.Id, cancellation);
        var now = time.GetUtcNow();
        bool loop = CountRestarts(row.Id, inspected.RestartCount, now) > LoopRestarts;
        var status = loop ? ServerStatus.RestartLoop : inspected.State switch
        {
            "running" when await probe.AnswersAsync(ManagedLabels.ContainerName(row.Id), row.Port, cancellation) => ServerStatus.Running,
            "running" => now - inspected.StartedAtUtc < StartGrace ? ServerStatus.Starting : ServerStatus.NotReachable,
            "restarting" => ServerStatus.Starting,
            "exited" or "dead" when inspected.ExitCode != 0 => ServerStatus.Crashed,
            "exited" or "dead" or "created" or "paused" => ServerStatus.Stopped,
            _ => ServerStatus.NotReachable,
        };
        int? exitCode = status is ServerStatus.Crashed or ServerStatus.RestartLoop ? inspected.ExitCode : null;
        var startedAt = inspected.State == "running" ? inspected.StartedAtUtc : null;
        return new ServerSnapshot(row.Id, row.Name, row.Port, container.Version, status, exitCode, startedAt);
    }

    /// <summary>AC3: the restarts Docker made within <see cref="LoopWindow"/>, counted from its restart count since the manager started.</summary>
    int CountRestarts(string serverId, int restartCount, DateTimeOffset now)
    {
        lock (gate)
        {
            if (!restarts.TryGetValue(serverId, out var history)) restarts[serverId] = history = new RestartHistory { Count = restartCount };
            for (int restart = history.Count; restart < restartCount; restart++) history.Times.Enqueue(now);
            history.Count = restartCount; // Docker starts counting again when the container is started by hand
            while (history.Times.TryPeek(out var at) && now - at > LoopWindow) history.Times.Dequeue();
            return history.Times.Count;
        }
    }

    /// <summary>The restart count last seen and when each new restart was seen.</summary>
    sealed class RestartHistory
    {
        public int Count { get; set; }
        public Queue<DateTimeOffset> Times { get; } = new();
    }
}
