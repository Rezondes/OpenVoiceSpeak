namespace OVS.Shared.Protocol.Messages.ServerBackups;

/// <summary>
/// Backups (Package 74, right ServerConfig).
/// Takes a safety backup, disconnects everyone with Restoring and restarts the server on the backup's state.
/// </summary>
public sealed record RestoreBackup(string FileName) : Request;
