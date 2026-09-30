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
