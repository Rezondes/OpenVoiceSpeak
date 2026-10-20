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

    /// <summary>Package M16: the last <paramref name="tail"/> lines of a managed container's output and then every new one, until <paramref name="cancellation"/>.</summary>
    IAsyncEnumerable<string> FollowLogsAsync(string containerId, int tail, CancellationToken cancellation = default);

    /// <summary>Package M29 (AC1): the containers of an OVS server image without the manager's label.</summary>
    Task<IReadOnlyList<ForeignServer>> ListForeignServersAsync(CancellationToken cancellation = default);

    /// <summary>Package M29 (AC2): stops a foreign OVS server's container (nothing else that is foreign).</summary>
    Task StopForeignAsync(string containerId, CancellationToken cancellation = default);

    /// <summary>Package M29 (AC2): starts a foreign OVS server's container again after a failed adoption.</summary>
    Task StartForeignAsync(string containerId, CancellationToken cancellation = default);

    /// <summary>Package M29 (AC2): removes a foreign OVS server's container after its adoption; its data stays.</summary>
    Task RemoveForeignAsync(string containerId, CancellationToken cancellation = default);

    /// <summary>Package M25: each volume's size in bytes by its name (the proxy allows <c>system/df</c>); empty when Docker cannot tell.</summary>
    Task<IReadOnlyDictionary<string, long>> VolumeSizesAsync(CancellationToken cancellation = default);

    /// <summary>Package M22: the CPU and memory a managed container uses now (the proxy allows <c>containers/{id}/stats</c>).</summary>
    Task<ContainerUsage> StatsAsync(string containerId, CancellationToken cancellation = default);

    /// <summary>The tag of the newest image of <paramref name="image"/> on the host (by creation), null for none (the proxy allows <c>images/json</c>).</summary>
    Task<string?> NewestTagAsync(string image, CancellationToken cancellation = default);

    /// <summary>Whether <paramref name="image"/>:<paramref name="tag"/> is on the host (the proxy allows <c>images/.../json</c>).</summary>
    Task<bool> ImageExistsAsync(string image, string tag, CancellationToken cancellation = default);

    /// <summary>Package M18: pulls <paramref name="image"/>:<paramref name="tag"/> (the proxy allows <c>images/create</c>).</summary>
    Task PullImageAsync(string image, string tag, CancellationToken cancellation = default);

    /// <summary>Package M9, M29: removes the volume <paramref name="volume"/> of a server (<see cref="Servers.ManagedServer.Volume"/>).</summary>
    Task RemoveVolumeAsync(string volume, CancellationToken cancellation = default);
}
