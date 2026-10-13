using OVS.Shared.IO;

namespace OVS.Server.Logging;

/// <summary>
/// Package 82: one session's prepared log download, a file in logs-export deleted with the session. Its own lock is
/// taken inside the ServerState lock (<see cref="End"/>) and by log jobs; it never takes another lock.
/// </summary>
public sealed class LogDownload
{
    readonly object gate = new();
    (string Id, string Path)? current;
    bool ended;

    /// <summary>Replaces the prepared download (its file is deleted); false once the session ended, the caller deletes the file then.</summary>
    public bool TrySet(string id, string path)
    {
        lock (gate)
        {
            if (ended) return false;
            if (current is { } old) SafeFile.DeleteQuietly(old.Path);
            current = (id, path);
            return true;
        }
    }

    /// <summary>The prepared file when id is the current download, else null.</summary>
    public string? FindPath(string? id)
    {
        lock (gate) return current is { } download && download.Id == id ? download.Path : null;
    }

    /// <summary>After the last chunk: deletes the file.</summary>
    public void Drop() => Drop(end: false);

    /// <summary>When the session ends: deletes the file, later downloads are refused.</summary>
    public void End() => Drop(end: true);

    void Drop(bool end)
    {
        lock (gate)
        {
            ended |= end;
            if (current is { } download) SafeFile.DeleteQuietly(download.Path);
            current = null;
        }
    }
}
