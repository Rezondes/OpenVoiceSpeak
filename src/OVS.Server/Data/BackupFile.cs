namespace OVS.Server.Data;

/// <summary>A free backup file name and its full path in the backup folder.</summary>
/// <param name="Name">The file name: prefix, local time, and _2, _3 ... when that second is taken.</param>
/// <param name="Path">The full path in the backup folder.</param>
internal sealed record BackupFile(string Name, string Path);
