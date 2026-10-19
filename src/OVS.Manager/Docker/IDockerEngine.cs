namespace OVS.Manager.Docker;

/// <summary>
/// Package M6 (MA2, D8): the manager's view of Docker, through the socket proxy and only on its own containers; the tests
/// use a fake. Every call for a container that is not the manager's throws <see cref="Exceptions.ForeignContainerException"/>, a
/// failed call (proxy or Docker) <see cref="Exceptions.DockerCallException"/>.
/// </summary>
internal interface IDockerEngine
{
    /// <summary>True when Docker answers (through the proxy).</summary>
    Task<bool> IsReachableAsync(CancellationToken cancellation = default);

    /// <summary>The managed containers.</summary>
    Task<IReadOnlyList<ManagedContainer>> ListAsync(CancellationToken cancellation = default);

    /// <summary>Package M8: the state, exit code, restart count and start of a managed container.</summary>
    Task<InspectedContainer> InspectAsync(string containerId, CancellationToken cancellation = default);

    /// <summary>Creates the volume and the container of <paramref name="spec"/> (not started); returns the container's id.</summary>
    Task<string> CreateAsync(ContainerSpec spec, CancellationToken cancellation = default);

    /// <summary>Starts a managed container; <see cref="Exceptions.PortInUseException"/> when its port is taken on the host.</summary>
    Task StartAsync(string containerId, CancellationToken cancellation = default);

    /// <summary>Stops a managed container (30 s for the server's own shutdown).</summary>
    Task StopAsync(string containerId, CancellationToken cancellation = default);

    /// <summary>Restarts a managed container.</summary>
    Task RestartAsync(string containerId, CancellationToken cancellation = default);

    /// <summary>Removes a managed container (its volume stays).</summary>
    Task RemoveAsync(string containerId, CancellationToken cancellation = default);

    /// <summary>Package M9: removes the volume of the server <paramref name="serverId"/> (only a manager's volume name ovs-&lt;id&gt;-data).</summary>
    Task RemoveVolumeAsync(string serverId, CancellationToken cancellation = default);
}
