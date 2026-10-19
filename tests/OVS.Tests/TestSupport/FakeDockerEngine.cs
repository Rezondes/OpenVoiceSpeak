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
        return Task.FromResult(Inspected.TryGetValue(containerId, out var inspected) ? inspected : new InspectedContainer(container.State, 0, 0, null));
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
        return Task.CompletedTask;
    }

    async Task SetStateAsync(string containerId, string state)
    {
        if (Hold is { } hold) await hold.Task;
        EnsureReachable();
        if (!Containers.TryGetValue(containerId, out var container)) throw new ForeignContainerException(containerId);
        Containers[containerId] = container with { State = state };
    }

    void EnsureReachable()
    {
        if (!Reachable) throw new HttpRequestException("Docker is not reachable (fake)");
    }
}
