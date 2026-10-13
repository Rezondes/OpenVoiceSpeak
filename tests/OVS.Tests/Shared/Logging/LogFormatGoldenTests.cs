using System.Text;
using OVS.Client.Logging;
using OVS.Server.Logging;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Shared.Logging;

/// <summary>
/// Step 30d1: the bytes of the log files are a product feature (the server's LogReader, LogExporter and LogDownload and
/// the client's log viewer read them), so they are pinned here before the logging moves to ILogger: the time stamp in
/// local time, one space, the text; a channel line carries its channel's name in brackets; control characters escaped;
/// one file per start, named after the start.
/// </summary>
public sealed class LogFormatGoldenTests() : TempDirectoryTest("ovs-golden-", quiet: false)
{
    /// <summary>13 October 2026, 14:05:09.123 in UTC, which the logs read as local time.</summary>
    readonly ManualTimeProvider clock = new(new DateTimeOffset(2026, 10, 13, 14, 5, 9, 123, TimeSpan.Zero)) { Local = TimeZoneInfo.Utc };
    readonly List<string> console = [];

    const string Stamp = "2026-10-13 14:05:09.123";
    const string FileName = "2026-10-13_14-05-09.log";

    static string Text(string path) => File.ReadAllText(path, Encoding.UTF8);

    [Fact]
    public void ServerLine_ToFileAndConsole_StampSpaceText()
    {
        using var logs = new ServerLogWriter(dir, 0, clock, console.Add);

        logs.WriteServerLine("Server startet");

        Assert.Equal($"{Stamp} Server startet{Environment.NewLine}", Text(Path.Combine(dir, "logs", "server", FileName)));
        Assert.Equal([$"{Stamp} Server startet"], console);
    }

    [Fact]
    public void ServerLine_SecretOrConsoleOnly_GoesOnlyWhereItIsAsked()
    {
        using var logs = new ServerLogWriter(dir, 0, clock, console.Add);

        logs.WriteServerLine("Admin-Token: geheim", toFile: false);
        logs.WriteServerLine("nur in die Datei", toConsole: false);

        Assert.Equal($"{Stamp} nur in die Datei{Environment.NewLine}", Text(Path.Combine(dir, "logs", "server", FileName)));
        Assert.Equal([$"{Stamp} Admin-Token: geheim"], console);
    }

    [Fact]
    public void ChannelLine_InTheChannelsFolder_CarriesTheNameInBrackets()
    {
        using var logs = new ServerLogWriter(dir, 0, clock, console.Add);
        var channel = Guid.Parse("11111111-2222-3333-4444-555555555555");

        logs.WriteChannelLine(channel, "Raid", "anna betritt den Channel");

        Assert.Equal($"{Stamp} [Raid] anna betritt den Channel{Environment.NewLine}",
            Text(Path.Combine(dir, "logs", "channels", channel.ToString(), FileName)));
        Assert.Empty(console);
    }

    [Fact]
    public void ServerLine_ControlCharacters_EscapedOnOneLine()
    {
        using var logs = new ServerLogWriter(dir, 0, clock, console.Add);

        logs.WriteServerLine("eins" + (char)0x0A + "zwei" + (char)0x0D + (char)0x09 + "drei" + (char)0x1B + "[31m" + (char)0x2028);

        var expected = Stamp + @" eins\nzwei\r\tdrei\u001B[31m\u2028";
        Assert.Equal(expected + Environment.NewLine, Text(Path.Combine(dir, "logs", "server", FileName)));
        Assert.Equal([expected], console);
    }

    [Fact]
    public void ClientLine_AfterFlush_InTheClientFileOfThisStart()
    {
        var log = new ClientLog(dir, clock);

        log.Write("Client startet");
        log.Write("zweite Zeile");
        log.Flush();

        Assert.Equal($"{Stamp} Client startet{Environment.NewLine}{Stamp} zweite Zeile{Environment.NewLine}",
            Text(Path.Combine(dir, "logs", "client-" + FileName)));
    }

    [Fact]
    public void ClientLine_ThroughILogger_SameBytesAsWrite()
    {
        var log = new ClientLog(dir, clock);

        #pragma warning disable CA1848 // the test writes through the plain ILogger extension on purpose
        Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(log, "Client startet");
        #pragma warning restore CA1848
        log.Flush();

        Assert.Equal($"{Stamp} Client startet{Environment.NewLine}", Text(Path.Combine(dir, "logs", "client-" + FileName)));
    }
}
