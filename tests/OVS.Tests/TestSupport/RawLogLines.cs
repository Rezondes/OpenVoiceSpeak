using Microsoft.Extensions.Logging;
using OVS.Server.Logging;
using OVS.Shared.Logging;

namespace OVS.Tests.TestSupport;

/// <summary>
/// Free text into the server's log files, for the tests of the files themselves (rotation, escaping, locking, the
/// golden bytes); the product writes only the logger messages of ServerLogLines and ChannelLogLines.
/// </summary>
internal static class RawLogLines
{
    /// <param name="logs">The writer under test.</param>
    /// <param name="text">The line's text after the time stamp.</param>
    /// <param name="toFile">False writes only to the console.</param>
    /// <param name="toConsole">False writes only to the file.</param>
    public static void WriteServerLine(this ServerLogWriter logs, string text, bool toFile = true, bool toConsole = true)
    {
        if (!toFile && !toConsole) return;
        var target = !toConsole ? LogFileLoggerProvider.FileOnly : !toFile ? LogFileLoggerProvider.ConsoleOnly : default;
        logs.Server.Log(LogLevel.Information, target, text, null, (line, _) => line);
    }

    /// <param name="logs">The writer under test.</param>
    /// <param name="channelId">The channel, which names the folder.</param>
    /// <param name="channelName">The name in brackets in front of the line.</param>
    /// <param name="text">The line's text after the name.</param>
    public static void WriteChannelLine(this ServerLogWriter logs, Guid channelId, string channelName, string text) =>
        logs.Channel(channelId, channelName).Log(LogLevel.Information, default, text, null, (line, _) => line);
}
