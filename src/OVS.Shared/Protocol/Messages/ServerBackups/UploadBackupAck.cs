namespace OVS.Shared.Protocol.Messages.ServerBackups;

/// <summary>
/// Backups (Package 74, right ServerConfig).
/// Package 75 (A91): transfers in chunks of ProtocolInfo.BackupChunkBytes, always one request per chunk, so the outbox never fills up.
/// </summary>
/// <param name="Received">The bytes stored so far, the offset of the next chunk.</param>
public sealed record UploadBackupAck(string? RequestId, string UploadId, long Received) : Message;
