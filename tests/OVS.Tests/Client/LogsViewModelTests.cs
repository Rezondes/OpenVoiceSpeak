using OVS.Client.Net;
using OVS.Client.ViewModels;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client;

/// <summary>Package 81: the Logs tab of the administration.</summary>
public class LogsViewModelTests
{
    static readonly Guid Lobby = Guid.NewGuid();
    static readonly Guid Raid = Guid.NewGuid();
    static readonly Guid Deleted = Guid.NewGuid();

    static DateTimeOffset Day(int day, int hour = 12) => new(2026, 1, day, hour, 0, 0, TimeSpan.Zero);

    static readonly LogFileInfo ServerFile = new("server/2026-01-10_12-00-00.log", LogKind.Server, null, null, Day(10), Day(10, 18), 2048);
    static readonly LogFileInfo RaidFile = new($"channels/{Raid}/2026-01-05_12-00-00.log", LogKind.Channel, Raid, "Raid", Day(5), Day(5, 18), 900);
    static readonly LogFileInfo OldFile = new($"channels/{Deleted}/2026-01-01_12-00-00.log", LogKind.Channel, Deleted, "Alt", Day(1), Day(1, 18), 300);
    static readonly LogFileInfo OldServerFile = new("server/2026-01-01_12-00-00.log", LogKind.Server, null, null, Day(1), Day(2, 18), 5_000_000);

    /// <summary>A server that answers ReadLog with 3 pages of numbered lines ("Zeile n", every 7th line "FEHLER"), and SearchLogs with the given result.</summary>
    static (LogsViewModel Logs, List<Request> Sent) Create(LogSearchResult? searchResult = null, Permission perms = Permission.LogsView,
        Func<Request, Message?>? reply = null, ManualTimeProvider? time = null)
    {
        var snapshot = new ServerSnapshot(new ServerSettingsInfo("Server", "", false), Lobby,
            [new ChannelInfo(Lobby, "Lobby", "", 0)], [], [new GroupInfo(WellKnownGroups.Admin, "Admin", Permission.All)],
            [new UserInfo(1, "fp1", "ich", Lobby, false, false, false, perms, [])]);
        var sent = new List<Request>();
        ServerViewModel? server = null;
        server = new ServerViewModel(new StateMirror(new Welcome(1, "", snapshot)), r =>
        {
            sent.Add(r);
            Message? answer = reply?.Invoke(r) ?? r switch
            {
                ListLogs => new LogList(r.RequestId, [ServerFile, RaidFile, OldServerFile, OldFile]),
                ReadLog read => Page(read),
                SearchLogs => searchResult,
                _ => null,
            };
            if (answer is not null) server!.Apply(answer);
            return Task.CompletedTask;
        }, time ?? new ManualTimeProvider());
        var admin = new AdminViewModel(server);
        _ = admin.RequestListsAsync(); // completes at once: the fake server answers synchronously
        return (admin.Logs, sent);
    }

    static LogPage Page(ReadLog read)
    {
        int page = read.Page is { } p && p <= 3 ? p : 3;
        int first = (page - 1) * 1000 + 1;
        int count = page == 3 ? 500 : 1000;
        return new LogPage(read.RequestId, read.FileId, page, 3, first,
            Enumerable.Range(first, count).Select(n => n % 7 == 0 ? $"Zeile {n} FEHLER beim Senden" : $"Zeile {n} ok").ToList());
    }

    [Fact]
    public void FileFilter_TypeAndPeriod()
    {
        var (logs, _) = Create();
        Assert.Equal([ServerFile.Id, RaidFile.Id, OldServerFile.Id, OldFile.Id], logs.Files.Select(f => f.Info.Id));
        Assert.Equal(["Server", "Raid", "Server", "Alt"], logs.Files.Select(f => f.Title));
        Assert.Contains("MB", logs.Files[2].Details);
        // all, server, then every channel of the listing by name
        Assert.Equal(["Alle Logs", "Server", "Alt", "Raid"], logs.Sources.Select(s => s.Label));

        logs.SelectedSource = logs.Sources[1];
        Assert.Equal([ServerFile.Id, OldServerFile.Id], logs.Files.Select(f => f.Info.Id));
        logs.SelectedSource = logs.Sources.Single(s => s.Label == "Raid");
        Assert.Equal([RaidFile.Id], logs.Files.Select(f => f.Info.Id));

        // a period keeps the files that overlap it
        logs.SelectedSource = logs.Sources[0];
        logs.FromDate = new DateTime(2026, 1, 2);
        Assert.Equal([ServerFile.Id, RaidFile.Id, OldServerFile.Id], logs.Files.Select(f => f.Info.Id));
        logs.ToDate = new DateTime(2026, 1, 6);
        Assert.Equal([RaidFile.Id, OldServerFile.Id], logs.Files.Select(f => f.Info.Id));
        Assert.Equal("2 von 4 Dateien", logs.FileCountText);
        logs.FromDate = new DateTime(2026, 2, 1);
        logs.ToDate = null;
        Assert.True(logs.HasNoFileMatches);
    }

    [Fact]
    public void OpenFile_PageNavigation_Refresh()
    {
        var (logs, sent) = Create();
        Assert.False(logs.IsFileOpen);
        logs.OpenCommand.Execute(logs.Files[0]);
        Assert.Equal(new ReadLog(ServerFile.Id, null), Assert.IsType<ReadLog>(sent[^1]) with { RequestId = null });
        Assert.True(logs.IsFileOpen);
        Assert.Equal((3, 3), (logs.Page, logs.PageCount));
        Assert.Equal(500, logs.Lines.Count);
        Assert.Equal((2001, "Zeile 2001 ok"), (logs.Lines[0].Number, logs.Lines[0].Text));
        Assert.Equal("Seite 3 von 3", logs.PageText);
        Assert.False(logs.NewerCommand.CanExecute(null));
        Assert.False(logs.LastCommand.CanExecute(null));

        logs.OlderCommand.Execute(null);
        Assert.Equal(2, ((ReadLog)sent[^1]).Page);
        Assert.Equal((2, 1001), (logs.Page, logs.Lines[0].Number));
        Assert.True(logs.NewerCommand.CanExecute(null));
        logs.FirstCommand.Execute(null);
        Assert.Equal(1, ((ReadLog)sent[^1]).Page);
        Assert.False(logs.OlderCommand.CanExecute(null));
        Assert.False(logs.FirstCommand.CanExecute(null));
        // refresh keeps the page; on the last page it asks for the last one again, so new lines and pages show
        logs.ReloadCommand.Execute(null);
        Assert.Equal(new ReadLog(ServerFile.Id, 1), ((ReadLog)sent[^1]) with { RequestId = null });
        logs.LastCommand.Execute(null);
        Assert.Null(((ReadLog)sent[^1]).Page);
        logs.ReloadCommand.Execute(null);
        Assert.Null(((ReadLog)sent[^1]).Page);

        // a page of another file (an old answer) is ignored
        var before = logs.Lines[0];
        logs.Apply(new LogPage(null, RaidFile.Id, 1, 1, 1, ["fremd"]));
        Assert.Same(before, logs.Lines[0]);
        logs.CloseFileCommand.Execute(null);
        Assert.False(logs.IsFileOpen);
    }

    [Fact]
    public void LineFilter_HighlightsMatches()
    {
        var (logs, _) = Create();
        logs.OpenCommand.Execute(logs.Files[0]);
        logs.LineFilter = "fehler";
        Assert.Equal(Enumerable.Range(2001, 500).Where(n => n % 7 == 0), logs.Lines.Select(l => l.Number));
        var line = logs.Lines[0];
        Assert.Equal([("Zeile 2002 ", false), ("FEHLER", true), (" beim Senden", false)], line.Segments.Select(s => (s.Text, s.IsMatch)));
        Assert.True(logs.HasLines);

        logs.LineFilter = "gibt es nicht";
        Assert.Empty(logs.Lines);
        Assert.False(logs.HasLines);
        logs.LineFilter = "";
        Assert.Equal(500, logs.Lines.Count);
        Assert.Equal([("Zeile 2001 ok", false)], logs.Lines[0].Segments.Select(s => (s.Text, s.IsMatch)));
        // the filter stays when paging
        logs.LineFilter = "FEHLER";
        logs.OlderCommand.Execute(null);
        Assert.All(logs.Lines, l => Assert.Contains("FEHLER", l.Text));
    }

    [Fact]
    public void SearchHit_OpensFileAtLine()
    {
        var result = new LogSearchResult(null, [new LogHit(ServerFile.Id, 1500, "Zeile 1500 FEHLER"), new LogHit(RaidFile.Id, 3, "Raid FEHLER")], true, false);
        var (logs, sent) = Create(result);
        logs.SelectedSource = logs.Sources[1];
        logs.FromDate = new DateTime(2026, 1, 3);
        logs.SearchText = "fehler";
        logs.SearchCommand.Execute(null);
        var search = Assert.IsType<SearchLogs>(sent[^1]);
        Assert.Equal(("fehler", LogKind.Server, (Guid?)null), (search.Query, search.Kind, search.ChannelId));
        Assert.Equal(new DateTimeOffset(new DateTime(2026, 1, 3)), search.From);
        Assert.Null(search.To);

        Assert.True(logs.ShowHits);
        Assert.Equal(2, logs.Hits.Count);
        Assert.Equal(("Server", 1500), (logs.Hits[0].Title, logs.Hits[0].Line));
        Assert.Equal("Raid", logs.Hits[1].Title);
        Assert.Contains("500", logs.HitsText); // more than the shown ones

        logs.OpenHitCommand.Execute(logs.Hits[0]);
        Assert.Equal(new ReadLog(ServerFile.Id, 2), ((ReadLog)sent[^1]) with { RequestId = null });
        Assert.Equal(ServerFile.Id, logs.OpenFile?.Info.Id);
        Assert.Equal(1500, logs.SelectedLine?.Number);
        Assert.True(logs.SelectedLine!.IsTarget);
        Assert.Single(logs.Lines, l => l.IsTarget);

        logs.ClearSearchCommand.Execute(null);
        Assert.False(logs.ShowHits);
        Assert.Empty(logs.Hits);
    }

    // ---- Package 82: download ----

    [Fact]
    public void Selection_AllInPeriod_None_SingleVsZip()
    {
        var (viewer, _) = Create();
        Assert.False(viewer.ShowDownload); // LogsView alone: no checkboxes, no download
        Assert.All(viewer.Files, f => Assert.False(f.CanSelect));

        var (logs, _) = Create(perms: Permission.LogsView | Permission.LogsDownload);
        Assert.True(logs.ShowDownload);
        Assert.All(logs.Files, f => Assert.True(f.CanSelect));
        Assert.False(logs.CanDownload);
        logs.Files[0].IsSelected = true;
        Assert.True(logs.CanDownload);
        Assert.False(logs.IsZipDownload);
        Assert.Equal("2026-01-10_12-00-00.log", logs.DownloadName);
        Assert.Equal("1 ausgewählt", logs.SelectionText);

        // "Alle im Zeitraum" adds what the filters show; the selection survives filtering and a new list
        logs.FromDate = new DateTime(2026, 1, 2);
        logs.ToDate = new DateTime(2026, 1, 6);
        logs.SelectAllInPeriodCommand.Execute(null);
        Assert.Equal([RaidFile.Id, OldServerFile.Id], logs.Files.Where(f => f.IsSelected).Select(f => f.Info.Id));
        logs.FromDate = logs.ToDate = null;
        Assert.Equal([ServerFile.Id, RaidFile.Id, OldServerFile.Id], logs.Files.Where(f => f.IsSelected).Select(f => f.Info.Id));
        logs.Apply(new LogList(null, [ServerFile, RaidFile, OldServerFile, OldFile]));
        Assert.Equal(3, logs.Files.Count(f => f.IsSelected));
        Assert.True(logs.IsZipDownload);
        Assert.Equal("ovs-logs_2026-01-01_2026-01-10.zip", logs.DownloadName);
        Assert.Equal([ServerFile.Id, RaidFile.Id, OldServerFile.Id], logs.SelectedIds);

        logs.SelectNoneCommand.Execute(null);
        Assert.DoesNotContain(logs.Files, f => f.IsSelected);
        Assert.False(logs.CanDownload);
    }

    [Fact]
    public async Task Download_PartFileMovedAtEnd_Progress()
    {
        var dir = Directory.CreateTempSubdirectory("ovs-logdownload-").FullName;
        try
        {
            var zip = new byte[1_200_000];
            Random.Shared.NextBytes(zip);
            string? target = null;
            bool fail = false;
            LogsViewModel? logs = null;
            var progress = new List<(bool Busy, double Percent, bool PartExists, bool TargetExists)>();
            Message? Reply(Request r)
            {
                switch (r)
                {
                    case PrepareLogDownload p:
                        Assert.Equal([ServerFile.Id, RaidFile.Id], p.FileIds);
                        return new LogDownloadReady(r.RequestId, "d1", "ovs-logs.zip", zip.Length);
                    case DownloadLogChunk c:
                        Assert.Equal("d1", c.DownloadId);
                        progress.Add((logs!.Owner!.IsTransferring, logs.Owner.TransferPercent, File.Exists(target + ".part"), File.Exists(target)));
                        if (fail && c.Offset > 0) return new Error(r.RequestId, Codes.NotFound);
                        var size = (int)Math.Min(ProtocolInfo.BackupChunkBytes, zip.Length - c.Offset);
                        return new LogChunk(r.RequestId, "d1", c.Offset, zip.Length, Convert.ToBase64String(zip, (int)c.Offset, size), c.Offset + size >= zip.Length);
                    default:
                        return null;
                }
            }
            var (created, sent) = Create(perms: Permission.LogsView | Permission.LogsDownload, reply: Reply);
            logs = created;
            logs.Files[0].IsSelected = logs.Files[1].IsSelected = true;
            target = Path.Combine(dir, "logs.zip");

            await logs.DownloadAsync(target);
            Assert.Equal(zip, File.ReadAllBytes(target));
            Assert.Equal([0L, 524_288L, 1_048_576L], sent.OfType<DownloadLogChunk>().Select(c => c.Offset));
            Assert.Equal([0d, 43.7, 87.4], progress.Select(p => Math.Round(p.Percent, 1)));
            Assert.All(progress, p => Assert.True(p.Busy && !p.TargetExists));
            Assert.All(progress.Skip(1), p => Assert.True(p.PartExists));
            Assert.Equal(string.Format(OVS.Client.Localization.Strings.Logs_Downloading, 100), logs.Owner!.TransferText);
            Assert.False(logs.Owner.IsTransferring);
            Assert.Equal([target], Directory.GetFiles(dir));

            // a failed chunk leaves the file there before untouched and no half file
            File.WriteAllText(target, "alt");
            fail = true;
            await logs.DownloadAsync(target);
            Assert.Equal("alt", File.ReadAllText(target));
            Assert.Equal([target], Directory.GetFiles(dir));
            Assert.False(logs.Owner.IsTransferring);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>Package 87 (AC4): the file list arrives in pages and is shown once complete.</summary>
    [Fact]
    public void FileList_FetchesAllPages()
    {
        LogFileInfo[] files = [ServerFile, RaidFile, OldServerFile, OldFile];
        var (logs, sent) = Create(reply: r => r is ListLogs l ? new LogList(r.RequestId, files.Skip(l.Offset).Take(3).ToList(), l.Offset, files.Length) : null);
        Assert.Equal([0, 3], sent.OfType<ListLogs>().Select(l => l.Offset));
        Assert.Equal(4, logs.Files.Count);
    }

    /// <summary>Package 97 (AC4): the search is busy (spinner, button disabled) until its result arrives.</summary>
    [Fact]
    public void Search_BusyUntilResult()
    {
        var time = new ManualTimeProvider();
        var (logs, sent) = Create(time: time); // no search result: the server answers when told below
        logs.SearchText = "fehler";
        logs.SearchCommand.Execute(null);
        Assert.True(logs.Searching.IsRunning);
        time.Advance(TimeSpan.FromMilliseconds(149));
        Assert.False(logs.Searching.IsBusy);
        Assert.True(logs.SearchCommand.CanExecute(null));
        time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(logs.Searching.IsBusy);
        Assert.False(logs.SearchCommand.CanExecute(null));
        logs.SearchCommand.Execute(null); // e.g. Enter in the box: ignored meanwhile
        var search = Assert.Single(sent.OfType<SearchLogs>());

        logs.Apply(new LogSearchResult(search.RequestId, [new LogHit(ServerFile.Id, 3, "Zeile 3 FEHLER")], false, false));
        Assert.False(logs.Searching.IsBusy);
        Assert.True(logs.SearchCommand.CanExecute(null));
        Assert.True(logs.ShowHits);
    }
}
