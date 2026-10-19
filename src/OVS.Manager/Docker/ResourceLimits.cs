namespace OVS.Manager.Docker;

/// <summary>Package M25 (AC2): a container's CPU and memory limits; null is no limit.</summary>
/// <param name="CpuCores">CPU cores, such as 1.5 (Docker's NanoCpus).</param>
/// <param name="MemoryMb">Memory in MB (Docker's Memory).</param>
internal sealed record ResourceLimits(double? CpuCores, int? MemoryMb)
{
    /// <summary>The smallest and largest CPU limit taken.</summary>
    public const double MinCpuCores = 0.1, MaxCpuCores = 64;

    /// <summary>The smallest and largest memory limit taken.</summary>
    public const int MinMemoryMb = 64, MaxMemoryMb = 262_144;

    /// <summary>No limits.</summary>
    public static readonly ResourceLimits None = new(null, null);

    /// <summary>Both limits are unset or within their range.</summary>
    public bool IsValid => CpuCores is null or (>= MinCpuCores and <= MaxCpuCores) && MemoryMb is null or (>= MinMemoryMb and <= MaxMemoryMb);
}
