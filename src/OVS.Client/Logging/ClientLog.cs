using Microsoft.Extensions.Logging;
using OVS.Shared.Logging;

namespace OVS.Client.Logging;

/// <summary>
/// The client's one log: &lt;profile&gt;/logs/client-&lt;start&gt;.log, a new file per client start, kept 30 days. Never contains the server password,
/// the admin token or the identity key. Writing never throws. Step 30d4: it is also the <see cref="ILogger"/> of the client; a
/// message goes through <see cref="Write"/>, the time stamp and the text as before.
/// </summary>
internal sealed class ClientLog : ILogger
{
    /// <summary>Days the client's log files are kept.</summary>
    public const int KeepDays = 30;
    readonly LogFileWriter files;

    /// <summary>A log in the profile's logs folder.</summary>
    public ClientLog(string profileDir, TimeProvider time) =>
        files = new LogFileWriter(Path.Combine(profileDir, "logs"), KeepDays, time, line => System.Diagnostics.Trace.WriteLine(line));

    // Package 124: every server message was logged on the UI thread, a file opened and closed per line. Lines now queue
    // and a pool thread writes what has gathered, opening the file once per batch (not held open: readers that lock the
    // file against writing, like File.ReadAllText or an editor, keep working between batches).
    readonly Queue<string> pending = new();
    readonly object writer = new();
    bool scheduled;

    /// <summary>Queues the line with the time of the call; it is written in the background, in order.</summary>
    public void Write(string text)
    {
        var line = $"{files.FormatNow()} {text}";
        lock (pending)
        {
            pending.Enqueue(line);
            if (scheduled) return;
            scheduled = true;
        }
        ThreadPool.UnsafeQueueUserWorkItem(_ => Flush(), null);
    }

    /// <inheritdoc/>
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    /// <inheritdoc/>
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (IsEnabled(logLevel)) Write(formatter(state, exception));
    }

    /// <summary>Raised on the thread about to write a batch (tests check it is never the UI thread).</summary>
    internal event Action? Writing;

    /// <summary>Writes everything queued, on the calling thread; at exit, so no line is lost.</summary>
    public void Flush()
    {
        lock (writer) // one batch at a time, so the order holds
        {
            bool drained = false;
            try
            {
                while (true)
                {
                    string[] lines;
                    lock (pending)
                    {
                        if (pending.Count == 0)
                        {
                            scheduled = false; // with the check, so a line queued right after schedules its own write
                            drained = true;
                            return;
                        }
                        lines = [.. pending];
                        pending.Clear();
                    }
                    try
                    {
                        Writing?.Invoke();
                        files.Append("", "client-", lines);
                    }
                    #pragma warning disable CA1031 // writing never throws (LogFileWriter catches IO errors); anything else costs a batch, not the log
                    catch (Exception exception) // writing never throws (LogFileWriter catches IO errors); anything else costs a batch, not the log
                    #pragma warning restore CA1031
                    {
                        System.Diagnostics.Trace.WriteLine($"Log could not be written: {exception.Message}");
                    }
                }
            }
            finally
            {
                if (!drained) lock (pending) scheduled = false; // whatever went wrong, the next line schedules a write again
            }
        }
    }
}
