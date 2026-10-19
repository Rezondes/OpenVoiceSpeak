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
    readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(60) };
    KeptSizes? volumeSizes;

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
        var environment = inspected.Config?.Env?.Select(variable => variable.Split('=', 2)).Where(pair => pair.Length == 2).ToDictionary(pair => pair[0], pair => pair[1]);
        return new InspectedContainer(inspected.State.Status, (int)inspected.State.ExitCode, (int)inspected.RestartCount, started, environment);
    });

    /// <inheritdoc/>
    public Task<string> CreateAsync(ContainerSpec spec, CancellationToken cancellation = default) => CallAsync(async () =>
    {
        var labels = ManagedLabels.For(spec);
        var volume = spec.DataSource ?? ManagedLabels.VolumeName(spec.ServerId);
        if (spec.DataSource is null) await client.Volumes.CreateAsync(new VolumesCreateParameters { Name = volume, Labels = labels }, cancellation);
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
                NanoCPUs = spec.Limits?.CpuCores is { } cores ? (long)(cores * 1_000_000_000) : 0, // Package M25: 0 is no limit
                Memory = spec.Limits?.MemoryMb is { } megabytes ? megabytes * 1024L * 1024 : 0,
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
    public Task RemoveVolumeAsync(string volume, CancellationToken cancellation = default) => CallAsync(async () =>
    {
        await client.Volumes.RemoveAsync(volume, force: false, cancellation);
        return true;
    });

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> FollowLogsAsync(string containerId, int tail, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellation = default)
    {
        await EnsureManagedAsync(containerId, cancellation);
        using var stream = await CallAsync(() => client.Containers.GetContainerLogsAsync(containerId, false, new ContainerLogsParameters
        {
            ShowStdout = true, ShowStderr = true, Follow = true, Tail = tail.ToString(CultureInfo.InvariantCulture),
        }, cancellation));
        var buffer = new byte[8192];
        var pending = new System.Text.StringBuilder();
        while (!cancellation.IsCancellationRequested)
        {
            var read = await stream.ReadOutputAsync(buffer, 0, buffer.Length, cancellation);
            if (read.EOF) break;
            pending.Append(System.Text.Encoding.UTF8.GetString(buffer, 0, read.Count));
            for (int end = pending.ToString().IndexOf('\n', StringComparison.Ordinal); end >= 0; end = pending.ToString().IndexOf('\n', StringComparison.Ordinal))
            {
                yield return pending.ToString(0, end).TrimEnd('\r');
                pending.Remove(0, end + 1);
            }
        }
    }

    /// <inheritdoc/>
    public Task PullImageAsync(string image, string tag, CancellationToken cancellation = default) => CallAsync(async () =>
    {
        await client.Images.CreateImageAsync(new ImagesCreateParameters { FromImage = image, Tag = tag }, null, new Progress<JSONMessage>(), cancellation);
        return true;
    });

    /// <inheritdoc/>
    public async Task<ContainerUsage> StatsAsync(string containerId, CancellationToken cancellation = default)
    {
        await EnsureManagedAsync(containerId, cancellation);
        var last = new LastStats();
        await CallAsync(async () =>
        {
            await client.Containers.GetContainerStatsAsync(containerId, new ContainerStatsParameters { Stream = false }, last, cancellation);
            return true;
        });
        return last.Value is { } stats ? Usage(stats) : new ContainerUsage(0, 0);
    }

    /// <summary>
    /// Package M25: <c>system/df</c> sizes every volume and takes its time, so its answer is kept for 10 minutes. Docker.DotNet
    /// has no call for it; the request goes to the same host. ponytail: only a tcp or http host, a unix socket gives no sizes.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, long>> VolumeSizesAsync(CancellationToken cancellation = default)
    {
        if (volumeSizes is { } kept && DateTimeOffset.UtcNow - kept.At < TimeSpan.FromMinutes(10)) return kept.Sizes; // clock: the age of a cache
        var host = new Uri(options.DockerHost);
        if (host.Scheme is not ("tcp" or "http")) return new Dictionary<string, long>();
        var json = await CallAsync(() => http.GetStringAsync(new UriBuilder("http", host.Host, host.Port, "/system/df", "?type=volume").Uri, cancellation));
        var sizes = VolumeUsage.Parse(json);
        volumeSizes = new KeptSizes(DateTimeOffset.UtcNow, sizes); // clock: the age of a cache
        return sizes;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ForeignServer>> ListForeignServersAsync(CancellationToken cancellation = default) => CallAsync(async () =>
    {
        var listed = await client.Containers.ListContainersAsync(new ContainersListParameters { All = true }, cancellation);
        var found = new List<ForeignServer>();
        foreach (var container in listed.Where(container => !ManagedLabels.IsManaged(container.Labels) && IsServerImage(container.Image, container.Labels)))
        {
            var inspected = await client.Containers.InspectContainerAsync(container.ID, cancellation);
            var data = inspected.Mounts?.FirstOrDefault(mount => mount.Destination == "/data");
            var environment = inspected.Config?.Env?.Select(variable => variable.Split('=', 2)).Where(pair => pair.Length == 2).ToDictionary(pair => pair[0], pair => pair[1]) ?? [];
            found.Add(new ForeignServer(container.ID, inspected.Name.TrimStart('/'), container.Image, container.State,
                data is null ? null : data.Type == "volume" ? data.Name : data.Source, environment));
        }
        return (IReadOnlyList<ForeignServer>)found;
    });

    /// <inheritdoc/>
    public async Task StopForeignAsync(string containerId, CancellationToken cancellation = default)
    {
        await EnsureForeignServerAsync(containerId, cancellation);
        await CallAsync(() => client.Containers.StopContainerAsync(containerId, new ContainerStopParameters { WaitBeforeKillSeconds = StopSeconds }, cancellation));
    }

    /// <inheritdoc/>
    public async Task StartForeignAsync(string containerId, CancellationToken cancellation = default)
    {
        await EnsureForeignServerAsync(containerId, cancellation);
        await CallAsync(() => client.Containers.StartContainerAsync(containerId, new ContainerStartParameters(), cancellation));
    }

    /// <inheritdoc/>
    public async Task RemoveForeignAsync(string containerId, CancellationToken cancellation = default)
    {
        await EnsureForeignServerAsync(containerId, cancellation);
        await CallAsync(async () =>
        {
            await client.Containers.RemoveContainerAsync(containerId, new ContainerRemoveParameters { Force = true }, cancellation);
            return true;
        });
    }

    /// <summary>Package M29: only an unlabelled container of the OVS server image may be touched as foreign.</summary>
    async Task EnsureForeignServerAsync(string containerId, CancellationToken cancellation)
    {
        if (!(await ListForeignServersAsync(cancellation)).Any(container => container.Id == containerId))
            throw new ForeignContainerException($"Container {containerId} is no foreign OVS server.");
    }

    /// <summary>The server image carries the label of its protocol (PLAN 193), whatever it is named, such as a self-built <c>openvoicespeak/server:local</c>.</summary>
    bool IsServerImage(string image, IDictionary<string, string>? labels) =>
        labels?.ContainsKey("org.openvoicespeak.protocol") == true
        || image.StartsWith(options.ServerImage, StringComparison.Ordinal) || image.Contains("openvoicespeak-server", StringComparison.Ordinal);

    /// <summary>Ends the client.</summary>
    public void Dispose()
    {
        client.Dispose();
        http.Dispose();
    }

    /// <summary>CPU as Docker's own <c>docker stats</c> counts it (the change against the previous sample), memory without the page cache.</summary>
    static ContainerUsage Usage(ContainerStatsResponse stats)
    {
        double cpu = (double)(stats.CPUStats?.CPUUsage?.TotalUsage ?? 0) - (stats.PreCPUStats?.CPUUsage?.TotalUsage ?? 0);
        double system = (double)(stats.CPUStats?.SystemUsage ?? 0) - (stats.PreCPUStats?.SystemUsage ?? 0);
        double cores = stats.CPUStats?.OnlineCPUs is > 0 and var online ? online : stats.CPUStats?.CPUUsage?.PercpuUsage?.Count ?? 1;
        var memory = stats.MemoryStats?.Usage ?? 0;
        var cache = stats.MemoryStats?.Stats is { } values ? values.TryGetValue("inactive_file", out var inactive) ? inactive : values.TryGetValue("cache", out var cached) ? cached : 0 : 0;
        return new ContainerUsage(cpu > 0 && system > 0 ? cpu / system * cores * 100 : 0, (long)(memory - Math.Min(cache, memory)));
    }

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

    /// <summary>The volume sizes with the time they were read.</summary>
    sealed record KeptSizes(DateTimeOffset At, IReadOnlyDictionary<string, long> Sizes);

    /// <summary>Keeps the one sample a stats call without streaming reports.</summary>
    sealed class LastStats : IProgress<ContainerStatsResponse>
    {
        public ContainerStatsResponse? Value { get; private set; }

        public void Report(ContainerStatsResponse value) => Value = value;
    }
}
