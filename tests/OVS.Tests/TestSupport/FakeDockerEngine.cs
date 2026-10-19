using System.Collections.Concurrent;
using OVS.Manager.Docker;
using OVS.Manager.Exceptions;

namespace OVS.Tests.TestSupport;

/// <summary>Package M6 (MA2): Docker in memory for the manager's tests; only managed containers exist in it.</summary>
internal sealed class FakeDockerEngine : IDockerEngine
{
    int created;

    /// <summary>False makes every call fail as with an unreachable proxy.</summary>
    public bool Reachable { get; set; } = true;

    /// <summary>The containers by id.</summary>
    public ConcurrentDictionary<string, ManagedContainer> Containers { get; } = new();

    /// <summary>Package M8: what inspect says per container id; without an entry, its state with exit code 0, no restarts and no start.</summary>
    public ConcurrentDictionary<string, InspectedContainer> Inspected { get; } = new();

    /// <summary>Package M8: while set, start, stop and restart wait for it (an action that takes its time).</summary>
    public TaskCompletionSource? Hold { get; set; }

    /// <summary>Package M22: what each container uses; none is idle.</summary>
    public ConcurrentDictionary<string, ContainerUsage> Usage { get; } = new();

    /// <summary>Package M18: the images pulled, in order, as "image:tag".</summary>
    public ConcurrentQueue<string> Pulled { get; } = new();

    /// <summary>Package M16: the console output per container id, given out once and then followed until cancelled.</summary>
    public ConcurrentDictionary<string, string[]> ConsoleLines { get; } = new();

    /// <summary>Package M9: the servers whose volume was removed, in order.</summary>
    public ConcurrentQueue<string> RemovedVolumes { get; } = new();

    /// <summary>Every spec created, in order.</summary>
    public ConcurrentQueue<ContainerSpec> Created { get; } = new();

    public Task<bool> IsReachableAsync(CancellationToken cancellation = default) => Task.FromResult(Reachable);

    public Task<IReadOnlyList<ManagedContainer>> ListAsync(CancellationToken cancellation = default)
    {
        EnsureReachable();
        return Task.FromResult<IReadOnlyList<ManagedContainer>>([.. Containers.Values.OrderBy(container => container.Port)]);
    }

    public Task<InspectedContainer> InspectAsync(string containerId, CancellationToken cancellation = default)
    {
        EnsureReachable();
        if (!Containers.TryGetValue(containerId, out var container)) throw new ForeignContainerException(containerId);
        if (Inspected.TryGetValue(containerId, out var inspected)) return Task.FromResult(inspected);
        var environment = Created.LastOrDefault(spec => spec.ServerId == container.ServerId && spec.Version == container.Version)?.Environment;
        return Task.FromResult(new InspectedContainer(container.State, 0, 0, container.State == "running" ? DateTimeOffset.UtcNow : null, environment));
    }

    public Task<string> CreateAsync(ContainerSpec spec, CancellationToken cancellation = default)
    {
        EnsureReachable();
        var id = $"fake-{Interlocked.Increment(ref created)}";
        Created.Enqueue(spec);
        Containers[id] = new ManagedContainer(id, spec.ServerId, spec.Port, spec.Version, "created", spec.Image);
        return Task.FromResult(id);
    }

    public Task StartAsync(string containerId, CancellationToken cancellation = default) => SetStateAsync(containerId, "running");

    public Task StopAsync(string containerId, CancellationToken cancellation = default) => SetStateAsync(containerId, "exited");

    public Task RestartAsync(string containerId, CancellationToken cancellation = default) => SetStateAsync(containerId, "running");

    public Task RemoveAsync(string containerId, CancellationToken cancellation = default)
    {
        EnsureReachable();
        if (!Containers.TryRemove(containerId, out _)) throw new ForeignContainerException(containerId);
        Inspected.TryRemove(containerId, out _);
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<string> FollowLogsAsync(string containerId, int tail, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellation = default)
    {
        EnsureReachable();
        foreach (var line in ConsoleLines.GetValueOrDefault(containerId, []).TakeLast(tail)) yield return line;
        await Task.Delay(Timeout.Infinite, cancellation); // following: no more lines until the page leaves
    }

    public Task<ContainerUsage> StatsAsync(string containerId, CancellationToken cancellation = default)
    {
        EnsureReachable();
        if (!Containers.ContainsKey(containerId)) throw new ForeignContainerException(containerId);
        return Task.FromResult(Usage.GetValueOrDefault(containerId) ?? new ContainerUsage(0, 0));
    }

    public Task PullImageAsync(string image, string tag, CancellationToken cancellation = default)
    {
        EnsureReachable();
        Pulled.Enqueue($"{image}:{tag}");
        return Task.CompletedTask;
    }

    public Task RemoveVolumeAsync(string serverId, CancellationToken cancellation = default)
    {
        EnsureReachable();
        RemovedVolumes.Enqueue(serverId);
        return Task.CompletedTask;
    }

    async Task SetStateAsync(string containerId, string state)
    {
        if (Hold is { } hold) await hold.Task;
        EnsureReachable();
        if (!Containers.TryGetValue(containerId, out var container)) throw new ForeignContainerException(containerId);
        Containers[containerId] = container with { State = state };
        if (Inspected.TryGetValue(containerId, out var inspected)) Inspected[containerId] = inspected with { State = state }; // Package M18: a set state stays true
    }

    void EnsureReachable()
    {
        if (!Reachable) throw new HttpRequestException("Docker is not reachable (fake)");
    }
}
