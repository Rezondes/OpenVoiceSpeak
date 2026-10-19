using System.Globalization;
using Microsoft.AspNetCore.Components;
using OVS.Manager.Control;
using OVS.Manager.Docker;
using OVS.Manager.Localization;
using OVS.Manager.Servers;
using OVS.Shared.Protocol.Messages.LogAccess;

namespace OVS.Manager.Components.Pages.Servers;

/// <summary>
/// Package M16 (AC1 to AC3): the server's own log, listed, paged, searched and downloaded; for a server without a session
/// (outdated, not answering) its console output instead, live and without token lines.
/// </summary>
sealed partial class ServerLog
{
    /// <summary>The most console lines kept on the page.</summary>
    const int MaxConsoleLines = 500;

    readonly CancellationTokenSource stop = new();

    [Inject]
    LogCommands Logs { get; set; } = null!;

    [Inject]
    ConsoleLog Console { get; set; } = null!;

    [Inject]
    ServerConnections Connections { get; set; } = null!;

    [Inject]
    StatusWatcher Watcher { get; set; } = null!;

    [Inject]
    IDockerEngine Docker { get; set; } = null!;

    [Inject]
    TimeProvider Time { get; set; } = null!;

    /// <summary>The server's id from the address.</summary>
    [Parameter]
    public string Id { get; set; } = "";

    /// <summary>The server's log files.</summary>
    IReadOnlyList<LogFileInfo> Files { get; set; } = [];

    /// <summary>The page shown.</summary>
    LogPage? Page { get; set; }

    /// <summary>The hits of the last search.</summary>
    IReadOnlyList<LogHit> Hits { get; set; } = [];

    string Query { get; set; } = "";

    bool IsFirstPage => Page is null || Page.Page <= 1;

    bool IsLastPage => Page is null || Page.Page >= Page.PageCount;

    /// <summary>AC3: the console is shown instead of the protocol log.</summary>
    bool ShowsConsole { get; set; }

    /// <summary>The console lines so far, the newest last.</summary>
    List<string> ConsoleLines { get; } = [];

    /// <summary>Why the last step failed, null when it did not.</summary>
    string? Error { get; set; }

    /// <summary>Lists the server's log, or follows its console when there is no session.</summary>
    protected override async Task OnInitializedAsync()
    {
        if (Connections.LinkOf(Id) is { Status: ServerLinkStatus.Connected })
        {
            var list = await Logs.ListAsync(Id);
            (Files, Error) = (list.Files, list.Error);
            return;
        }
        ShowsConsole = true;
        var container = (await Docker.ListAsync()).FirstOrDefault(entry => entry.ServerId == Id);
        if (container is not null) _ = FollowAsync(container.Id);
    }

    /// <summary>Stops following the console.</summary>
    public async ValueTask DisposeAsync()
    {
        await stop.CancelAsync();
        stop.Dispose();
    }

    async Task FollowAsync(string containerId)
    {
        try
        {
            await foreach (var line in Console.FollowAsync(containerId, stop.Token))
            {
                ConsoleLines.Add(line);
                if (ConsoleLines.Count > MaxConsoleLines) ConsoleLines.RemoveAt(0);
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    Func<Task> Read(LogFileInfo file) => () => ShowAsync(Logs.ReadAsync(Id, file.Id, null));

    Func<Task> Open(LogHit hit) => () => ShowAsync(Logs.ReadAtAsync(Id, hit));

    Task PreviousAsync() => ShowAsync(Logs.ReadAsync(Id, Page!.FileId, Page.Page - 1));

    Task NextAsync() => ShowAsync(Logs.ReadAsync(Id, Page!.FileId, Page.Page + 1));

    async Task SearchAsync()
    {
        var result = await Logs.SearchAsync(Id, Query);
        (Hits, Error) = (result.Hits, result.Error);
    }

    async Task ShowAsync(Task<LogText> reading)
    {
        var text = await reading;
        Error = text.Error;
        if (text.Page is not null) Page = text.Page;
    }

    static string HitText(LogHit hit) => TextFormat.Of(Strings.Log_Hit, hit.FileId, hit.Line, hit.Text);

    static string PageTitle(LogPage page) => TextFormat.Of(Strings.Log_Page, page.FileId, page.Page, page.PageCount);

    string DownloadLink(LogFileInfo file) => $"/servers/{Uri.EscapeDataString(Id)}/log/download?file={Uri.EscapeDataString(file.Id)}";

    string Local(DateTimeOffset at) => TimeZoneInfo.ConvertTime(at, Time.LocalTimeZone).ToString("g", CultureInfo.CurrentCulture);
}
