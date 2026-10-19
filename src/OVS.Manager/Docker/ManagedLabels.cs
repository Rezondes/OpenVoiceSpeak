namespace OVS.Manager.Docker;

/// <summary>
/// Package M6 (AC3): the labels that mark a container as the manager's and name its server, and the names of its
/// container and volume. The socket proxy cannot filter by label, so the manager checks them itself (D8).
/// </summary>
internal static class ManagedLabels
{
    /// <summary>"true" on every container the manager created.</summary>
    public const string Managed = "ovs.managed";
    /// <summary>The server's id.</summary>
    public const string ServerId = "ovs.server-id";
    /// <summary>The server's port (TCP and UDP).</summary>
    public const string Port = "ovs.port";
    /// <summary>The server's version (the image tag).</summary>
    public const string Version = "ovs.version";

    /// <summary>The labels of the container of <paramref name="spec"/>.</summary>
    public static Dictionary<string, string> For(ContainerSpec spec) => new()
    {
        [Managed] = "true",
        [ServerId] = spec.ServerId,
        [Port] = spec.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
        [Version] = spec.Version,
    };

    /// <summary>True for the labels of a managed container.</summary>
    public static bool IsManaged(IDictionary<string, string>? labels) => labels is not null && labels.TryGetValue(Managed, out var value) && value == "true";

    /// <summary>The container's name, which is also its host name in the network ovs-managed (MA6).</summary>
    public static string ContainerName(string serverId) => $"ovs-{serverId}";

    /// <summary>The volume of the server's /data.</summary>
    public static string VolumeName(string serverId) => $"ovs-{serverId}-data";
}
