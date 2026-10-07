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
}
