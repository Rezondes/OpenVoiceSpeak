namespace OVS.Shared.Protocol.Messages.ServerBackups;

/// <param name="UploadId">32 lowercase hex digits chosen by the client; a new id at offset 0 replaces an unfinished upload.</param>
/// <summary>
/// Backups (Package 74, right ServerConfig).
/// Package 75 (A91): transfers in chunks of ProtocolLimits.BackupChunkBytes, always one request per chunk, so the outbox never fills up.
/// Answered with UploadBackupAck, the last one (IsLast) with BackupUploaded and the new BackupList once the archive is valid.
/// </summary>
/// <param name="Offset">The position of this chunk in the archive.</param>
/// <param name="DataBase64">The chunk's bytes as base64.</param>
/// <param name="IsLast">True for the archive's last chunk.</param>
public sealed record UploadBackupChunk(string UploadId, long Offset, string DataBase64, bool IsLast) : Request;
