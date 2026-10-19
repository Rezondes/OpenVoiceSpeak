namespace OVS.Manager.Docker;

/// <summary>Package M22: what a container uses right now, from Docker's stats.</summary>
/// <param name="CpuPercent">CPU in percent of one core (200 is two cores fully).</param>
/// <param name="MemoryBytes">Memory in use without the page cache.</param>
internal sealed record ContainerUsage(double CpuPercent, long MemoryBytes);
