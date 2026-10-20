namespace OVS.Manager.Servers;

/// <summary>A server made from a backup: its id, and why the backup was not restored on it (null when it was).</summary>
/// <param name="ServerId">The new server's id.</param>
/// <param name="Error">Why the backup was not restored; the server then stays empty.</param>
internal sealed record BackupCreation(string ServerId, string? Error);
