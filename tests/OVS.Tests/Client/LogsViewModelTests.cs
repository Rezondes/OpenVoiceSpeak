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
    static (LogsViewModel Logs, List<Request> Sent) Create(LogSearchResult? searchResult = null)
    {
        var snapshot = new ServerSnapshot(new ServerSettingsInfo("Server", "", false), Lobby,
            [new ChannelInfo(Lobby, "Lobby", "", 0)], [], [new GroupInfo(WellKnownGroups.Admin, "Admin", Permission.All)],
            [new UserInfo(1, "fp1", "ich", Lobby, false, false, false, Permission.LogsView, [])]);
        var sent = new List<Request>();
        ServerViewModel? server = null;
        server = new ServerViewModel(new StateMirror(new Welcome(1, "", snapshot)), r =>
        {
            sent.Add(r);
            Message? answer = r switch
            {
                ListLogs => new LogList(r.RequestId, [ServerFile, RaidFile, OldServerFile, OldFile]),
                ReadLog read => Page(read),
                SearchLogs => searchResult,
                _ => null,
            };
            if (answer is not null) server!.Apply(answer);
            return Task.CompletedTask;
        }, new ManualTimeProvider());
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
}
