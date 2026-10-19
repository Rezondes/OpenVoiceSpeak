namespace OVS.Manager.Servers;

/// <summary>
/// Package M25 (AC3): the disk of the manager's data folder. In the compose setup it is a named volume, which Docker keeps
/// in its data root, so its disk is the data root's. ponytail: a bind mount elsewhere shows that disk instead.
/// </summary>
internal sealed class DataDirDisk(ManagerOptions options) : IHostDisk
{
    /// <inheritdoc/>
    public DiskSpace? Read()
    {
        try
        {
            var drive = new DriveInfo(Path.GetFullPath(options.DataDir));
            return new DiskSpace(drive.AvailableFreeSpace, drive.TotalSize);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
