using OVS.Manager.Servers;

namespace OVS.Tests.TestSupport;

/// <summary>Package M25: the data root's disk as a test sets it.</summary>
internal sealed class FakeHostDisk : IHostDisk
{
    /// <summary>What <see cref="Read"/> answers.</summary>
    public DiskSpace? Space { get; set; } = new(250_000_000_000, 500_000_000_000);

    public DiskSpace? Read() => Space;
}
