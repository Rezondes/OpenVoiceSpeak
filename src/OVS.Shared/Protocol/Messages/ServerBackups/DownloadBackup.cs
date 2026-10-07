namespace OVS.Shared.Protocol.Messages.ServerBackups;

/// <summary>
/// Backups (Package 74, right ServerConfig).
/// Package 75 (A91): transfers in chunks of ProtocolLimits.BackupChunkBytes, always one request per chunk, so the outbox never fills up.
/// Asks for the chunk at Offset; answered with one BackupChunk.
/// </summary>
public sealed record DownloadBackup(string FileName, long Offset) : Request;
