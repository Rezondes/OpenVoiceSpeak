using OVS.Manager.Docker;

namespace OVS.Manager.Telemetry;

/// <summary>Package M25 (AC1): a server's CPU and RAM now and over 24 hours, and its volume's size.</summary>
/// <param name="Usage">CPU and RAM now, null when the container does not run or Docker did not tell.</param>
/// <param name="VolumeBytes">The volume's size, null when Docker did not tell.</param>
/// <param name="Cpu">The highest CPU percent of each hour.</param>
/// <param name="Memory">The highest MB of each hour.</param>
/// <param name="FromUnixMs">24 hours ago.</param>
/// <param name="ToUnixMs">Now.</param>
internal sealed record ResourceSnapshot(ContainerUsage? Usage, long? VolumeBytes, IReadOnlyList<ChartPoint> Cpu, IReadOnlyList<ChartPoint> Memory, long FromUnixMs, long ToUnixMs);
