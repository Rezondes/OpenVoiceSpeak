using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OVS.Client.Localization;
using OVS.Shared.Logging;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.LogAccess;

namespace OVS.Client.ViewModels;

/// <summary>
/// Package 165: the page "Audit-Log". Asks the log search of Package 81 for the server log lines with the audit marker
/// (<see cref="AuditLine"/>), newest first, and shows them as rows (time, actor, action) with a search field and a person
/// filter. No store of its own: the lines live as long as the log (LogDays). Shown with LogsView only.
/// </summary>
internal sealed partial class AuditLogViewModel : ObservableObject
{
    readonly ServerViewModel server;
    IReadOnlyList<AuditLine> all = [];

    [ObservableProperty] string searchText = "";
    [ObservableProperty] string selectedPerson = Strings.Audit_Everyone;
    [ObservableProperty] string? note;

    /// <param name="server">The server whose log is asked.</param>
    internal AuditLogViewModel(ServerViewModel server)
    {
        this.server = server;
        Loading = server.NewPending();
        People.Add(Strings.Audit_Everyone);
    }

    /// <summary>The rows that pass the search and the person filter, newest first.</summary>
    public ObservableCollection<AuditLine> Rows { get; } = [];
    /// <summary>"Alle" and everyone who did something, by name.</summary>
    public ObservableCollection<string> People { get; } = [];
    /// <summary>The search's state (Package 97).</summary>
    public PendingActionViewModel Loading { get; }
    /// <summary>The search came back and no row passes.</summary>
    public bool HasNoRows => Loading.HasSucceeded && Rows.Count == 0;

    /// <summary>The page shows with LogsView.</summary>
    internal bool ShowAuditLog => server.SelfPermissions.Has(Permission.LogsView);

    /// <summary>Asks for the audit lines (also the page's refresh button, Package 154).</summary>
    [RelayCommand]
    internal Task RefreshAsync() =>
        ShowAuditLog ? server.SendAsync(new SearchLogs(AuditLine.Marker.TrimEnd(), LogKind.Server), null, Loading, Apply) : Task.CompletedTask;

    /// <summary>The administration page closed: no answer reaches the page any more.</summary>
    internal void Detach() => server.Routes.RemoveAll(Apply);

    /// <summary>The search's answer.</summary>
    internal void Apply(Message message)
    {
        if (message is not LogSearchResult result) return;
        Loading.Done();
        all = [.. result.Hits.Select(hit => AuditLine.Parse(hit.Text)).OfType<AuditLine>().OrderByDescending(line => line.Time, StringComparer.Ordinal)];
        Note = result.TimedOut || result.Truncated ? Strings.Audit_Partial : null;
        var chosen = SelectedPerson;
        People.Clear();
        People.Add(Strings.Audit_Everyone);
        foreach (var person in all.Select(line => line.Actor).Distinct().Order(StringComparer.CurrentCultureIgnoreCase)) People.Add(person);
        SelectedPerson = People.Contains(chosen) ? chosen : Strings.Audit_Everyone;
        Rebuild();
    }

    partial void OnSearchTextChanged(string value) => Rebuild();
    partial void OnSelectedPersonChanged(string value) => Rebuild();

    void Rebuild()
    {
        Rows.Clear();
        foreach (var line in all.Where(Passes)) Rows.Add(line);
        OnPropertyChanged(nameof(HasNoRows));
    }

    bool Passes(AuditLine line) =>
        (SelectedPerson == Strings.Audit_Everyone || line.Actor == SelectedPerson)
        && (SearchText.Trim() is not { Length: > 0 } wanted
            || line.Action.Contains(wanted, StringComparison.CurrentCultureIgnoreCase) || line.Actor.Contains(wanted, StringComparison.CurrentCultureIgnoreCase));
}
