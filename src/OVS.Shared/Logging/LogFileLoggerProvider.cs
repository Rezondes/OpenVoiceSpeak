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
            var line = LogFileWriter.EscapeToOneLine($"{files!.FormatNow()} {text}"); // Package 90: the console gets the escaped line too
            if (console is not null && eventId.Name != FileOnly.Name) console(line);
            if (folder is not null && eventId.Name != ConsoleOnly.Name) files.Append(folder.Replace('/', Path.DirectorySeparatorChar), "", line);
        }
    }
}
