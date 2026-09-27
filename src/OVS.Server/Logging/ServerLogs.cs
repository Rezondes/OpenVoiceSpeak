using OVS.Shared.Logging;

namespace OVS.Server.Logging;

/// <summary>
/// Server log (console and logs/server/&lt;start&gt;.log) plus one log per channel (logs/channels/&lt;id&gt;/&lt;start&gt;.log),
/// a new file per server start and optionally per day, see LogFiles.
/// A failing disk never takes the server down: the first write error is reported on the console.
/// </summary>
public sealed class ServerLogs
{
    readonly LogFiles files;
    readonly Action<string> console;

    /// <param name="keepDays">0 keeps every file.</param>
    /// <param name="newFileEachDay">False keeps the files of this run until the next start.</param>
    public ServerLogs(string dataDir, int keepDays, TimeProvider time, Action<string> console, bool newFileEachDay = true)
    {
        files = new LogFiles(Path.Combine(dataDir, "logs"), keepDays, time, console, newFileEachDay);
        this.console = console;
    }

    /// <param name="toFile">False for secrets such as the admin token, which only belong on the console.</param>
    public void Server(string text, bool toFile = true, bool toConsole = true)
    {
        var line = $"{files.Stamp()} {text}";
        if (toConsole) console(line);
        if (toFile) files.Append("server", "", line);
    }

    /// <summary>The folder is named by id, so renaming a channel keeps its history together.</summary>
    public void Channel(Guid channelId, string channelName, string text) =>
        files.Append(Path.Combine("channels", channelId.ToString()), "", $"{files.Stamp()} [{channelName}] {text}");
}
