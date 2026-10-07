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
public sealed partial class ServerState
{
    readonly LogReader logReader;
    readonly LogExporter logExporter;

    void OnListLogs(Session s, ListLogs r)
    {
        if (!Require(s, r, Permission.LogsView) || !ThrottleList(s, r, r.Offset)) return;
        var names = data.Channels.ToDictionary(c => c.Id, c => c.Name);
        int offset = Math.Max(0, r.Offset);
        QueueLogJob(s, r, () =>
        {
            var all = logReader.List(names);
            return new LogList(r.RequestId, Page(all, offset), offset, all.Count); // Package 87
        });
    }

    void OnReadLog(Session s, ReadLog r)
    {
        if (!Require(s, r, Permission.LogsView)) return;
        if (r.Page is < 1)
        {
            Fail(s, r, ReasonCodes.InvalidValue, $"Seite {r.Page}");
            return;
        }
        QueueLogJob(s, r, () => logReader.Resolve(r.FileId) is { } path
            ? logReader.ReadPage(r.RequestId, r.FileId, path, r.Page)
            : new Error(r.RequestId, ReasonCodes.NotFound));
    }

    void OnSearchLogs(Session s, SearchLogs r)
    {
        if (!Require(s, r, Permission.LogsView)) return;
        if (string.IsNullOrWhiteSpace(r.Query) || r.Query.Length > ProtocolLimits.MaxLogQueryLength)
        {
            Fail(s, r, ReasonCodes.InvalidValue, "Suchtext leer oder zu lang");
            return;
        }
        // Package 90: only a search that was queued (and so runs) is logged; without the query, so a search never finds itself
        if (QueueLogJob(s, r, () => logReader.Search(r.RequestId, r))) logs.WriteServerLine($"Logs durchsucht von {s.Nickname}");
    }

    // ---- Package 82 (A99): download, a snapshot in logs-export pulled chunk by chunk like a backup ----

    const Permission DownloadRights = Permission.LogsView | Permission.LogsDownload;

    void OnPrepareLogDownload(Session s, PrepareLogDownload r)
    {
        if (!Require(s, r, DownloadRights)) return;
        if (r.FileIds is not { Count: > 0 } ids || ids.Distinct().Count() != ids.Count)
        {
            Fail(s, r, ReasonCodes.InvalidValue, "keine oder doppelte Dateien");
            return;
        }
        if (!ThrottleHeavy(s, r)) return; // Package 86
        var names = data.Channels.ToDictionary(c => c.Id, c => c.Name);
        QueueLogJob(s, r, () =>
        {
            var downloadId = Guid.NewGuid().ToString("N");
            var (export, code) = logExporter.Export(downloadId, ids, names);
            if (export is null) return new Error(r.RequestId, code);
            if (!s.LogDownload.TrySet(downloadId, export.Path))
            {
                SafeFile.DeleteQuietly(export.Path); // the session ended meanwhile
                return new Error(r.RequestId, ReasonCodes.NotFound);
            }
            logs.WriteServerLine($"Logs heruntergeladen von {s.Nickname}: {export.FileName} ({ids.Count} Dateien, {export.Size} Bytes)");
            return new LogDownloadReady(r.RequestId, downloadId, export.FileName, export.Size);
        });
    }

    /// <summary>One chunk per request, read off the lock; the snapshot is deleted after the last one.</summary>
    void OnDownloadLogChunk(Session s, DownloadLogChunk r)
    {
        if (!Require(s, r, DownloadRights)) return;
        QueueLogJob(s, r, () =>
        {
            if (s.LogDownload.FindPath(r.DownloadId) is not { } path) return new Error(r.RequestId, ReasonCodes.NotFound);
            if (SafeFile.ReadChunk(path, r.Offset) is not var (bytes, total)) return new Error(r.RequestId, ReasonCodes.InvalidValue, $"Offset {r.Offset}");
            bool last = r.Offset + bytes.Length >= total;
            if (last) s.LogDownload.Drop();
            return new LogChunk(r.RequestId, r.DownloadId, r.Offset, total, Convert.ToBase64String(bytes), last);
        });
    }

    /// <summary>Runs the file work after the session's earlier log jobs, off the lock; SessionOutbox.Send is safe from any thread.</summary>
    /// <returns>False when the session's queue was full and the request was answered with RateLimited.</returns>
    bool QueueLogJob(Session s, Request r, Func<Message> work)
    {
        var queued = s.LogJobs.TryQueue(() =>
        {
            try
            {
                s.Outbox.Send(work());
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                s.Outbox.Send(new Error(r.RequestId, ReasonCodes.InvalidValue, e.Message));
            }
        });
        if (!queued) Fail(s, r, ReasonCodes.RateLimited);
        return queued;
    }
}
