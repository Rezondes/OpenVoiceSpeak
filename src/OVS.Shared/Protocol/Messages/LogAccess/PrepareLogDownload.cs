namespace OVS.Shared.Protocol.Messages.LogAccess;

/// <summary>
/// Logs (Package 81, A98: right LogsView).
/// Package 82 (A99, rights LogsView and LogsDownload): a snapshot on the server, then pulled in chunks like a backup.
/// One file comes as .log, several as one zip (server/..., channels/&lt;name&gt;_&lt;id&gt;/...). Answered with LogDownloadReady.
/// </summary>
public sealed record PrepareLogDownload(IReadOnlyList<string> FileIds) : Request;
