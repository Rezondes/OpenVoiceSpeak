using System.Text;
using Microsoft.Extensions.Logging;
using OVS.Server.Logging;
using OVS.Shared.Logging;
using OVS.Shared.Permissions;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Shared.Logging;

/// <summary>Step 30d3: the one ILoggerProvider writes the log files in the format the golden tests pin.</summary>
public sealed class LogFileLoggerProviderTests() : TempDirectoryTest("ovs-provider-", quiet: false)
{
    readonly ManualTimeProvider clock = new(new DateTimeOffset(2026, 10, 13, 14, 5, 9, 123, TimeSpan.Zero)) { Local = TimeZoneInfo.Utc };
    readonly List<string> console = [];

    const string Stamp = "2026-10-13 14:05:09.123";
    const string FileName = "2026-10-13_14-05-09.log";

    LogFileLoggerProvider Provider() => new(new LogFileWriter(dir, 0, clock, console.Add), console.Add);

    string Text(params string[] path) => File.ReadAllText(Path.Combine([dir, .. path]), Encoding.UTF8);

    [Fact]
    public void Log_ServerCategory_FileAndConsole()
    {
        using var provider = Provider();
        #pragma warning disable CA1848 // the test writes through the plain ILogger extension on purpose
        provider.CreateLogger(LogFileLoggerProvider.ServerCategory).LogInformation("Server startet");
        #pragma warning restore CA1848

        Assert.Equal($"{Stamp} Server startet{Environment.NewLine}", Text("server", FileName));
        Assert.Equal([$"{Stamp} Server startet"], console);
    }

    /// <summary>
    /// Package 165: an administrative line carries the audit marker and its actor (the moderator of a moderation line)
    /// after the time stamp, readable back with <see cref="AuditLine.Parse"/>; every other line is written as before.
    /// </summary>
    [Fact]
    public void AuditEvent_MarkerAdded_OtherLinesUnchanged()
    {
        using var provider = Provider();
        var server = provider.CreateLogger(LogFileLoggerProvider.ServerCategory);
        const string Actor = "chef \"der\" Große [1]";
        server.GroupCreated("Team", Actor, Permission.Speak);
        server.Kicked("anna", "mod", "Spam");
        server.Connected("anna", "fp", System.Net.IPAddress.Loopback);
        #pragma warning disable CA1848 // the test writes through the plain ILogger extension on purpose
        server.LogInformation("Server startet");
        #pragma warning restore CA1848

        var lines = Text("server", FileName).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal($"{Stamp} [audit] \"chef \\\"der\\\" Große [1]\" Group 'Team' created by {Actor} (Speak)", lines[0]);
        Assert.Equal(new AuditLine(Stamp, Actor, $"Group 'Team' created by {Actor} (Speak)"), AuditLine.Parse(lines[0]));
        Assert.Equal(new AuditLine(Stamp, "mod", "anna was kicked by mod: Spam"), AuditLine.Parse(lines[1])); // the moderator acted
        Assert.Equal($"{Stamp} anna connected (fp, 127.0.0.1)", lines[2]);
        Assert.Equal($"{Stamp} Server startet", lines[3]);
        Assert.Null(AuditLine.Parse(lines[2]));
        Assert.Null(AuditLine.Parse($"{Stamp} [audit] \"cut"));
        Assert.Equal(lines, console);
    }

    /// <summary>Package 143 (A153): a line logged with an exception carries its text and stack trace, still on one line.</summary>
    [Fact]
    public void Exception_AppendedOnOneLine_OtherLinesUnchanged()
    {
        using var provider = Provider();
        var server = provider.CreateLogger(LogFileLoggerProvider.ServerCategory);
        InvalidOperationException thrown;
        try
        {
            throw new InvalidOperationException("kaputt");
        }
        catch (InvalidOperationException exception)
        {
            thrown = exception;
        }

        server.Log(LogLevel.Error, default, "Fehler abc", thrown, (text, _) => text);
        server.Log(LogLevel.Information, default, "Server startet", null, (text, _) => text);

        var lines = Text("server", FileName).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.StartsWith($"{Stamp} Fehler abc System.InvalidOperationException: kaputt", lines[0], StringComparison.Ordinal);
        Assert.Contains(@"\n   at ", lines[0], StringComparison.Ordinal); // the trace's line breaks are escaped
        Assert.Contains(nameof(Exception_AppendedOnOneLine_OtherLinesUnchanged), lines[0], StringComparison.Ordinal);
        Assert.Equal($"{Stamp} Server startet", lines[1]);
        Assert.Equal(lines, console);
    }

    [Fact]
    public void Log_ConsoleOnlyOrFileOnlyEvent_GoesOnlyThere()
    {
        using var provider = Provider();
        var server = provider.CreateLogger(LogFileLoggerProvider.ServerCategory);

        server.Log(LogLevel.Information, LogFileLoggerProvider.ConsoleOnly, "Admin-Token: geheim", null, (text, _) => text);
        server.Log(LogLevel.Information, LogFileLoggerProvider.FileOnly, "nur in die Datei", null, (text, _) => text);

        Assert.Equal($"{Stamp} nur in die Datei{Environment.NewLine}", Text("server", FileName));
        Assert.Equal([$"{Stamp} Admin-Token: geheim"], console);
    }

    /// <summary>Package 148: the console category writes the console alone, never a file.</summary>
    [Fact]
    public void Log_ConsoleCategory_OnlyTheConsole()
    {
        using var provider = Provider();
        provider.CreateLogger(LogFileLoggerProvider.ConsoleCategory).Log(LogLevel.Information, default, "Admin-Token: geheim", null, (text, _) => text);

        Assert.Equal([$"{Stamp} Admin-Token: geheim"], console);
        Assert.False(Directory.Exists(Path.Combine(dir, "server")) && Directory.EnumerateFiles(Path.Combine(dir, "server")).Any(path => File.ReadAllText(path).Length > 0));
    }

    [Fact]
    public void Log_ChannelCategory_OwnFolderNoConsole()
    {
        var channel = Guid.Parse("11111111-2222-3333-4444-555555555555");

        using var provider = Provider();
        #pragma warning disable CA1848 // the test writes through the plain ILogger extension on purpose
        provider.CreateLogger(LogFileLoggerProvider.ChannelCategory(channel)).LogInformation("[Raid] anna betritt den Channel");
        #pragma warning restore CA1848

        Assert.Equal($"{Stamp} [Raid] anna betritt den Channel{Environment.NewLine}", Text("channels", channel.ToString(), FileName));
        Assert.Empty(console);
    }

    [Fact]
    public void Log_OtherCategory_Nothing()
    {
        using var provider = Provider();
        var logger = provider.CreateLogger("Microsoft.Hosting.Lifetime");

        #pragma warning disable CA1848 // the test writes through the plain ILogger extension on purpose
        logger.LogInformation("Application started");
        #pragma warning restore CA1848

        Assert.False(logger.IsEnabled(LogLevel.Information));
        Assert.False(Directory.EnumerateFiles(dir, "*.log", SearchOption.AllDirectories).Any());
        Assert.Empty(console);
    }
}
