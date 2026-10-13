using OVS.Server.Logging;
using OVS.Shared.IO;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.LogAccess;

namespace OVS.Server.State;

/// <summary>
/// Package 81 (A98): the log viewer, all with the right LogsView. The lock only covers the check and the channel names;
/// the files are read on the session's log queue (AC7), so voice and chat keep running during a large search.
/// </summary>
internal sealed partial class ServerState
{
    readonly LogReader logReader;
    readonly LogExporter logExporter;

    void OnListLogs(Session session, ListLogs request)
    {
        if (!Require(session, request, Permission.LogsView) || !ThrottleList(session, request, request.Offset)) return;
        var names = data.Channels.ToDictionary(channel => channel.Id, channel => channel.Name);
        int offset = Math.Max(0, request.Offset);
        QueueLogJob(session, request, () =>
        {
            var all = logReader.List(names);
            return new LogList(request.RequestId, Page(all, offset), offset, all.Count); // Package 87
        });
    }

    void OnReadLog(Session session, ReadLog request)
    {
        if (!Require(session, request, Permission.LogsView)) return;
        if (request.Page is < 1)
        {
            Fail(session, request, ReasonCodes.InvalidValue, $"Page {request.Page}");
            return;
        }
        QueueLogJob(session, request, () => logReader.Resolve(request.FileId) is { } path
            ? logReader.ReadPage(request.RequestId, request.FileId, path, request.Page)
            : new Error(request.RequestId, ReasonCodes.NotFound));
    }

    void OnSearchLogs(Session session, SearchLogs request)
    {
        if (!Require(session, request, Permission.LogsView)) return;
        if (string.IsNullOrWhiteSpace(request.Query) || request.Query.Length > ProtocolLimits.MaxLogQueryLength)
        {
            Fail(session, request, ReasonCodes.InvalidValue, "Search text empty or too long");
            return;
        }
        // Package 90: only a search that was queued (and so runs) is logged; without the query, so a search never finds itself
        if (QueueLogJob(session, request, () => logReader.Search(request.RequestId, request))) logs.Server.LogsSearched(session.Nickname);
    }

    // ---- Package 82 (A99): download, a snapshot in logs-export pulled chunk by chunk like a backup ----

    const Permission DownloadRights = Permission.LogsView | Permission.LogsDownload;

    void OnPrepareLogDownload(Session session, PrepareLogDownload request)
    {
        if (!Require(session, request, DownloadRights)) return;
        if (request.FileIds is not { Count: > 0 } ids || ids.Distinct().Count() != ids.Count)
        {
            Fail(session, request, ReasonCodes.InvalidValue, "no files or duplicate files");
            return;
        }
        if (!ThrottleHeavy(session, request)) return; // Package 86
        var names = data.Channels.ToDictionary(channel => channel.Id, channel => channel.Name);
        QueueLogJob(session, request, () =>
        {
            var downloadId = Guid.NewGuid().ToString("N");
            var (export, code) = logExporter.Export(downloadId, ids, names);
            if (export is null) return new Error(request.RequestId, code);
            if (!session.LogDownload.TrySet(downloadId, export.Path))
            {
                SafeFile.DeleteQuietly(export.Path); // the session ended meanwhile
                return new Error(request.RequestId, ReasonCodes.NotFound);
            }
            logs.Server.LogsDownloaded(session.Nickname, export.FileName, ids.Count, export.Size);
            return new LogDownloadReady(request.RequestId, downloadId, export.FileName, export.Size);
        });
    }

    /// <summary>One chunk per request, read off the lock; the snapshot is deleted after the last one.</summary>
    void OnDownloadLogChunk(Session session, DownloadLogChunk request)
    {
        if (!Require(session, request, DownloadRights)) return;
        QueueLogJob(session, request, () =>
        {
            if (session.LogDownload.FindPath(request.DownloadId) is not { } path) return new Error(request.RequestId, ReasonCodes.NotFound);
            if (SafeFile.ReadChunk(path, request.Offset) is not var (bytes, total)) return new Error(request.RequestId, ReasonCodes.InvalidValue, $"Offset {request.Offset}");
            bool last = request.Offset + bytes.Length >= total;
            if (last) session.LogDownload.Drop();
            return new LogChunk(request.RequestId, request.DownloadId, request.Offset, total, Convert.ToBase64String(bytes), last);
        });
    }

    /// <summary>Runs the file work after the session's earlier log jobs, off the lock; SessionOutbox.Send is safe from any thread.</summary>
    /// <returns>False when the session's queue was full and the request was answered with RateLimited.</returns>
    bool QueueLogJob(Session session, Request request, Func<Message> work)
    {
        var queued = session.LogJobs.TryQueue(() =>
        {
            try
            {
                session.Outbox.Send(work());
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                session.Outbox.Send(new Error(request.RequestId, ReasonCodes.InvalidValue, exception.Message));
            }
        });
        if (!queued) Fail(session, request, ReasonCodes.RateLimited);
        return queued;
    }
}
