namespace OVS.Manager.Servers;

/// <summary>Package M7 (AC1): what the admin enters for a new server; null takes the default.</summary>
/// <param name="Name">The server's name.</param>
/// <param name="MaxUsers">The start value of the user limit.</param>
/// <param name="Password">The start value of the server password, empty for none.</param>
/// <param name="Version">The version; null takes the manager's own (MA5).</param>
/// <param name="TimeZone">The time zone; null takes the manager's TZ (D14).</param>
/// <param name="Port">The port; null takes the lowest free one.</param>
/// <param name="Limits">Package M25: CPU and memory limits, null for none.</param>
/// <param name="Image">The image without its tag, null for the manager's (a clone takes its source's).</param>
internal sealed record CreateServerRequest(string Name, int MaxUsers, string? Password, string? Version, string? TimeZone, int? Port, Docker.ResourceLimits? Limits = null,
    string? Image = null);
