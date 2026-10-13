namespace OVS.Shared.Protocol.Messages.LogAccess;

/// <summary>
/// Logs (Package 81, A98: right LogsView).
/// Package 82 (A99, rights LogsView and LogsDownload): a snapshot on the server, then pulled in chunks like a backup.
/// </summary>
/// <param name="DownloadId">Names the snapshot in DownloadLogChunk; one per session, a new one replaces it.</param>
/// <param name="FileName">The file's own name, or ovs-logs_&lt;from&gt;_&lt;to&gt;.zip.</param>
/// <param name="RequestId">The request this answers.</param>
/// <param name="Size">The snapshot's size in bytes.</param>
public sealed record LogDownloadReady(string? RequestId, string DownloadId, string FileName, long Size) : Message;
