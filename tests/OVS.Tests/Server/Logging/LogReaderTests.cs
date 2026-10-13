using OVS.Server;
using OVS.Server.Logging;
using OVS.Shared.Protocol.Messages.LogAccess;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server.Logging;

/// <summary>The log reader's search, on the reader's clock.</summary>
public sealed class LogReaderTests() : TempDirectoryTest("ovs-logreader-", quiet: false)
{
    /// <summary>Package 81: a search stops with what it found once its time limit has passed on the clock.</summary>
    [Fact]
    public void Search_LimitPassedOnTheClock_StopsAsTimedOut()
    {
        var path = Path.Combine(dir, "logs", "server", "2025-01-01_00-00-00.log");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, Enumerable.Repeat("2025-01-01 00:00:00.000 nothing to find here", 2048)); // checked every 1024 lines
        var time = new ManualTimeProvider();
        var reader = new LogReader(dir, time) { ReadHook = _ => time.Advance(ServerTuning.LogSearchTimeout + TimeSpan.FromSeconds(1)) };

        var result = reader.Search(null, new SearchLogs("anything"));

        Assert.True(result.TimedOut);
    }

    /// <summary>Package 143 (A153): the admin finds a failed request's line by the error ID the user copied.</summary>
    [Fact]
    public void Search_ErrorId_FindsTheLine()
    {
        var time = new ManualTimeProvider();
        using var logs = new ServerLogWriter(dir, 0, time, _ => { });
        var errorId = ErrorIds.New();
        InvalidOperationException thrown;
        try
        {
            throw new InvalidOperationException("Testfehler");
        }
        catch (InvalidOperationException exception)
        {
            thrown = exception;
        }
        logs.Server.RequestFailed("CreateChannel", "anna", errorId, thrown);
        logs.Server.ServerChat("anna", "nichts davon");

        var result = new LogReader(dir, time).Search(null, new SearchLogs(errorId));

        var hit = Assert.Single(result.Hits);
        Assert.Contains("Testfehler", hit.Text, StringComparison.Ordinal);
    }
}
