namespace OVS.Manager.Servers;

/// <summary>Package M25 (AC3): the disk of Docker's data root, as the manager sees it.</summary>
internal interface IHostDisk
{
    /// <summary>Its free and whole space now, null when it cannot be read.</summary>
    DiskSpace? Read();
}
