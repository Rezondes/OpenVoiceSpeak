using System.Collections.ObjectModel;
using System.Globalization;
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
internal sealed partial class LogsViewModel : ObservableObject
{
    readonly ServerViewModel server;
    readonly HashSet<string> selected = [];
    bool downloadRight;
    IReadOnlyList<LogFileInfo> allFiles = [];
    readonly PagedListCollector<LogFileInfo> filePages = new(); // Package 87
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
    /// <param name="server">The server the logs belong to.</param>
    internal LogsViewModel(ServerViewModel server, AdminTransfer transfer)
    {
        this.server = server;
        Transfer = transfer;
        FilesLoad = server.NewPending();
        Searching = server.NewPending();
        Searching.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(PendingActionViewModel.IsBusy)) SearchCommand.NotifyCanExecuteChanged();
        };
        downloadRight = ShowDownload;
        RebuildSources();
        selectedSource = Sources[0];
    }

    /// <summary>The source filter's choices.</summary>
    public ObservableCollection<Choice<LogSource>> Sources { get; } = [];
    /// <summary>The files that pass the filters, newest first.</summary>
    public ObservableCollection<LogFileViewModel> Files { get; } = [];
    /// <summary>The server has no log files.</summary>
    public bool HasNoFiles => allFiles.Count == 0;
    /// <summary>No file matches the filter.</summary>
    public bool HasNoFileMatches => allFiles.Count > 0 && !Files.Live().Any();

    /// <summary>A file is open.</summary>
    public bool IsFileOpen => OpenFile is not null;
    /// <summary>The open file's title.</summary>
    public string OpenTitle => OpenFile is { } logFile ? $"{logFile.Title}, {logFile.StartText}" : "";
    /// <summary>The page and the page count.</summary>
    public string PageText => TextFormat.Of(Strings.Ui_LogPage, Page, PageCount);
    /// <summary>The lines of the page that pass the line filter.</summary>
    public ObservableCollection<LogLineViewModel> Lines { get; } = [];
    /// <summary>The open page has lines.</summary>
    public bool HasLines => Lines.Count > 0;
    /// <summary>The last search's hits.</summary>
    public ObservableCollection<LogHitViewModel> Hits { get; } = [];

    /// <summary>The page's one transfer, see <see cref="AdminViewModel.Transfer"/>.</summary>
    internal AdminTransfer Transfer { get; }
    /// <summary>Package 97 (A113): the file list's loading state and the running search.</summary>
    public PendingActionViewModel FilesLoad { get; }
    /// <summary>The search, running until its result comes.</summary>
    public PendingActionViewModel Searching { get; }

    /// <summary>Package 81: the logs are shown and asked for with LogsView.</summary>
    internal bool ShowLogs => server.SelfPermissions.Has(Permission.LogsView);

    /// <summary>Asks for the log file list when the logs are shown.</summary>
    internal Task RefreshAsync() => ShowLogs ? SendRoutedAsync(new ListLogs(), pending: FilesLoad) : Task.CompletedTask;

    /// <summary>Sends a request whose answers, and the server's error for it, come back to <see cref="Apply"/>.</summary>
    Task<string> SendRoutedAsync(Request request, string? id = null, PendingActionViewModel? pending = null) => server.SendAsync(request, id, pending, Apply);

    /// <summary>The administration page closed: no answer reaches the tab any more.</summary>
    internal void Detach() => server.Routes.RemoveAll(Apply);

    /// <summary>The server's answers to the tab's requests (sent with this as their route).</summary>
    internal void Apply(Message message)
    {
        switch (message)
        {
            case LogList list:
                if (filePages.Add(list.RequestId, list.Offset, list.Total, list.Files, (id, offset) => _ = SendRoutedAsync(new ListLogs(offset), id)) is not { } files) break;
                allFiles = files;
                FilesLoad.Done();
                selected.IntersectWith(allFiles.Select(logFile => logFile.Id)); // files gone meanwhile (retention) drop out
                RebuildSources();
                RebuildFiles();
                break;
            case LogPage logPage when logPage.FileId == OpenFile?.Info.Id: // an answer for a file closed meanwhile is dropped
                pageLines = logPage.Lines;
                Page = logPage.Page;
                PageCount = logPage.PageCount;
                firstLine = logPage.FirstLine;
                RebuildLines();
                break;
            case LogSearchResult result:
                Searching.Done();
                Hits.Clear();
                foreach (var hit in result.Hits) Hits.Add(new LogHitViewModel(hit, TitleOf(hit.FileId)));
                HitsText = result.TimedOut ? TextFormat.Of(Strings.Ui_LogHitsTimedOut, result.Hits.Count)
                    : result.Truncated ? TextFormat.Of(Strings.Ui_LogHitsTruncated, ProtocolLimits.MaxLogHits)
                    : result.Hits.Count == 0 ? Strings.Ui_LogNoHits
                    : TextFormat.Of(Strings.Ui_LogHits, result.Hits.Count);
                ShowHits = true;
                break;
        }
    }

    int firstLine = 1;

    // ---- File list ----

    partial void OnSelectedSourceChanged(Choice<LogSource>? value) => RebuildFiles();
    partial void OnFromDateChanged(DateTime? value) => RebuildFiles();
    partial void OnToDateChanged(DateTime? value) => RebuildFiles();

    DateTimeOffset? From => FromDate is { } time ? new DateTimeOffset(time.Date) : null;
    DateTimeOffset? To => ToDate is { } time ? new DateTimeOffset(time.Date.AddDays(1).AddTicks(-1)) : null;

    /// <summary>All, server, then every channel of the listing by name; the choice survives a new list.</summary>
    void RebuildSources()
    {
        var wanted = new List<Choice<LogSource>> { new(new(null, null), Strings.Ui_LogSourceAll), new(new(LogKind.Server, null), Strings.Ui_LogSourceServer) };
        wanted.AddRange(allFiles.Where(logFile => logFile.ChannelId is not null)
            .GroupBy(logFile => logFile.ChannelId).Select(grouping => new Choice<LogSource>(new(LogKind.Channel, grouping.Key), grouping.First().ChannelName ?? Strings.Ui_LogDeletedChannel))
            .OrderBy(source => source.Label, StringComparer.CurrentCultureIgnoreCase));
        if (wanted.SequenceEqual(Sources)) return;
        var selected = SelectedSource?.Value;
        Sources.Clear();
        foreach (var choice in wanted) Sources.Add(choice);
        SelectedSource = Sources.FirstOrDefault(choice => choice.Value == selected) ?? Sources[0];
    }

    bool Passes(LogFileInfo logFile)
    {
        var source = SelectedSource?.Value;
        return (source?.Kind is null || logFile.Kind == source.Kind) && (source?.ChannelId is null || logFile.ChannelId == source.ChannelId)
               && (To is null || logFile.Start <= To) && (From is null || logFile.LastWrite >= From);
    }

    void RebuildFiles()
    {
        var shown = allFiles.Where(Passes).Select(file => new LogFileViewModel(file, ShowDownload, selected.Contains(file.Id), OnSelectionChanged)).ToList();
        CollectionSync.Sync(Files, shown, server.Leave); // Package 102: a file filtered out folds away
        SelectedFile = shown.FirstOrDefault(logFile => logFile.Info.Id == OpenFile?.Info.Id);
        FileCountText = TextFormat.Of(Strings.Ui_LogFileCount, shown.Count, allFiles.Count);
        OnPropertyChanged(nameof(HasNoFiles));
        OnPropertyChanged(nameof(HasNoFileMatches));
    }

    // ---- Package 82: selection and download ----

    /// <summary>The download shows (LogsDownload).</summary>
    public bool ShowDownload => server.SelfPermissions.Has(Permission.LogsDownload);
    /// <summary>Files are ticked for a download.</summary>
    public bool CanDownload => ShowDownload && selected.Count > 0;
    /// <summary>More than one file is ticked: the download is a zip.</summary>
    public bool IsZipDownload => selected.Count > 1;
    /// <summary>How many files are ticked, as shown.</summary>
    public string SelectionText => TextFormat.Of(Strings.Ui_LogSelection, selected.Count);
    /// <summary>The chosen files, newest first like the list.</summary>
    public IReadOnlyList<string> SelectedIds => allFiles.Where(logFile => selected.Contains(logFile.Id)).Select(logFile => logFile.Id).ToList();

    /// <summary>The file's own name for one file, else ovs-logs_&lt;from&gt;_&lt;to&gt;.zip like the server names it.</summary>
    public string DownloadName
    {
        get
        {
            var files = allFiles.Where(logFile => selected.Contains(logFile.Id)).ToList();
            if (files.Count == 1) return files[0].Id[(files[0].Id.LastIndexOf('/') + 1)..];
            if (files.Count == 0) return "";
            return $"ovs-logs_{files.Min(logFile => logFile.Start).ToLocalTime():yyyy-MM-dd}_{files.Max(logFile => logFile.LastWrite).ToLocalTime():yyyy-MM-dd}.zip";
        }
    }

    /// <summary>The server's state changed: checkboxes come and go with LogsDownload (Package 82).</summary>
    internal void FollowServer()
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

    string TitleOf(string fileId) => allFiles.FirstOrDefault(logFile => logFile.Id == fileId) is { } logFileInfo ? LogFileViewModel.TitleOf(logFileInfo) : fileId;

    // ---- The opened file ----

    [RelayCommand]
    Task OpenAsync(LogFileViewModel? file) => file is null ? Task.CompletedTask : OpenAtAsync(file, null, null);

    partial void OnSelectedFileChanged(LogFileViewModel? value)
    {
        if (value is not null && value.Info.Id != OpenFile?.Info.Id) _ = OpenAtAsync(value, null, null);
    }

    partial void OnSelectedHitChanged(LogHitViewModel? value)
    {
        if (value is not null) _ = OpenHitAsync(value);
    }

#pragma warning disable CA1859 // its callers only wait for it; LoadAsync hands on a completed task as well
    Task OpenAtAsync(LogFileViewModel file, int? page, int? line)
#pragma warning restore CA1859
    {
        if (OpenFile?.Info.Id != file.Info.Id)
        {
            OpenFile = file;
            pageLines = [];
            Lines.Clear();
            Page = PageCount = 0;
        }
        SelectedFile = Files.Live().FirstOrDefault(logFile => logFile.Info.Id == file.Info.Id);
        targetLine = line;
        return SendRoutedAsync(new ReadLog(file.Info.Id, page));
    }

    Task LoadAsync(int? page) => OpenFile is { } file ? OpenAtAsync(file, page, null) : Task.CompletedTask;

    bool CanOlder => IsFileOpen && Page > 1;
    bool CanNewer => IsFileOpen && Page < PageCount;

    [RelayCommand(CanExecute = nameof(CanOlder))]
    Task OlderAsync() => LoadAsync(Page - 1);

    [RelayCommand(CanExecute = nameof(CanNewer))]
    Task NewerAsync() => LoadAsync(Page + 1);

    [RelayCommand(CanExecute = nameof(CanOlder))]
    Task FirstAsync() => LoadAsync(1);

    [RelayCommand(CanExecute = nameof(CanNewer))]
    Task LastAsync() => LoadAsync(null);

    /// <summary>The same page again; on the last one the last again, so new lines and pages of a running log show.</summary>
    [RelayCommand]
    Task ReloadAsync() => LoadAsync(Page >= PageCount ? null : Page);

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
        for (int index = 0; index < pageLines.Count; index++)
        {
            if (filter.Length > 0 && !pageLines[index].Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            var line = new LogLineViewModel(firstLine + index, pageLines[index], filter, firstLine + index == targetLine);
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
    Task SearchAsync()
    {
        if (Searching.IsRunning) return Task.CompletedTask;
        var source = SelectedSource?.Value;
        return SendRoutedAsync(new SearchLogs(SearchText, source?.Kind, source?.ChannelId, From, To), pending: Searching);
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
    Task OpenHitAsync(LogHitViewModel? hit)
    {
        if (hit is null) return Task.CompletedTask;
        var file = Files.Live().FirstOrDefault(logFile => logFile.Info.Id == hit.FileId)
                   ?? allFiles.Where(logFile => logFile.Id == hit.FileId).Select(logFile => new LogFileViewModel(logFile)).FirstOrDefault();
        if (file is null) return Task.CompletedTask;
        LineFilter = "";
        return OpenAtAsync(file, (hit.Line - 1) / WireProtocol.LogPageLines + 1, hit.Line);
    }
}
