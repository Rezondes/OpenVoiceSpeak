namespace OVS.Shared.Protocol.Messages.ServerBackups;

/// <summary>Backups (Package 74, right ServerConfig). Answered with the new BackupList.</summary>
public sealed record CreateBackup : Request;
