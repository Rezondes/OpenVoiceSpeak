namespace OVS.Shared.Protocol.Messages.LogAccess;

/// <summary>
/// Logs (Package 81, A98: right LogsView).
/// Package 82 (A99, rights LogsView and LogsDownload): a snapshot on the server, then pulled in chunks like a backup.
/// Asks for the chunk at Offset; answered with one LogChunk. The snapshot is deleted after the last one.
/// </summary>
public sealed record DownloadLogChunk(string DownloadId, long Offset) : Request;
