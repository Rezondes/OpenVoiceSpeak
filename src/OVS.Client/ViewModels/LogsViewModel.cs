using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OVS.Client.Localization;
using OVS.Shared.Protocol;

namespace OVS.Client.ViewModels;

/// <summary>A choice of the Logs tab's type filter: every file, the server logs, or one channel.</summary>
public sealed record LogSource(LogKind? Kind, Guid? ChannelId);

/// <summary>
/// Package 81 (A98): the Logs tab. The server's file list with type and period filters, one opened file page by page
/// with a line filter, and the search over all files. Files are only ever named by the id from the server's list.
/// </summary>
public sealed partial class LogsViewModel : ObservableObject
{
    readonly ServerViewModel server;
    IReadOnlyList<LogFileInfo> allFiles = [];
    IReadOnlyList<string> pageLines = [];
    int? targetLine;

    [ObservableProperty] Choice<LogSource>? selectedSource;
    [ObservableProperty] DateTime? fromDate;
    [ObservableProperty] DateTime? toDate;
    [ObservableProperty] string fileCountText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFileOpen), nameof(OpenTitle))]
    LogFileViewModel? openFile;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageText))]
    [NotifyCanExecuteChangedFor(nameof(OlderCommand), nameof(NewerCommand), nameof(FirstCommand), nameof(LastCommand))]
    int page;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageText))]
    [NotifyCanExecuteChangedFor(nameof(OlderCommand), nameof(NewerCommand), nameof(FirstCommand), nameof(LastCommand))]
    int pageCount;
    [ObservableProperty] string lineFilter = "";
    [ObservableProperty] LogLineViewModel? selectedLine;
    /// <summary>The list's selection: choosing a file opens it, choosing a hit opens its file at the line.</summary>
    [ObservableProperty] LogFileViewModel? selectedFile;
    [ObservableProperty] LogHitViewModel? selectedHit;

    [ObservableProperty] string searchText = "";
    [ObservableProperty] bool showHits;
    [ObservableProperty] string hitsText = "";

    public LogsViewModel(ServerViewModel server)
    {
        this.server = server;
        RebuildSources();
        selectedSource = Sources[0];
    }

    public ObservableCollection<Choice<LogSource>> Sources { get; } = [];
    /// <summary>The files that pass the filters, newest first.</summary>
    public ObservableCollection<LogFileViewModel> Files { get; } = [];
    public bool HasNoFiles => allFiles.Count == 0;
    public bool HasNoFileMatches => allFiles.Count > 0 && Files.Count == 0;

    public bool IsFileOpen => OpenFile is not null;
    public string OpenTitle => OpenFile is { } f ? $"{f.Title}, {f.StartText}" : "";
    public string PageText => string.Format(Strings.Ui_LogPage, Page, PageCount);
    /// <summary>The lines of the page that pass the line filter.</summary>
    public ObservableCollection<LogLineViewModel> Lines { get; } = [];
    public bool HasLines => Lines.Count > 0;
    public ObservableCollection<LogHitViewModel> Hits { get; } = [];

    public Task RequestAsync() => server.SendAsync(new ListLogs());

    /// <summary>The server's answers, passed on by the administration page.</summary>
    public void Apply(Message message)
    {
        switch (message)
        {
            case LogList list:
                allFiles = list.Files;
                RebuildSources();
                RebuildFiles();
                break;
            case LogPage p when p.FileId == OpenFile?.Info.Id: // an answer for a file closed meanwhile is dropped
                pageLines = p.Lines;
                Page = p.Page;
                PageCount = p.PageCount;
                firstLine = p.FirstLine;
                RebuildLines();
                break;
            case LogSearchResult result:
                Hits.Clear();
                foreach (var hit in result.Hits) Hits.Add(new LogHitViewModel(hit, TitleOf(hit.FileId)));
                HitsText = result.TimedOut ? string.Format(Strings.Ui_LogHitsTimedOut, result.Hits.Count)
                    : result.Truncated ? string.Format(Strings.Ui_LogHitsTruncated, ProtocolInfo.MaxLogHits)
                    : result.Hits.Count == 0 ? Strings.Ui_LogNoHits
                    : string.Format(Strings.Ui_LogHits, result.Hits.Count);
                ShowHits = true;
                break;
        }
    }

    int firstLine = 1;

    // ---- File list ----

    partial void OnSelectedSourceChanged(Choice<LogSource>? value) => RebuildFiles();
    partial void OnFromDateChanged(DateTime? value) => RebuildFiles();
    partial void OnToDateChanged(DateTime? value) => RebuildFiles();

    DateTimeOffset? From => FromDate is { } d ? new DateTimeOffset(d.Date) : null;
    DateTimeOffset? To => ToDate is { } d ? new DateTimeOffset(d.Date.AddDays(1).AddTicks(-1)) : null;

    /// <summary>All, server, then every channel of the listing by name; the choice survives a new list.</summary>
    void RebuildSources()
    {
        var wanted = new List<Choice<LogSource>> { new(new(null, null), Strings.Ui_LogSourceAll), new(new(LogKind.Server, null), Strings.Ui_LogSourceServer) };
        wanted.AddRange(allFiles.Where(f => f.ChannelId is not null)
            .GroupBy(f => f.ChannelId).Select(g => new Choice<LogSource>(new(LogKind.Channel, g.Key), g.First().ChannelName ?? Strings.Ui_LogDeletedChannel))
            .OrderBy(c => c.Label, StringComparer.CurrentCultureIgnoreCase));
        if (wanted.SequenceEqual(Sources)) return;
        var selected = SelectedSource?.Value;
        Sources.Clear();
        foreach (var choice in wanted) Sources.Add(choice);
        SelectedSource = Sources.FirstOrDefault(c => c.Value == selected) ?? Sources[0];
    }

    bool Passes(LogFileInfo f)
    {
        var source = SelectedSource?.Value;
        return (source?.Kind is null || f.Kind == source.Kind) && (source?.ChannelId is null || f.ChannelId == source.ChannelId)
               && (To is null || f.Start <= To) && (From is null || f.LastWrite >= From);
    }

    void RebuildFiles()
    {
        Files.Clear();
        foreach (var file in allFiles.Where(Passes)) Files.Add(new LogFileViewModel(file));
        SelectedFile = Files.FirstOrDefault(f => f.Info.Id == OpenFile?.Info.Id);
        FileCountText = string.Format(Strings.Ui_LogFileCount, Files.Count, allFiles.Count);
        OnPropertyChanged(nameof(HasNoFiles));
        OnPropertyChanged(nameof(HasNoFileMatches));
    }

    string TitleOf(string fileId) => allFiles.FirstOrDefault(f => f.Id == fileId) is { } f ? LogFileViewModel.TitleOf(f) : fileId;

    // ---- The opened file ----

    [RelayCommand]
    Task Open(LogFileViewModel? file) => file is null ? Task.CompletedTask : OpenAt(file, null, null);

    partial void OnSelectedFileChanged(LogFileViewModel? value)
    {
        if (value is not null && value.Info.Id != OpenFile?.Info.Id) _ = OpenAt(value, null, null);
    }

    partial void OnSelectedHitChanged(LogHitViewModel? value)
    {
        if (value is not null) _ = OpenHit(value);
    }

    Task OpenAt(LogFileViewModel file, int? page, int? line)
    {
        if (OpenFile?.Info.Id != file.Info.Id)
        {
            OpenFile = file;
            pageLines = [];
            Lines.Clear();
            Page = PageCount = 0;
        }
        SelectedFile = Files.FirstOrDefault(f => f.Info.Id == file.Info.Id);
        targetLine = line;
        return server.SendAsync(new ReadLog(file.Info.Id, page));
    }

    Task Load(int? page) => OpenFile is { } file ? OpenAt(file, page, null) : Task.CompletedTask;

    bool CanOlder => IsFileOpen && Page > 1;
    bool CanNewer => IsFileOpen && Page < PageCount;

    [RelayCommand(CanExecute = nameof(CanOlder))]
    Task Older() => Load(Page - 1);

    [RelayCommand(CanExecute = nameof(CanNewer))]
    Task Newer() => Load(Page + 1);

    [RelayCommand(CanExecute = nameof(CanOlder))]
    Task First() => Load(1);

    [RelayCommand(CanExecute = nameof(CanNewer))]
    Task Last() => Load(null);

    /// <summary>The same page again; on the last one the last again, so new lines and pages of a running log show.</summary>
    [RelayCommand]
    Task Reload() => Load(Page >= PageCount ? null : Page);

    [RelayCommand]
    void CloseFile()
    {
        OpenFile = null;
        SelectedFile = null;
        pageLines = [];
        Page = PageCount = 0;
        RebuildLines();
    }

    partial void OnLineFilterChanged(string value) => RebuildLines();

    void RebuildLines()
    {
        var filter = LineFilter.Trim();
        Lines.Clear();
        LogLineViewModel? target = null;
        for (int i = 0; i < pageLines.Count; i++)
        {
            if (filter.Length > 0 && !pageLines[i].Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            var line = new LogLineViewModel(firstLine + i, pageLines[i], filter, firstLine + i == targetLine);
            if (line.IsTarget) target = line;
            Lines.Add(line);
        }
        SelectedLine = target;
        OnPropertyChanged(nameof(HasLines));
    }

    // ---- Search over all files ----

    bool CanSearch => !string.IsNullOrWhiteSpace(SearchText);
    partial void OnSearchTextChanged(string value) => SearchCommand.NotifyCanExecuteChanged();

    /// <summary>With the type and period of the file filters.</summary>
    [RelayCommand(CanExecute = nameof(CanSearch))]
    Task Search()
    {
        var source = SelectedSource?.Value;
        return server.SendAsync(new SearchLogs(SearchText, source?.Kind, source?.ChannelId, From, To));
    }

    [RelayCommand]
    void ClearSearch()
    {
        ShowHits = false;
        SelectedHit = null;
        Hits.Clear();
    }

    /// <summary>Opens the hit's file on the page of its line, which shows highlighted.</summary>
    [RelayCommand]
    Task OpenHit(LogHitViewModel? hit)
    {
        if (hit is null) return Task.CompletedTask;
        var file = Files.FirstOrDefault(f => f.Info.Id == hit.FileId)
                   ?? allFiles.Where(f => f.Id == hit.FileId).Select(f => new LogFileViewModel(f)).FirstOrDefault();
        if (file is null) return Task.CompletedTask;
        LineFilter = "";
        return OpenAt(file, (hit.Line - 1) / ProtocolInfo.LogPageLines + 1, hit.Line);
    }
}

/// <summary>One file of the list: server or channel name, start and size.</summary>
public sealed class LogFileViewModel(LogFileInfo info)
{
    public LogFileInfo Info { get; } = info;
    public string Title => TitleOf(Info);
    public bool IsServer => Info.Kind == LogKind.Server;
    public string StartText => Info.Start.ToLocalTime().ToString("g");
    public string Details => $"{StartText}, {BackupViewModel.Size(Info.Size)}";

    public static string TitleOf(LogFileInfo f) => f.Kind == LogKind.Server ? Strings.Ui_LogSourceServer : f.ChannelName ?? Strings.Ui_LogDeletedChannel;
}

/// <summary>A piece of a log line; IsMatch marks the text the line filter found.</summary>
public sealed record LogSegment(string Text, bool IsMatch);

/// <summary>One line of the opened page; IsTarget for the line a search hit opened.</summary>
public sealed class LogLineViewModel
{
    public LogLineViewModel(int number, string text, string filter, bool isTarget)
    {
        Number = number;
        Text = text;
        IsTarget = isTarget;
        Segments = Split(text, filter);
    }

    public int Number { get; }
    public string Text { get; }
    public bool IsTarget { get; }
    public IReadOnlyList<LogSegment> Segments { get; }

    static List<LogSegment> Split(string text, string filter)
    {
        var segments = new List<LogSegment>();
        int at = 0;
        while (filter.Length > 0 && text.IndexOf(filter, at, StringComparison.OrdinalIgnoreCase) is var found and >= 0)
        {
            if (found > at) segments.Add(new(text[at..found], false));
            segments.Add(new(text.Substring(found, filter.Length), true));
            at = found + filter.Length;
        }
        if (at < text.Length || segments.Count == 0) segments.Add(new(text[at..], false));
        return segments;
    }
}

/// <summary>One search hit: file title, line number and the line.</summary>
public sealed class LogHitViewModel(LogHit hit, string title)
{
    public string FileId => hit.FileId;
    public int Line => hit.Line;
    public string Text => hit.Text;
    public string Title { get; } = title;
    public string LineText => string.Format(Strings.Ui_LogLine, Line);
}
