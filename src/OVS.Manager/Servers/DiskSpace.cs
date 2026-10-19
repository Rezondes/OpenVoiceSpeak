namespace OVS.Manager.Servers;

/// <summary>Package M25 (AC3): the free and the whole space of a disk.</summary>
/// <param name="FreeBytes">What is free.</param>
/// <param name="TotalBytes">The disk's size.</param>
internal sealed record DiskSpace(long FreeBytes, long TotalBytes)
{
    /// <summary>Below this share free, the overview warns.</summary>
    public const double WarnBelow = 0.10;

    /// <summary>The free share, 0 to 1.</summary>
    public double FreeShare => TotalBytes > 0 ? (double)FreeBytes / TotalBytes : 0;

    /// <summary>Less than <see cref="WarnBelow"/> is free.</summary>
    public bool IsLow => FreeShare < WarnBelow;
}
