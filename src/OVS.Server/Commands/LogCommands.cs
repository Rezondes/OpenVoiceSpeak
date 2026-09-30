using OVS.Server.Data;
using OVS.Server.Logging;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;

namespace OVS.Server;

/// <summary>
/// Package 81 (A98): the log viewer, all with the right LogsView. The lock only covers the check and the channel names;
/// the files are read on the session's log queue (AC7), so voice and chat keep running during a large search.
/// </summary>
public sealed partial class ServerState
{
    public LogReader LogReader { get; }

    void OnListLogs(Session s, ListLogs r)
    {
        if (!Require(s, r, Permission.LogsView)) return;
        var names = data.Channels.ToDictionary(c => c.Id, c => c.Name);
        QueueLogJob(s, r, () => new LogList(r.RequestId, LogReader.List(names)));
    }

    void OnReadLog(Session s, ReadLog r)
    {
        if (!Require(s, r, Permission.LogsView)) return;
        if (r.Page is < 1)
        {
            Fail(s, r, Codes.InvalidValue, $"Seite {r.Page}");
            return;
        }
        QueueLogJob(s, r, () => LogReader.Resolve(r.FileId) is { } path
            ? LogReader.ReadPage(r.RequestId, r.FileId, path, r.Page)
            : new Error(r.RequestId, Codes.NotFound));
    }

    void OnSearchLogs(Session s, SearchLogs r)
    {
        if (!Require(s, r, Permission.LogsView)) return;
        if (string.IsNullOrWhiteSpace(r.Query) || r.Query.Length > ProtocolInfo.MaxLogQueryLength)
        {
            Fail(s, r, Codes.InvalidValue, "Suchtext leer oder zu lang");
            return;
        }
        logs.Server($"Logs durchsucht von {s.Nickname}"); // without the query, so a search never finds itself
        QueueLogJob(s, r, () => LogReader.Search(r.RequestId, r));
    }

    // ---- Package 82 (A99): download, a snapshot in logs-export pulled chunk by chunk like a backup ----

    const Permission DownloadRights = Permission.LogsView | Permission.LogsDownload;

    void OnPrepareLogDownload(Session s, PrepareLogDownload r)
    {
        if (!Require(s, r, DownloadRights)) return;
        if (r.FileIds is not { Count: > 0 } ids || ids.Distinct().Count() != ids.Count)
        {
            Fail(s, r, Codes.InvalidValue, "keine oder doppelte Dateien");
            return;
        }
        var names = data.Channels.ToDictionary(c => c.Id, c => c.Name);
        QueueLogJob(s, r, () =>
        {
            var downloadId = Guid.NewGuid().ToString("N");
            var (export, code) = LogReader.Export(downloadId, ids, names);
            if (export is null) return new Error(r.RequestId, code);
            if (!s.SetLogDownload(downloadId, export.Path))
            {
                BackupStore.DeleteQuietly(export.Path); // the session ended meanwhile
                return new Error(r.RequestId, Codes.NotFound);
            }
            logs.Server($"Logs heruntergeladen von {s.Nickname}: {export.FileName} ({ids.Count} Dateien, {export.Size} Bytes)");
            return new LogDownloadReady(r.RequestId, downloadId, export.FileName, export.Size);
        });
    }

    /// <summary>One chunk per request, read off the lock; the snapshot is deleted after the last one.</summary>
    void OnDownloadLogChunk(Session s, DownloadLogChunk r)
    {
        if (!Require(s, r, DownloadRights)) return;
        QueueLogJob(s, r, () =>
        {
            if (s.LogDownloadPath(r.DownloadId) is not { } path) return new Error(r.RequestId, Codes.NotFound);
            if (BackupStore.ReadChunk(path, r.Offset) is not var (bytes, total)) return new Error(r.RequestId, Codes.InvalidValue, $"Offset {r.Offset}");
            bool last = r.Offset + bytes.Length >= total;
            if (last) s.DropLogDownload();
            return new LogChunk(r.RequestId, r.DownloadId, r.Offset, total, Convert.ToBase64String(bytes), last);
        });
    }

    /// <summary>Runs the file work after the session's earlier log jobs, off the lock; Session.Send is safe from any thread.</summary>
    void QueueLogJob(Session s, Request r, Func<Message> work)
    {
        var queued = s.TryQueueLogJob(() =>
        {
            try
            {
                s.Send(work());
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                s.Send(new Error(r.RequestId, Codes.InvalidValue, e.Message));
            }
        });
        if (!queued) Fail(s, r, Codes.RateLimited);
    }
}
