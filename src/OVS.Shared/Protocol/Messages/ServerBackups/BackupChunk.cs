namespace OVS.Shared.Protocol.Messages.ServerBackups;

/// <summary>
/// Backups (Package 74, right ServerConfig).
/// Package 75 (A91): transfers in chunks of ProtocolInfo.BackupChunkBytes, always one request per chunk, so the outbox never fills up.
/// </summary>
public sealed record BackupChunk(string? RequestId, string FileName, long Offset, long TotalSize, string DataBase64, bool IsLast) : Message;
