using Microsoft.Extensions.Logging;
using OVS.Shared.Logging;

namespace OVS.Server.Logging;

/// <summary>
/// Server log (console and logs/server/&lt;start&gt;.log) plus one log per channel (logs/channels/&lt;id&gt;/&lt;start&gt;.log),
/// a new file per server start and optionally per day, see LogFileWriter.
/// A failing disk never takes the server down: the first write error is reported on the console.
/// Step 30d3: the lines go through <see cref="LogFileLoggerProvider"/>; Step 31 turns each call into a logger message.
/// </summary>
public sealed class ServerLogWriter
{
    readonly LogFileWriter files;
    readonly LogFileLoggerProvider provider;
    readonly ILogger server;

    /// <param name="keepDays">0 keeps every file.</param>
    /// <param name="newFileEachDay">False keeps the files of this run until the next start.</param>
    /// <param name="dataDir">The data folder; the logs go into its logs folder.</param>
    /// <param name="time">The clock of the time stamps and file names.</param>
    /// <param name="console">Gets every line meant for the console, and the write failures.</param>
    public ServerLogWriter(string dataDir, int keepDays, TimeProvider time, Action<string> console, bool newFileEachDay = true)
    {
        files = new LogFileWriter(Path.Combine(dataDir, "logs"), keepDays, time, console, newFileEachDay);
        provider = new LogFileLoggerProvider(files, console);
        server = provider.CreateLogger(LogFileLoggerProvider.ServerCategory);
    }

    /// <summary>Package 69: retention and daily files come from the administration and change at runtime.</summary>
    public void Update(int keepDays, bool newFileEachDay) => files.Update(keepDays, newFileEachDay);

    /// <param name="toFile">False for secrets such as the admin token, which only belong on the console.</param>
    /// <param name="text">The line's text after the time stamp.</param>
    /// <param name="toConsole">False for lines that only belong in the file.</param>
    public void WriteServerLine(string text, bool toFile = true, bool toConsole = true)
    {
        if (!toFile && !toConsole) return;
        var target = !toConsole ? LogFileLoggerProvider.FileOnly : !toFile ? LogFileLoggerProvider.ConsoleOnly : default;
        server.Log(LogLevel.Information, target, text, null, (line, _) => line);
    }

    /// <summary>The folder is named by id, so renaming a channel keeps its history together.</summary>
    public void WriteChannelLine(Guid channelId, string channelName, string text) =>
        provider.CreateLogger(LogFileLoggerProvider.ChannelCategory(channelId)).Log(LogLevel.Information, default, $"[{channelName}] {text}", null, (line, _) => line);
}
