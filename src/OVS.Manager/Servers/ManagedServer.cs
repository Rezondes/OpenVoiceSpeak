namespace OVS.Manager.Servers;

/// <summary>Package M7: a server the manager created; its container carries its id (label ovs.server-id).</summary>
internal sealed class ManagedServer
{
    /// <summary>A short id, also in the container's name ovs-&lt;id&gt; and the volume's.</summary>
    public string Id { get; set; } = "";

    /// <summary>The name shown in the manager (the server's own name starts as the same).</summary>
    public string Name { get; set; } = "";

    /// <summary>The port for TCP and UDP, 7000 to 7099 (D2).</summary>
    public int Port { get; set; }

    /// <summary>The image tag the server runs.</summary>
    public string Version { get; set; } = "";

    /// <summary>The server's time zone (TZ).</summary>
    public string TimeZone { get; set; } = "";

    /// <summary>The start value of the user limit.</summary>
    public int MaxUsers { get; set; }

    /// <summary>When it was created, in UTC.</summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Package M9 (MA9): when the admin deleted it, in UTC; null while it is not in the trash.</summary>
    public DateTime? DeletedAtUtc { get; set; }

    /// <summary>Docker's id of its container, null until it is created.</summary>
    public string? ContainerId { get; set; }

    /// <summary>Package M11 (AC3): the server certificate's fingerprint pinned at the first connect; null before it.</summary>
    public string? CertificateFingerprint { get; set; }

    /// <summary>The fingerprint of the manager's identity on this server, its server admin (D3, MA4).</summary>
    public string AdminFingerprint { get; set; } = "";
}
