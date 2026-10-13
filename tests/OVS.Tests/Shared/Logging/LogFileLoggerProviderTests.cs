using System.Text;
using Microsoft.Extensions.Logging;
using OVS.Shared.Logging;
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
