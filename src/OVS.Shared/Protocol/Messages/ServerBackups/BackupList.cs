namespace OVS.Shared.Protocol.Messages.ServerBackups;

/// <summary>Backups (Package 74, right ServerConfig). Newest first.</summary>
public sealed record BackupList(string? RequestId, IReadOnlyList<BackupInfo> Backups, int Offset = 0, int Total = 0) : Message;
