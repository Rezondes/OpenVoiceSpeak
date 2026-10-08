using OVS.Client.Localization;
using OVS.Client.Services;
using OVS.Client.ViewModels;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.LogAccess;
using OVS.Shared.Protocol.Messages.ServerBackups;
using OVS.Tests.TestSupport;
using static OVS.Tests.TestSupport.HangGuard;

namespace OVS.Tests.Client.Services;

/// <summary>
/// Packages 75 and 82: the administration's file transfers without a server. Requests go out and answers come back through
/// each request's route; a manual clock waits for them, so an answer that never comes times out only when a test moves it.
/// </summary>
public sealed class AdminTransferTests : TempDirectoryTest
{
    static readonly TimeSpan AnswerTimeout = TimeSpan.FromSeconds(30);
    /// <summary>1.2 MB, so the server's 512 KB chunks make three.</summary>
    static readonly byte[] Content = RandomBytes(1_200_000);
    const int ChunkSize = ProtocolLimits.BackupChunkBytes;

    readonly ManualTimeProvider time = new();
    readonly List<Request> sent = [];
    readonly List<string> notices = [];
    /// <summary>Where the answers to each request go, by its id, until the transfer removes the route.</summary>
    readonly AnswerRoutes routes = new();
    /// <summary>What an upload's route passed on after the answer.</summary>
    readonly List<Message> followUps = [];
    readonly AdminTransfer transfer;
    /// <summary>The server's answer to a request (sent with its id), applied before the send returns: the hardest order. Null: none.</summary>
    Func<Request, Message?> answer = _ => null;

    public AdminTransferTests() : base("ovs-transfer-", quiet: false) => transfer = new AdminTransfer(Send, routes.RemoveAll, notices.Add, time);

    static byte[] RandomBytes(int count)
    {
        var bytes = new byte[count];
        new Random(24).NextBytes(bytes);
        return bytes;
    }

    Task Send(Request request, Action<Message> route)
    {
        var withId = request with { RequestId = $"r{sent.Count + 1}" };
        sent.Add(withId);
        routes.Add(withId.RequestId!, route);
        if (answer(withId) is { } reply) routes.Deliver(withId.RequestId, reply);
        return Task.CompletedTask;
    }

    /// <summary>The requests whose route is still in place.</summary>
    IEnumerable<string> Routed() => sent.Select(r => r.RequestId!).Where(id => routes.Find(id) is not null);

    /// <summary>A server that has <see cref="Content"/> as backup "b.ovsbackup" and as log download "d7", in chunks of <see cref="ChunkSize"/>.</summary>
    static Message? Serve(Request r) => r switch
    {
        DownloadBackup d => new BackupChunk(r.RequestId, d.FileName, d.Offset, Content.Length, Part(d.Offset), d.Offset + ChunkSize >= Content.Length),
        PrepareLogDownload => new LogDownloadReady(r.RequestId, "d7", "ovs-logs.zip", Content.Length),
        DownloadLogChunk c => new LogChunk(r.RequestId, c.DownloadId, c.Offset, Content.Length, Part(c.Offset), c.Offset + ChunkSize >= Content.Length),
        _ => null,
    };

    static string Part(long offset) => Convert.ToBase64String(Content, (int)offset, (int)Math.Min(ChunkSize, Content.Length - offset));

    static long? ChunkOffset(Request r) => r switch
    {
        DownloadBackup d => d.Offset,
        DownloadLogChunk c => c.Offset,
        _ => null,
    };

    Task DownloadAsync(bool logs, string target) =>
        logs ? transfer.DownloadLogsAsync(["a", "b"], target) : transfer.DownloadBackupAsync("b.ovsbackup", target);

    string Target(string name = "target.bin") => Path.Combine(dir, name);

    // ---- Downloads ----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Download_ChunksAnswered_FileInPlaceWithProgress(bool logs)
    {
        var target = Target();
        var format = logs ? Strings.Logs_Downloading : Strings.Backup_Downloading;
        var seen = new List<(bool Running, double Percent, string Text, bool PartExists, bool TargetExists)>();
        answer = r =>
        {
            if (ChunkOffset(r) is not null)
                seen.Add((transfer.IsRunning, transfer.Percent, transfer.ProgressText, File.Exists(target + ".part"), File.Exists(target)));
            return Serve(r);
        };

        await Bound(DownloadAsync(logs, target));

        Assert.Equal(Content, File.ReadAllBytes(target));
        Assert.Equal([0L, 524_288L, 1_048_576L], sent.Select(ChunkOffset).OfType<long>());
        Assert.Equal([0d, 43.7, 87.4], seen.Select(s => Math.Round(s.Percent, 1)));
        Assert.Equal(string.Format(format, 44), seen[1].Text);
        Assert.All(seen, s => Assert.True(s.Running && s.PartExists && !s.TargetExists));
        Assert.Equal(100d, transfer.Percent);
        Assert.Equal(string.Format(format, 100), transfer.ProgressText);
        Assert.False(transfer.IsRunning);
        Assert.Equal([target], Directory.GetFiles(dir));
        Assert.Empty(notices);
        if (logs)
        {
            Assert.Equal(["a", "b"], Assert.IsType<PrepareLogDownload>(sent[0]).FileIds);
            Assert.All(sent.OfType<DownloadLogChunk>(), c => Assert.Equal("d7", c.DownloadId));
        }
        else Assert.All(sent, r => Assert.Equal("b.ovsbackup", Assert.IsType<DownloadBackup>(r).FileName));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Download_ErrorForASecondChunk_OldFileKeptWithoutNotice(bool logs)
    {
        var target = Target();
        File.WriteAllText(target, "alt");
        answer = r => ChunkOffset(r) is 0 or null ? Serve(r) : new Error(r.RequestId, ReasonCodes.NotFound); // the server shows its own

        await Bound(DownloadAsync(logs, target));

        Assert.Equal([0L, 524_288L], sent.Select(ChunkOffset).OfType<long>());
        Assert.Equal("alt", File.ReadAllText(target));
        Assert.Equal([target], Directory.GetFiles(dir));
        Assert.Empty(notices);
        Assert.False(transfer.IsRunning);
    }

    [Theory]
    [InlineData("backup chunk")]
    [InlineData("log preparation")]
    [InlineData("log chunk")]
    public async Task Download_NoAnswerFor30Seconds_StopsWithItsNotice(string unanswered)
    {
        bool logs = unanswered != "backup chunk";
        answer = r => unanswered == "log chunk" && r is PrepareLogDownload ? Serve(r) : null;

        var download = DownloadAsync(logs, Target());
        time.Advance(AnswerTimeout - TimeSpan.FromTicks(1));
        Assert.False(download.IsCompleted);
        Assert.True(transfer.IsRunning);
        Assert.Empty(notices);
        time.Advance(TimeSpan.FromTicks(1));
        await Bound(download);

        Assert.Equal([logs ? Strings.Logs_TransferFailed : Strings.Backup_TransferFailed], notices);
        Assert.False(transfer.IsRunning);
        Assert.Empty(Directory.GetFiles(dir));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Download_ChunkNotBase64_StopsWithItsNoticeAndOldFileKept(bool logs)
    {
        var target = Target();
        File.WriteAllText(target, "alt");
        answer = r => Serve(r) switch
        {
            BackupChunk c => c with { DataBase64 = "%%%" },
            LogChunk c => c with { DataBase64 = "%%%" },
            var other => other,
        };

        await Bound(DownloadAsync(logs, target));

        Assert.Equal([logs ? Strings.Logs_TransferFailed : Strings.Backup_TransferFailed], notices);
        Assert.Equal("alt", File.ReadAllText(target));
        Assert.Equal([target], Directory.GetFiles(dir));
        Assert.False(transfer.IsRunning);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Download_TargetFolderMissing_StopsWithItsNotice(bool logs)
    {
        answer = Serve;

        await Bound(DownloadAsync(logs, Path.Combine(dir, "missing", "target.bin")));

        Assert.Equal([logs ? Strings.Logs_TransferFailed : Strings.Backup_TransferFailed], notices);
        Assert.Empty(sent.Select(ChunkOffset).OfType<long>());
        Assert.False(transfer.IsRunning);
    }

    [Fact]
    public async Task DownloadLogsAsync_NoFiles_SendsNothing()
    {
        await Bound(transfer.DownloadLogsAsync([], Target()));

        Assert.Empty(sent);
        Assert.Equal("", transfer.ProgressText);
        Assert.False(transfer.IsRunning);
    }

    // ---- One at a time, and the page closing ----

    [Fact]
    public async Task AnyTransfer_AnotherRunning_ReturnsWithoutSending()
    {
        var upload = Target("up.ovsbackup");
        File.WriteAllBytes(upload, [1, 2, 3]);
        var running = transfer.DownloadBackupAsync("b.ovsbackup", Target());

        var backup = transfer.DownloadBackupAsync("c.ovsbackup", Target("c.bin"));
        var logs = transfer.DownloadLogsAsync(["a"], Target("l.zip"));
        var uploaded = transfer.UploadBackupAsync(upload, followUps.Add);

        Assert.True(backup.IsCompleted && logs.IsCompleted && uploaded.IsCompleted);
        Assert.Null(await uploaded);
        Assert.Single(sent);
        Assert.True(transfer.IsRunning);
        Assert.Equal(string.Format(Strings.Backup_Downloading, 0), transfer.ProgressText);
        transfer.Cancel();
        await Bound(running);
    }

    [Fact]
    public async Task Cancel_AnswerAwaited_StopsWithoutNotice()
    {
        var target = Target();
        File.WriteAllText(target, "alt");
        var download = transfer.DownloadBackupAsync("b.ovsbackup", target);

        transfer.Cancel();
        await Bound(download);

        Assert.Empty(notices);
        Assert.False(transfer.IsRunning);
        Assert.Equal("alt", File.ReadAllText(target));
        Assert.Equal([target], Directory.GetFiles(dir));
    }

    [Fact]
    public async Task Cancel_WhileSending_StopsWithoutRouteOrNotice()
    {
        answer = _ => { transfer.Cancel(); return null; }; // the page closes while the request goes out

        await Bound(transfer.DownloadBackupAsync("b.ovsbackup", Target()));

        Assert.Empty(Routed());
        Assert.Empty(notices);
        Assert.False(transfer.IsRunning);
    }

    /// <summary>Each request has its own route, until the next request or Cancel.</summary>
    [Fact]
    public async Task Download_ChunksAnswered_OnlyTheLastRequestKeepsItsRoute()
    {
        answer = Serve;

        await Bound(transfer.DownloadBackupAsync("b.ovsbackup", Target()));

        Assert.Equal(["r3"], Routed());
    }

    [Fact]
    public async Task Cancel_AfterADownload_RemovesTheLastRoute()
    {
        answer = Serve;
        await Bound(transfer.DownloadBackupAsync("b.ovsbackup", Target()));

        transfer.Cancel();

        Assert.Empty(Routed());
    }

    // ---- Upload ----

    [Fact]
    public async Task UploadBackupAsync_ChunksAcknowledged_ReturnsTheStoredBackupWithProgress()
    {
        var content = RandomBytes(1_100_000);
        var path = Target("up.ovsbackup");
        File.WriteAllBytes(path, content);
        var stored = new BackupInfo("hochgeladen_2026-01-01_12-00-00.ovsbackup", time.GetUtcNow(), content.Length, "dev.0000");
        var received = new MemoryStream();
        var seen = new List<(bool Running, double Percent)>();
        answer = r =>
        {
            var c = Assert.IsType<UploadBackupChunk>(r);
            seen.Add((transfer.IsRunning, transfer.Percent));
            received.Write(Convert.FromBase64String(c.DataBase64));
            return c.IsLast ? new BackupUploaded(r.RequestId, stored) : new UploadBackupAck(r.RequestId, c.UploadId, received.Length);
        };

        Assert.Same(stored, await Bound(transfer.UploadBackupAsync(path, followUps.Add)));

        var chunks = sent.Cast<UploadBackupChunk>().ToList();
        Assert.Equal(content, received.ToArray());
        Assert.Equal([0L, 524_288L, 1_048_576L], chunks.Select(c => c.Offset));
        Assert.Equal([false, false, true], chunks.Select(c => c.IsLast));
        Assert.Matches("^[0-9a-f]{32}$", Assert.Single(chunks.Select(c => c.UploadId).Distinct()));
        Assert.Equal([0d, 47.7, 95.3], seen.Select(s => Math.Round(s.Percent, 1)));
        Assert.All(seen, s => Assert.True(s.Running));
        Assert.Equal(100d, transfer.Percent);
        Assert.Equal(string.Format(Strings.Backup_Uploading, 100), transfer.ProgressText);
        Assert.False(transfer.IsRunning);
        Assert.Empty(notices);
        Assert.Empty(followUps);
    }

    [Fact]
    public async Task UploadBackupAsync_ListAfterTheStoredBackup_GoesToTheFollowUp()
    {
        var path = Target("up.ovsbackup");
        File.WriteAllBytes(path, [1, 2, 3]);
        var stored = new BackupInfo("hochgeladen_2026-01-01_12-00-00.ovsbackup", time.GetUtcNow(), 3, "dev.0000");
        answer = r => new BackupUploaded(r.RequestId, stored);
        Assert.Same(stored, await Bound(transfer.UploadBackupAsync(path, followUps.Add)));

        var list = new BackupList("r1", [stored]);
        routes.Deliver("r1", list); // the server sends the new list right after the stored backup

        Assert.Same(list, Assert.Single(followUps));
    }

    [Fact]
    public async Task UploadBackupAsync_FirstChunkRefused_NullWithoutNotice()
    {
        var path = Target("up.ovsbackup");
        File.WriteAllBytes(path, new byte[ProtocolLimits.BackupChunkBytes + 1]);
        answer = r => new Error(r.RequestId, ReasonCodes.InvalidBackup); // the server shows its own

        Assert.Null(await Bound(transfer.UploadBackupAsync(path, followUps.Add)));

        Assert.Single(sent);
        Assert.Empty(notices);
        Assert.False(transfer.IsRunning);
    }

    [Theory]
    [InlineData(0L, false)]
    [InlineData(1L, true)]
    public async Task UploadBackupAsync_FileSize_RefusedOnlyOverTheLimit(long over, bool refused)
    {
        var path = Target("big.ovsbackup");
        using (var file = File.Create(path)) file.SetLength(ProtocolLimits.MaxBackupUploadBytes + over);
        answer = r => new Error(r.RequestId, ReasonCodes.InvalidBackup); // ends an upload after its first chunk

        Assert.Null(await Bound(transfer.UploadBackupAsync(path, followUps.Add)));

        Assert.Equal(refused ? 0 : 1, sent.Count);
        string[] expected = refused ? [ErrorTexts.For(ReasonCodes.BackupTooLarge)] : [];
        Assert.Equal(expected, notices);
        Assert.False(transfer.IsRunning);
    }

    [Fact]
    public async Task UploadBackupAsync_FileMissing_NullWithNotice()
    {
        Assert.Null(await Bound(transfer.UploadBackupAsync(Target("missing.ovsbackup"), followUps.Add)));

        Assert.Empty(sent);
        Assert.Equal([Strings.Backup_TransferFailed], notices);
        Assert.False(transfer.IsRunning);
    }

    // ---- The administration page: it passes the answers on, stops the transfer when closed and shares it with the logs tab ----

    [Fact]
    public async Task AdminPage_AnswerAfterTheRequest_CompletesTheDownload()
    {
        var pageSent = new List<Request>();
        var server = FakeServers.Admin(pageSent, time);
        var page = new AdminViewModel(server);
        var target = Target();
        var download = page.Transfer.DownloadBackupAsync("b.ovsbackup", target);

        var request = pageSent.OfType<DownloadBackup>().Single();
        server.Apply(new BackupChunk(request.RequestId, "b.ovsbackup", 0, Content.Length, Convert.ToBase64String(Content), true));
        await Bound(download);

        Assert.Equal(Content, File.ReadAllBytes(target));
        Assert.False(page.Transfer.IsRunning);
    }

    [Fact]
    public async Task AdminPage_Detached_WaitingTransferStopsWithoutNotice()
    {
        var server = FakeServers.Admin(time: time);
        server.Notice += notices.Add;
        var page = new AdminViewModel(server);
        var download = page.Transfer.DownloadBackupAsync("b.ovsbackup", Target());

        page.Detach();
        await Bound(download);

        Assert.False(page.Transfer.IsRunning);
        Assert.Empty(notices);
        Assert.Empty(Directory.GetFiles(dir));
    }

    [Fact]
    public void AdminPage_LogsTab_SharesTheOneTransfer()
    {
        var page = new AdminViewModel(FakeServers.Admin(time: time));

        Assert.Same(page.Transfer, page.Logs.Transfer);
    }
}
