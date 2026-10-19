using System.Globalization;
using Docker.DotNet;
using Docker.DotNet.Models;
using OVS.Manager.Exceptions;

namespace OVS.Manager.Docker;

/// <summary>
/// Package M6 (D8): <see cref="IDockerEngine"/> with Docker.DotNet over the socket proxy (<c>OVS_MANAGER_DOCKER_HOST</c>).
/// Docker lists by the label <c>ovs.managed=true</c> and the adapter checks it again; a call for any other container
/// is refused before it is sent. Package M7: a failed call becomes a <see cref="DockerCallException"/>.
/// </summary>
internal sealed class DockerEngine(ManagerOptions options) : IDockerEngine, IDisposable
{
    static readonly TimeSpan PingTimeout = TimeSpan.FromSeconds(2);
    const int StopSeconds = 30;

    readonly DockerClient client = new DockerClientConfiguration(new Uri(options.DockerHost)).CreateClient();

    /// <inheritdoc/>
    public async Task<bool> IsReachableAsync(CancellationToken cancellation = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(PingTimeout); // clock: real time, a socket call
        try
        {
            await client.System.PingAsync(timeout.Token);
            return true;
        }
        catch (Exception exception) when (IsDockerFailure(exception) || exception is OperationCanceledException)
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ManagedContainer>> ListAsync(CancellationToken cancellation = default) => CallAsync(async () =>
    {
        var listed = await client.Containers.ListContainersAsync(new ContainersListParameters
        {
            All = true,
            Filters = new Dictionary<string, IDictionary<string, bool>> { ["label"] = new Dictionary<string, bool> { [$"{ManagedLabels.Managed}=true"] = true } },
        }, cancellation);
        return (IReadOnlyList<ManagedContainer>)[.. listed.Where(container => ManagedLabels.IsManaged(container.Labels)).Select(container => new ManagedContainer(
            container.ID, Label(container.Labels, ManagedLabels.ServerId),
            int.TryParse(Label(container.Labels, ManagedLabels.Port), CultureInfo.InvariantCulture, out var port) ? port : 0,
            Label(container.Labels, ManagedLabels.Version), container.State, container.Image))];
    });

    /// <inheritdoc/>
    public Task<InspectedContainer> InspectAsync(string containerId, CancellationToken cancellation = default) => CallAsync(async () =>
    {
        var inspected = await client.Containers.InspectContainerAsync(containerId, cancellation);
        if (!ManagedLabels.IsManaged(inspected.Config?.Labels))
            throw new ForeignContainerException($"Container {containerId} is not managed by the OVS manager.");
        var started = DateTimeOffset.TryParse(inspected.State.StartedAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) && at.Year > 1 ? at : (DateTimeOffset?)null;
        return new InspectedContainer(inspected.State.Status, (int)inspected.State.ExitCode, (int)inspected.RestartCount, started);
    });

    /// <inheritdoc/>
    public Task<string> CreateAsync(ContainerSpec spec, CancellationToken cancellation = default) => CallAsync(async () =>
    {
        var labels = ManagedLabels.For(spec);
        var volume = ManagedLabels.VolumeName(spec.ServerId);
        await client.Volumes.CreateAsync(new VolumesCreateParameters { Name = volume, Labels = labels }, cancellation);
        var port = spec.Port.ToString(CultureInfo.InvariantCulture);
        var bindings = new[] { $"{port}/tcp", $"{port}/udp" };
        var created = await client.Containers.CreateContainerAsync(new CreateContainerParameters
        {
            Name = ManagedLabels.ContainerName(spec.ServerId),
            Image = $"{spec.Image}:{spec.Version}",
            Env = [.. spec.Environment.Select(variable => $"{variable.Key}={variable.Value}")],
            Labels = labels,
            ExposedPorts = bindings.ToDictionary(binding => binding, _ => default(EmptyStruct)),
            HostConfig = new HostConfig
            {
                PortBindings = bindings.ToDictionary(binding => binding, _ => (IList<PortBinding>)[new PortBinding { HostPort = port }]),
                Binds = [$"{volume}:/data"],
                RestartPolicy = new RestartPolicy { Name = RestartPolicyKind.UnlessStopped },
                NetworkMode = spec.Network,
            },
        }, cancellation);
        return created.ID;
    });

    /// <inheritdoc/>
    public async Task StartAsync(string containerId, CancellationToken cancellation = default)
    {
        await EnsureManagedAsync(containerId, cancellation);
        try
        {
            await CallAsync(() => client.Containers.StartContainerAsync(containerId, new ContainerStartParameters(), cancellation));
        }
        catch (DockerCallException exception) when (exception.InnerException is DockerApiException api
                                                    && (api.ResponseBody.Contains("port is already allocated", StringComparison.OrdinalIgnoreCase)
                                                        || api.ResponseBody.Contains("address already in use", StringComparison.OrdinalIgnoreCase)))
        {
            throw new PortInUseException($"Container {containerId}: {api.ResponseBody}", api);
        }
    }

    /// <inheritdoc/>
    public async Task StopAsync(string containerId, CancellationToken cancellation = default)
    {
        await EnsureManagedAsync(containerId, cancellation);
        await CallAsync(() => client.Containers.StopContainerAsync(containerId, new ContainerStopParameters { WaitBeforeKillSeconds = StopSeconds }, cancellation));
    }

    /// <inheritdoc/>
    public async Task RestartAsync(string containerId, CancellationToken cancellation = default)
    {
        await EnsureManagedAsync(containerId, cancellation);
        await CallAsync(async () =>
        {
            await client.Containers.RestartContainerAsync(containerId, new ContainerRestartParameters { WaitBeforeKillSeconds = StopSeconds }, cancellation);
            return true;
        });
    }

    /// <inheritdoc/>
    public async Task RemoveAsync(string containerId, CancellationToken cancellation = default)
    {
        await EnsureManagedAsync(containerId, cancellation);
        await CallAsync(async () =>
        {
            await client.Containers.RemoveContainerAsync(containerId, new ContainerRemoveParameters { Force = true }, cancellation);
            return true;
        });
    }

    /// <inheritdoc/>
    public Task RemoveVolumeAsync(string serverId, CancellationToken cancellation = default) => CallAsync(async () =>
    {
        await client.Volumes.RemoveAsync(ManagedLabels.VolumeName(serverId), force: false, cancellation);
        return true;
    });

    /// <summary>Ends the client.</summary>
    public void Dispose() => client.Dispose();

    /// <summary>AC2: the id must be one Docker lists as managed; otherwise nothing with it is sent.</summary>
    async Task EnsureManagedAsync(string containerId, CancellationToken cancellation)
    {
        if (!(await ListAsync(cancellation)).Any(container => container.Id == containerId))
            throw new ForeignContainerException($"Container {containerId} is not managed by the OVS manager.");
    }

    /// <summary>Package M7: a call whose failure is the proxy's or Docker's becomes a <see cref="DockerCallException"/>.</summary>
    static async Task<T> CallAsync<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (Exception exception) when (IsDockerFailure(exception))
        {
            throw new DockerCallException($"Docker call failed: {exception.Message}", exception);
        }
    }

    static async Task CallAsync(Func<Task<bool>> call) => await CallAsync<bool>(call);

    static bool IsDockerFailure(Exception exception) => exception is DockerApiException or HttpRequestException or IOException or TimeoutException;

    static string Label(IDictionary<string, string> labels, string key) => labels.TryGetValue(key, out var value) ? value : "";
}
