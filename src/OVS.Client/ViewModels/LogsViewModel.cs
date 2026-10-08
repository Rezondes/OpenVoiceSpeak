using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OVS.Client.Collections;
using OVS.Client.Localization;
using OVS.Client.Models;
using OVS.Client.Services;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.LogAccess;

namespace OVS.Client.ViewModels;

/// <summary>
/// Package 81 (A98): the Logs tab. The server's file list with type and period filters, one opened file page by page
/// with a line filter, and the search over all files. Files are only ever named by the id from the server's list.
/// Package 82 (A99): with LogsDownload, files are selected with checkboxes and saved as one .log or a zip.
/// </summary>
public sealed partial class LogsViewModel : ObservableObject
{
    readonly ServerViewModel server;
    readonly HashSet<string> selected = [];
    bool downloadRight;
    IReadOnlyList<LogFileInfo> allFiles = [];
    readonly ListPages<LogFileInfo> filePages = new(); // Package 87
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

    /// <param name="transfer">The administration page's transfer; it carries the download and shows its progress.</param>
    internal LogsViewModel(ServerViewModel server, AdminTransfer transfer)
    {
        this.server = server;
        Transfer = transfer;
        FilesLoad = server.NewPending();
        Searching = server.NewPending();
        Searching.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PendingViewModel.IsBusy)) SearchCommand.NotifyCanExecuteChanged();
        };
        downloadRight = ShowDownload;
        RebuildSources();
        selectedSource = Sources[0];
    }

    public ObservableCollection<Choice<LogSource>> Sources { get; } = [];
    /// <summary>The files that pass the filters, newest first.</summary>
    public ObservableCollection<LogFileViewModel> Files { get; } = [];
    public bool HasNoFiles => allFiles.Count == 0;
    public bool HasNoFileMatches => allFiles.Count > 0 && !Files.Live().Any();

    public bool IsFileOpen => OpenFile is not null;
    public string OpenTitle => OpenFile is { } f ? $"{f.Title}, {f.StartText}" : "";
    public string PageText => string.Format(Strings.Ui_LogPage, Page, PageCount);
    /// <summary>The lines of the page that pass the line filter.</summary>
    public ObservableCollection<LogLineViewModel> Lines { get; } = [];
    public bool HasLines => Lines.Count > 0;
    public ObservableCollection<LogHitViewModel> Hits { get; } = [];

    internal AdminTransfer Transfer { get; }
    /// <summary>Package 97 (A113): the file list's loading state and the running search.</summary>
    public PendingViewModel FilesLoad { get; }
    public PendingViewModel Searching { get; }

    public Task RequestAsync() => server.SendAsync(new ListLogs(), pending: FilesLoad);

    /// <summary>The server's answers, passed on by the administration page.</summary>
    public void Apply(Message message)
    {
        switch (message)
        {
            case LogList list:
                if (filePages.Add(list.RequestId, list.Offset, list.Total, list.Files, (id, o) => _ = server.SendAsync(new ListLogs(o), id)) is not { } files) break;
                allFiles = files;
                FilesLoad.Done();
                selected.IntersectWith(allFiles.Select(f => f.Id)); // files gone meanwhile (retention) drop out
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
                Searching.Done();
                Hits.Clear();
                foreach (var hit in result.Hits) Hits.Add(new LogHitViewModel(hit, TitleOf(hit.FileId)));
                HitsText = result.TimedOut ? string.Format(Strings.Ui_LogHitsTimedOut, result.Hits.Count)
                    : result.Truncated ? string.Format(Strings.Ui_LogHitsTruncated, ProtocolLimits.MaxLogHits)
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
        var shown = allFiles.Where(Passes).Select(file => new LogFileViewModel(file, ShowDownload, selected.Contains(file.Id), OnSelectionChanged)).ToList();
        CollectionSync.Sync(Files, shown, server.Leave); // Package 102: a file filtered out folds away
        SelectedFile = shown.FirstOrDefault(f => f.Info.Id == OpenFile?.Info.Id);
        FileCountText = string.Format(Strings.Ui_LogFileCount, shown.Count, allFiles.Count);
        OnPropertyChanged(nameof(HasNoFiles));
        OnPropertyChanged(nameof(HasNoFileMatches));
    }

    // ---- Package 82: selection and download ----

    public bool ShowDownload => server.SelfPermissions.Has(Permission.LogsDownload);
    public bool CanDownload => ShowDownload && selected.Count > 0;
    public bool IsZipDownload => selected.Count > 1;
    public string SelectionText => string.Format(Strings.Ui_LogSelection, selected.Count);
    /// <summary>The chosen files, newest first like the list.</summary>
    public IReadOnlyList<string> SelectedIds => allFiles.Where(f => selected.Contains(f.Id)).Select(f => f.Id).ToList();

    /// <summary>The file's own name for one file, else ovs-logs_&lt;from&gt;_&lt;to&gt;.zip like the server names it.</summary>
    public string DownloadName
    {
        get
        {
            var files = allFiles.Where(f => selected.Contains(f.Id)).ToList();
            if (files.Count == 1) return files[0].Id[(files[0].Id.LastIndexOf('/') + 1)..];
            if (files.Count == 0) return "";
            return $"ovs-logs_{files.Min(f => f.Start).ToLocalTime():yyyy-MM-dd}_{files.Max(f => f.LastWrite).ToLocalTime():yyyy-MM-dd}.zip";
        }
    }

    /// <summary>The own rights changed: checkboxes come and go with LogsDownload.</summary>
    public void RightsChanged()
    {
        if (ShowDownload == downloadRight) return;
        downloadRight = ShowDownload;
        if (!downloadRight) selected.Clear();
        RebuildFiles();
        SelectionChanged();
    }

    void OnSelectionChanged(LogFileViewModel file)
    {
        if (file.IsSelected) selected.Add(file.Info.Id);
        else selected.Remove(file.Info.Id);
        SelectionChanged();
    }

    void SelectionChanged()
    {
        OnPropertyChanged(nameof(ShowDownload));
        OnPropertyChanged(nameof(CanDownload));
        OnPropertyChanged(nameof(IsZipDownload));
        OnPropertyChanged(nameof(SelectionText));
    }

    /// <summary>Adds every file the type and period filters show.</summary>
    [RelayCommand]
    void SelectAllInPeriod()
    {
        foreach (var file in Files) file.IsSelected = true;
    }

    [RelayCommand]
    void SelectNone()
    {
        selected.Clear();
        foreach (var file in Files) file.IsSelected = false;
        SelectionChanged();
    }

    /// <summary>Saves the selection to target (chosen in the save dialog).</summary>
    public Task DownloadAsync(string target) => Transfer.DownloadLogsAsync(SelectedIds, target);

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
        SelectedFile = Files.Live().FirstOrDefault(f => f.Info.Id == file.Info.Id);
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

    /// <summary>Package 97: disabled while the search shows busy; before that a second try is ignored in Search.</summary>
    bool CanSearch => !string.IsNullOrWhiteSpace(SearchText) && !Searching.IsBusy;
    partial void OnSearchTextChanged(string value) => SearchCommand.NotifyCanExecuteChanged();

    /// <summary>With the type and period of the file filters.</summary>
    [RelayCommand(CanExecute = nameof(CanSearch))]
    Task Search()
    {
        if (Searching.IsRunning) return Task.CompletedTask;
        var source = SelectedSource?.Value;
        return server.SendAsync(new SearchLogs(SearchText, source?.Kind, source?.ChannelId, From, To), pending: Searching);
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
        var file = Files.Live().FirstOrDefault(f => f.Info.Id == hit.FileId)
                   ?? allFiles.Where(f => f.Id == hit.FileId).Select(f => new LogFileViewModel(f)).FirstOrDefault();
        if (file is null) return Task.CompletedTask;
        LineFilter = "";
        return OpenAt(file, (hit.Line - 1) / WireProtocol.LogPageLines + 1, hit.Line);
    }
}
