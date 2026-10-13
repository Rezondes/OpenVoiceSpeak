namespace OVS.Shared.Protocol.Messages.ServerBackups;

/// <summary>
/// Backups (Package 74, right ServerConfig).
/// Package 75 (A91): transfers in chunks of ProtocolLimits.BackupChunkBytes, always one request per chunk, so the outbox never fills up.
/// </summary>
/// <param name="Received">The bytes stored so far, the offset of the next chunk.</param>
/// <param name="RequestId">The request this answers.</param>
/// <param name="UploadId">The upload's id.</param>
public sealed record UploadBackupAck(string? RequestId, string UploadId, long Received) : Message;
