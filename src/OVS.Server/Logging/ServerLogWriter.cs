using Microsoft.Extensions.Logging;
using OVS.Shared.Logging;

namespace OVS.Server.Logging;

/// <summary>
/// Server log (console and logs/server/&lt;start&gt;.log) plus one log per channel (logs/channels/&lt;id&gt;/&lt;start&gt;.log),
/// a new file per server start and optionally per day, see LogFileWriter.
/// A failing disk never takes the server down: the first write error is reported on the console.
/// Step 31: the lines are the logger messages of <see cref="ServerLogLines"/> and <see cref="ChannelLogLines"/>.
/// Package 144 (A154): every logger comes from the writer's <see cref="Factory"/>, which holds the file provider and whatever
/// a run adds; the run's container uses that factory, and disposing the writer disposes it and its providers.
/// </summary>
internal sealed class ServerLogWriter : IDisposable
{
    readonly LogFileWriter files;

    /// <param name="keepDays">0 keeps every file.</param>
    /// <param name="newFileEachDay">False keeps the files of this run until the next start.</param>
    /// <param name="dataDir">The data folder; the logs go into its logs folder.</param>
    /// <param name="time">The clock of the time stamps and file names.</param>
    /// <param name="console">Gets every line meant for the console, and the write failures.</param>
    /// <param name="configureLogging">Adds further logger providers beside the file provider.</param>
    public ServerLogWriter(string dataDir, int keepDays, TimeProvider time, Action<string> console, bool newFileEachDay = true,
        Action<ILoggingBuilder>? configureLogging = null)
    {
        files = new LogFileWriter(Path.Combine(dataDir, "logs"), keepDays, time, console, newFileEachDay);
        Factory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace); // the file provider decides what it writes, as before
            builder.AddProvider(new LogFileLoggerProvider(files, console));
            configureLogging?.Invoke(builder);
        });
        Server = Factory.CreateLogger(LogFileLoggerProvider.ServerCategory);
        Console = Factory.CreateLogger(LogFileLoggerProvider.ConsoleCategory);
    }

    /// <summary>Package 144: the run's logger factory, also the one of its container.</summary>
    public ILoggerFactory Factory { get; }

    /// <summary>The server log: logs/server/ and the console.</summary>
    public ILogger Server { get; }

    /// <summary>Package 148: the console alone, for secrets such as the admin token; never a file, never the backend.</summary>
    public ILogger Console { get; }

    /// <summary>Disposes the factory and its providers, which flushes what they still hold.</summary>
    public void Dispose() => Factory.Dispose();

    /// <summary>Package 69: retention and daily files come from the administration and change at runtime.</summary>
    public void Update(int keepDays, bool newFileEachDay) => files.Update(keepDays, newFileEachDay);

    /// <summary>
    /// The log of one channel; its folder is named by id, so renaming a channel keeps its history together, and each
    /// line carries the channel's current name in brackets.
    /// </summary>
    public ILogger Channel(Guid channelId, string channelName) =>
        new NamedChannelLogger(Factory.CreateLogger(LogFileLoggerProvider.ChannelCategory(channelId)), channelName);

    /// <summary>Puts "[name] " in front of every line.</summary>
    sealed class NamedChannelLogger(ILogger inner, string name) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            inner.Log(logLevel, eventId, state, exception, (value, error) => $"[{name}] {formatter(value, error)}");
    }
}
