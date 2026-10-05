namespace OVS.Shared.Protocol.Messages.ServerBackups;

/// <summary>Backups (Package 74, right ServerConfig).</summary>
/// <param name="FileName">Only the name inside the server's backups folder; requests name a backup by it.</param>
public sealed record BackupInfo(string FileName, DateTimeOffset CreatedAt, long Size, string ServerVersion);
