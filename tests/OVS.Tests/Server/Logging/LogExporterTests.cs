using System.IO.Compression;
using System.Text;
using OVS.Server.Logging;
using OVS.Shared.Protocol;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server.Logging;

public sealed class LogExporterTests : TempDirectoryTest
{
    const string DownloadId = "0123456789abcdef0123456789abcdef";
    const string ServerLog = "server/2025-01-01_00-00-00.log";
    static readonly Guid Raid = Guid.Parse("11111111-1111-1111-1111-111111111111");
    static readonly Guid Gone = Guid.Parse("22222222-2222-2222-2222-222222222222");
    static readonly string RaidLog = $"channels/{Raid}/2025-02-01_00-00-00.log";
    static readonly string GoneLog = $"channels/{Gone}/2025-03-01_00-00-00.log";

    /// <summary>The server's local time is two hours ahead of UTC, so the zip name shows the local date.</summary>
    sealed class PlusTwoHours : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone { get; } = TimeZoneInfo.CreateCustomTimeZone("plus2", TimeSpan.FromHours(2), "plus2", "plus2");
    }

    readonly LogReader reader;
    readonly LogExporter exporter;

    public LogExporterTests() : base("ovs-logexporter-", quiet: false)
    {
        var time = new PlusTwoHours();
        reader = new LogReader(dir, time);
        exporter = new LogExporter(dir, reader, time);
    }

    string ExportDir => Path.Combine(dir, LogExporter.ExportFolder);

    string WriteLog(string id, string text, DateTime? lastWriteUtc = null)
    {
        var path = Path.Combine(dir, "logs", id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        if (lastWriteUtc is { } at) File.SetLastWriteTimeUtc(path, at);
        return path;
    }

    void WriteThreeLogs()
    {
        // the server log starts first and is written last: the zip name needs Min of the starts and Max of the last writes
        WriteLog(ServerLog, "server alt\n", new DateTime(2025, 4, 4, 23, 0, 0, DateTimeKind.Utc));
        WriteLog(RaidLog, "2025-02-01 00:00:00.000 [Raid alt] eins\n", new DateTime(2025, 2, 1, 12, 0, 0, DateTimeKind.Utc));
        WriteLog(GoneLog, "2025-03-01 00:00:00.000 [Weg] zwei\n", new DateTime(2025, 3, 2, 23, 0, 0, DateTimeKind.Utc));
    }

    static List<(string Name, string Text)> Entries(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        return zip.Entries.Select(e =>
        {
            using var text = new StreamReader(e.Open(), Encoding.UTF8);
            return (e.FullName, text.ReadToEnd());
        }).ToList();
    }

    [Fact]
    public void RemoveExports_NoFolder_NothingCreated()
    {
        exporter.RemoveExports();
        Assert.False(Directory.Exists(ExportDir));
    }

    [Fact]
    public void RemoveExports_LeftoverExports_AllRemoved()
    {
        Directory.CreateDirectory(ExportDir);
        File.WriteAllText(Path.Combine(ExportDir, DownloadId + ".zip"), "rest");
        File.WriteAllText(Path.Combine(ExportDir, "fedcba9876543210fedcba9876543210.log"), "rest");
        exporter.RemoveExports();
        Assert.Empty(Directory.GetFiles(ExportDir));
    }

    [Fact]
    public void Export_UnknownId_NotFoundAndNothingWritten()
    {
        WriteLog(ServerLog, "server\n");
        var (export, code) = exporter.Export(DownloadId, [ServerLog, "server/2025-09-09_00-00-00.log"], new Dictionary<Guid, string>());
        Assert.Null(export);
        Assert.Equal(Codes.NotFound, code);
        Assert.False(Directory.Exists(ExportDir));
    }

    [Fact]
    public void Export_PathOutsideListing_NotFound()
    {
        WriteLog(ServerLog, "server\n");
        File.WriteAllText(Path.Combine(dir, "secret.log"), "secret\n");
        var (export, code) = exporter.Export(DownloadId, ["server/../../secret.log"], new Dictionary<Guid, string>());
        Assert.Null(export);
        Assert.Equal(Codes.NotFound, code);
        Assert.False(Directory.Exists(ExportDir));
    }

    [Fact]
    public void Export_TogetherOverMaxLogDownloadBytes_LogsTooLargeAndNothingWritten()
    {
        foreach (var id in new[] { ServerLog, "server/2025-01-02_00-00-00.log" })
        {
            var path = WriteLog(id, "");
            using var big = File.OpenWrite(path);
            big.SetLength(ProtocolInfo.MaxLogDownloadBytes / 2 + 1);
        }
        var (export, code) = exporter.Export(DownloadId, [ServerLog, "server/2025-01-02_00-00-00.log"], new Dictionary<Guid, string>());
        Assert.Null(export);
        Assert.Equal(Codes.LogsTooLarge, code);
        Assert.False(Directory.Exists(ExportDir));
    }

    [Fact]
    public void Export_OneFile_PlainLogWithItsBytes()
    {
        var bytes = Encoding.UTF8.GetBytes("2025-01-01 00:00:00.000 erste Zeile äöü\r\nzweite\n");
        var source = WriteLog(ServerLog, "");
        File.WriteAllBytes(source, bytes);

        var (export, code) = exporter.Export(DownloadId, [ServerLog], new Dictionary<Guid, string>());

        Assert.Equal("", code);
        Assert.Equal(new LogExport("2025-01-01_00-00-00.log", Path.Combine(ExportDir, DownloadId + ".log"), bytes.Length), export);
        Assert.Equal(bytes, File.ReadAllBytes(export!.Path));
    }

    [Fact]
    public void Export_Selection_ZipWithOneEntryPerFileInTheChosenOrder()
    {
        WriteThreeLogs();

        var (export, code) = exporter.Export(DownloadId, [RaidLog, ServerLog, GoneLog], new Dictionary<Guid, string> { [Raid] = "Raid", [Gone] = "Neu" });

        Assert.Equal("", code);
        Assert.Equal(Path.Combine(ExportDir, DownloadId + ".zip"), export!.Path);
        Assert.Equal(new FileInfo(export.Path).Length, export.Size);
        Assert.Equal(
        [
            ($"channels/Raid_{Raid}/2025-02-01_00-00-00.log", "2025-02-01 00:00:00.000 [Raid alt] eins\n"),
            ("server/2025-01-01_00-00-00.log", "server alt\n"),
            ($"channels/Neu_{Gone}/2025-03-01_00-00-00.log", "2025-03-01 00:00:00.000 [Weg] zwei\n"),
        ], Entries(export.Path));
    }

    [Fact]
    public void Export_DeletedChannel_FolderNamedAfterFirstLine()
    {
        WriteThreeLogs();

        var (export, _) = exporter.Export(DownloadId, [RaidLog, GoneLog], new Dictionary<Guid, string> { [Raid] = "Raid" });

        Assert.Contains($"channels/Weg_{Gone}/2025-03-01_00-00-00.log", Entries(export!.Path).Select(e => e.Name));
    }

    [Fact]
    public void Export_Selection_NamedByFirstStartAndLastWriteInLocalTime()
    {
        WriteThreeLogs(); // the last write 2025-04-04 23:00 UTC is 2025-04-05 01:00 local

        var (export, _) = exporter.Export(DownloadId, [GoneLog, ServerLog, RaidLog], new Dictionary<Guid, string>());

        Assert.Equal("ovs-logs_2025-01-01_2025-04-05.zip", export!.FileName);
    }

    [Theory]
    [InlineData("Raid/1 ..", "Raid_1")]
    [InlineData("a\\b:c*d?e\"f<g>h|i", "a_b_c_d_e_f_g_h_i")]
    [InlineData("Tab\there", "Tab_here")]
    [InlineData(" .Lobby. ", "Lobby")]
    [InlineData(" . ", "channel")]
    [InlineData("", "channel")]
    public void Export_ChannelName_SafeFolderName(string name, string folder)
    {
        WriteThreeLogs();

        var (export, _) = exporter.Export(DownloadId, [ServerLog, RaidLog], new Dictionary<Guid, string> { [Raid] = name });

        Assert.Contains($"channels/{folder}_{Raid}/2025-02-01_00-00-00.log", Entries(export!.Path).Select(e => e.Name));
    }

    [Fact]
    public void Export_EachFile_ReadHookBeforeItsCopy()
    {
        WriteThreeLogs();
        var hooked = new List<string>();
        exporter.ReadHook = id =>
        {
            hooked.Add(id);
            File.AppendAllText(Path.Combine(dir, "logs", id), "added by the hook\n"); // in the copy only if the source opens after the hook
        };

        var (export, _) = exporter.Export(DownloadId, [GoneLog, ServerLog], new Dictionary<Guid, string>());

        Assert.Equal([GoneLog, ServerLog], hooked);
        Assert.All(Entries(export!.Path), e => Assert.EndsWith("added by the hook\n", e.Text));
    }

    [Fact]
    public void Export_ReaderHookSet_ReaderHookNotCalled()
    {
        WriteThreeLogs();
        var hooked = new List<string>();
        reader.ReadHook = hooked.Add;

        var (export, _) = exporter.Export(DownloadId, [GoneLog, ServerLog], new Dictionary<Guid, string>());

        Assert.NotNull(export);
        Assert.Empty(hooked);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Export_ReadFails_ThrowsAndLeavesNoFile(int files)
    {
        WriteThreeLogs();
        string[] ids = files == 1 ? [ServerLog] : [ServerLog, RaidLog];
        exporter.ReadHook = id =>
        {
            if (id == ids[^1]) throw new IOException("locked");
        };

        Assert.Throws<IOException>(() => exporter.Export(DownloadId, ids, new Dictionary<Guid, string>()));

        Assert.Empty(Directory.GetFiles(ExportDir));
    }
}
