namespace OVS.Shared.Protocol.Messages.LogAccess;

/// <summary>
/// Logs (Package 81, A98: right LogsView).
/// Package 82 (A99, rights LogsView and LogsDownload): a snapshot on the server, then pulled in chunks like a backup.
/// </summary>
public sealed record LogChunk(string? RequestId, string DownloadId, long Offset, long TotalSize, string DataBase64, bool IsLast) : Message;
