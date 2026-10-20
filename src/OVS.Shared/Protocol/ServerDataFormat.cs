namespace OVS.Shared.Protocol;

/// <summary>
/// The versions of a server's data and of its backup archive. The server writes and reads them; the manager checks a backup
/// against them before it makes a server from it (the two ship in the same release).
/// </summary>
public static class ServerDataFormat
{
    /// <summary>The version of server-data.json this release writes; older files are migrated on load, newer ones refused.</summary>
    public const int Version = 8;

    /// <summary>The backup archive format this release writes and reads (Package 74).</summary>
    public const int BackupFormat = 1;
}
