using Microsoft.Extensions.Logging;

namespace OVS.Shared.Logging;

/// <summary>
/// Step 30d3: the one <see cref="ILoggerProvider"/> of the log files. A line is the time stamp and the message, exactly as
/// before (no level, no category in the file). The category says where it goes: <see cref="ServerCategory"/> to the
/// server file and the console, <see cref="ChannelCategory"/> to that channel's folder; any other category writes
/// nothing (framework logs such as the host's never reach the files).
/// </summary>
public sealed class LogFileLoggerProvider(LogFileWriter files, Action<string> console) : ILoggerProvider
{
    /// <summary>The server log: logs/server/ and the console.</summary>
    public const string ServerCategory = "server";

    /// <summary>Package 148: lines only for the console, such as the admin token; no other logger provider gets this category.</summary>
    public const string ConsoleCategory = "console";

    /// <summary>A line only for the console, such as the admin token; matched by its name, so several logger messages can carry it.</summary>
    public static readonly EventId ConsoleOnly = new(1, nameof(ConsoleOnly));

    /// <summary>A line only for the file; matched by its name like <see cref="ConsoleOnly"/>.</summary>
    public static readonly EventId FileOnly = new(2, nameof(FileOnly));

    /// <summary>
    /// Package 165: the start of the event name of an administrative action (who changed what), such as "Audit:GroupCreated";
    /// its line carries the audit marker and the actor after the time stamp (<see cref="AuditLine"/>), so the administration
    /// finds them with the log search. The actor is the line's "Moderator", or else its "Nickname".
    /// </summary>
    public const string AuditEventPrefix = "Audit:";

    /// <summary>The log of one channel, in a folder named by its id, so a rename keeps its history together.</summary>
    public static string ChannelCategory(Guid channelId) => "channels/" + channelId;

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName) =>
        categoryName == ServerCategory ? new FileLogger(files, ServerCategory, console)
        : categoryName == ConsoleCategory ? new FileLogger(files, null, console)
        : categoryName.StartsWith("channels/", StringComparison.Ordinal) ? new FileLogger(files, categoryName, null)
        : new FileLogger(null, "", null);

    /// <inheritdoc/>
    public void Dispose()
    {
    }

    /// <summary>
    /// Writes into one folder of the logs (none: the console only), and to the console when it has one; without files it
    /// writes nothing.
    /// </summary>
    sealed class FileLogger(LogFileWriter? files, string? folder, Action<string>? console) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => files is not null && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var text = exception is null ? formatter(state, exception) : $"{formatter(state, exception)} {exception}"; // Package 143: with the stack trace
            var marker = eventId.Name?.StartsWith(AuditEventPrefix, StringComparison.Ordinal) == true ? AuditLine.Prefix(ActorOf(state)) : ""; // Package 165
            var line = LogFileWriter.EscapeToOneLine($"{files!.FormatNow()} {marker}{text}"); // Package 90: the console gets the escaped line too
            if (console is not null && eventId.Name != FileOnly.Name) console(line);
            if (folder is not null && eventId.Name != ConsoleOnly.Name) files.Append(folder.Replace('/', Path.DirectorySeparatorChar), "", line);
        }

        /// <summary>Package 165: who did it, the moderator of a moderation line, else the nickname.</summary>
        static string ActorOf<TState>(TState state)
        {
            var values = state as IEnumerable<KeyValuePair<string, object?>> ?? [];
            return values.FirstOrDefault(pair => pair.Key == "Moderator").Value?.ToString()
                ?? values.FirstOrDefault(pair => pair.Key == "Nickname").Value?.ToString() ?? "";
        }
    }
}
