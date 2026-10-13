using Microsoft.Extensions.Logging;
using OVS.Shared.Logging;

namespace OVS.Server.Logging;

/// <summary>
/// Server log (console and logs/server/&lt;start&gt;.log) plus one log per channel (logs/channels/&lt;id&gt;/&lt;start&gt;.log),
/// a new file per server start and optionally per day, see LogFileWriter.
/// A failing disk never takes the server down: the first write error is reported on the console.
/// Step 31: the lines are the logger messages of <see cref="ServerLogLines"/> and <see cref="ChannelLogLines"/>.
/// </summary>
internal sealed class ServerLogWriter
{
    readonly LogFileWriter files;
    readonly LogFileLoggerProvider provider;

    /// <param name="keepDays">0 keeps every file.</param>
    /// <param name="newFileEachDay">False keeps the files of this run until the next start.</param>
    /// <param name="dataDir">The data folder; the logs go into its logs folder.</param>
    /// <param name="time">The clock of the time stamps and file names.</param>
    /// <param name="console">Gets every line meant for the console, and the write failures.</param>
    public ServerLogWriter(string dataDir, int keepDays, TimeProvider time, Action<string> console, bool newFileEachDay = true)
    {
        files = new LogFileWriter(Path.Combine(dataDir, "logs"), keepDays, time, console, newFileEachDay);
        provider = new LogFileLoggerProvider(files, console);
        Server = provider.CreateLogger(LogFileLoggerProvider.ServerCategory);
    }

    /// <summary>The server log: logs/server/ and the console.</summary>
    public ILogger Server { get; }

    /// <summary>Package 69: retention and daily files come from the administration and change at runtime.</summary>
    public void Update(int keepDays, bool newFileEachDay) => files.Update(keepDays, newFileEachDay);

    /// <summary>
    /// The log of one channel; its folder is named by id, so renaming a channel keeps its history together, and each
    /// line carries the channel's current name in brackets.
    /// </summary>
    public ILogger Channel(Guid channelId, string channelName) =>
        new NamedChannelLogger(provider.CreateLogger(LogFileLoggerProvider.ChannelCategory(channelId)), channelName);

    /// <summary>Puts "[name] " in front of every line.</summary>
    sealed class NamedChannelLogger(ILogger inner, string name) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            inner.Log(logLevel, eventId, state, exception, (value, error) => $"[{name}] {formatter(value, error)}");
    }
}
