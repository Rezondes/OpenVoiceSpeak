namespace OVS.Manager.Docker;

/// <summary>Package M6: what a server's container is made of (M7 fills it).</summary>
/// <param name="ServerId">The server's id, in the names and labels.</param>
/// <param name="Image">The image without its tag.</param>
/// <param name="Version">The image tag, the server's version.</param>
/// <param name="Port">The port, the same inside and outside, for TCP and UDP.</param>
/// <param name="Environment">The container's variables.</param>
/// <param name="Network">The network the container joins (ovs-managed, MA6).</param>
/// <param name="Limits">Package M25: its CPU and memory limits, null for none.</param>
/// <param name="DataSource">Package M29: the volume or host folder mounted as /data as it is; null for the manager's own volume ovs-&lt;id&gt;-data.</param>
internal sealed record ContainerSpec(string ServerId, string Image, string Version, int Port, IReadOnlyDictionary<string, string> Environment, string Network,
    ResourceLimits? Limits = null, string? DataSource = null);
