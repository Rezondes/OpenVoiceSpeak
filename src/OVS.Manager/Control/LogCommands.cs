using OVS.Manager.Audit;
using OVS.Manager.Localization;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.LogAccess;
using Error = OVS.Shared.Protocol.Messages.Connection.Error;

namespace OVS.Manager.Control;

/// <summary>
/// Package M16 (Packages 81, 82): a server's own log through the service session: listed per file, paged, searched and
/// downloaded. The channel logs hold chat and are never listed, read or downloaded here (OpenTelemetry decision 3).
/// </summary>
internal sealed class LogCommands(ServerConnections connections, AuditLog audit)
{
    /// <summary>The start of a server log's id; channel logs start with "channels/".</summary>
    public const string ServerFolder = "server/";

    /// <summary>AC1, AC4: the server's log files, newest first.</summary>
    public async Task<LogFilePage> ListAsync(string serverId, CancellationToken cancellation = default)
    {
        if (Connected(serverId) is not { } link) return new LogFilePage([], Strings.Link_NotConnected);
        return await link.RequestAsync(new ListLogs(), cancellation: cancellation) switch
        {
            LogList list => new LogFilePage([.. list.Files.Where(file => file.Kind == LogKind.Server && IsServerLog(file.Id))], null),
            Error error => new LogFilePage([], ServerTexts.Refusal(error)),
            _ => new LogFilePage([], Strings.Link_NotConnected),
        };
    }

    /// <summary>AC1: one page of a server log; <paramref name="page"/> null is the last page.</summary>
    public async Task<LogText> ReadAsync(string serverId, string fileId, int? page, CancellationToken cancellation = default)
    {
        if (!IsServerLog(fileId)) return Nothing(Strings.Log_ServerOnly);
        if (Connected(serverId) is not { } link) return Nothing(Strings.Link_NotConnected);
        return await link.RequestAsync(new ReadLog(fileId, page), cancellation: cancellation) switch
        {
            LogPage read => new LogText(read, [], null),
            Error error => Nothing(ServerTexts.Refusal(error)),
            _ => Nothing(Strings.Link_NotConnected),
        };
    }

    /// <summary>AC1: the page of <paramref name="hit"/>, so the hit opens its place.</summary>
    public Task<LogText> ReadAtAsync(string serverId, LogHit hit, CancellationToken cancellation = default) =>
        ReadAsync(serverId, hit.FileId, (hit.Line - 1) / WireProtocol.LogPageLines + 1, cancellation);

    /// <summary>AC1: searches the server logs for <paramref name="query"/>.</summary>
    public async Task<LogText> SearchAsync(string serverId, string query, CancellationToken cancellation = default)
    {
        if (Connected(serverId) is not { } link) return Nothing(Strings.Link_NotConnected);
        return await link.RequestAsync(new SearchLogs(query, LogKind.Server), cancellation: cancellation) switch
        {
            LogSearchResult result => new LogText(null, [.. result.Hits.Where(hit => IsServerLog(hit.FileId))], null),
            Error error => Nothing(ServerTexts.Refusal(error)),
            _ => Nothing(Strings.Link_NotConnected),
        };
    }

    /// <summary>AC2: streams a server log file (the server sends a single file as it is) into <paramref name="output"/>; returns null or why it failed.</summary>
    public async Task<string?> DownloadAsync(string serverId, string fileId, Stream output, string actor, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (!IsServerLog(fileId)) return Strings.Log_ServerOnly;
        if (Connected(serverId) is not { } link) return Strings.Link_NotConnected;
        if (await link.RequestAsync(new PrepareLogDownload([fileId]), cancellation: cancellation) is not LogDownloadReady ready)
            return Strings.Log_DownloadFailed;
        long offset = 0;
        while (true)
        {
            switch (await link.RequestAsync(new DownloadLogChunk(ready.DownloadId, offset), cancellation: cancellation))
            {
                case LogChunk chunk:
                    var bytes = Convert.FromBase64String(chunk.DataBase64);
                    await output.WriteAsync(bytes, cancellation);
                    offset += bytes.Length;
                    if (!chunk.IsLast && bytes.Length > 0) break;
                    await audit.WriteAsync(actor, AuditActions.LogDownloaded, fileId, serverId, cancellation: cancellation);
                    return null;
                case Error error:
                    return ServerTexts.Refusal(error);
                default:
                    return Strings.Link_NotConnected;
            }
        }
    }

    /// <summary>The download's file name, as the server names a single file: its own name.</summary>
    public static string FileName(string fileId) => fileId[(fileId.LastIndexOf('/') + 1)..];

    /// <summary>AC4: only the server's own log, never a channel log.</summary>
    public static bool IsServerLog(string fileId) => fileId.StartsWith(ServerFolder, StringComparison.Ordinal) && !fileId.Contains("..", StringComparison.Ordinal);

    ServerLink? Connected(string serverId) => connections.LinkOf(serverId) is { Status: ServerLinkStatus.Connected } link ? link : null;

    static LogText Nothing(string error) => new(null, [], error);
}
