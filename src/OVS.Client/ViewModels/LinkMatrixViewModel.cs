using OVS.Client.Localization;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OVS.Shared.Protocol;

namespace OVS.Client.ViewModels;

/// <summary>
/// The "Links" tab (Package 38): every channel against every other, one checkbox per link. Changes stay pending
/// until "Übernehmen" sends them as one request. Pending means: differs from the server; a change by someone else
/// that reaches the same state simply ends the pending mark.
/// </summary>
public sealed partial class LinkMatrixViewModel : ObservableObject
{
    readonly ServerViewModel server;
    readonly Dictionary<(Guid, Guid), bool> desired = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPending), nameof(PendingText))]
    int pendingCount;

    public LinkMatrixViewModel(ServerViewModel server)
    {
        this.server = server;
        Applying = server.NewPending();
        Rebuild();
    }

    /// <summary>Package 98: "Übernehmen" spins until the server's link changes match what was sent.</summary>
    public Pending Applying { get; }

    public ObservableCollection<LinkRowViewModel> Rows { get; } = [];
    public bool HasPending => PendingCount > 0;
    public string PendingText => PendingCount switch
    {
        0 => Strings.Links_NoPending,
        1 => Strings.Links_OnePending,
        var n => string.Format(Strings.Links_Pending, n),
    };

    static (Guid, Guid) Norm(Guid a, Guid b) => a.CompareTo(b) < 0 ? (a, b) : (b, a);

    bool OnServer((Guid, Guid) key) => server.Mirror.Links.Contains(key);

    public bool IsLinked(Guid a, Guid b) => desired.TryGetValue(Norm(a, b), out var linked) ? linked : OnServer(Norm(a, b));

    public bool IsPending(Guid a, Guid b) => desired.ContainsKey(Norm(a, b));

    public void Set(Guid a, Guid b, bool linked)
    {
        if (a == b) return;
        var key = Norm(a, b);
        if (linked == OnServer(key)) desired.Remove(key);
        else desired[key] = linked;
        Refresh();
    }

    /// <summary>After any server change: new channels, names and links; own pending changes stay.</summary>
    internal void Rebuild()
    {
        var channels = server.Channels.ToList();
        var ids = channels.Select(c => c.Id).ToHashSet();
        foreach (var (key, linked) in desired.ToList())
            if (!ids.Contains(key.Item1) || !ids.Contains(key.Item2) || linked == OnServer(key)) desired.Remove(key);

        var selected = Rows.Where(r => r.IsSelected).Select(r => r.Id).ToHashSet();
        Rows.Clear();
        for (int i = 0; i < channels.Count; i++)
        {
            var row = channels[i];
            Rows.Add(new LinkRowViewModel(i + 1, row.Id, row.Name, selected.Contains(row.Id),
                channels.Select(col => new LinkCellViewModel(this, row.Id, col.Id, string.Format(Strings.Links_Pair, row.Name, col.Name))).ToList()));
        }
        PendingCount = desired.Count;
    }

    void Refresh()
    {
        foreach (var cell in Rows.SelectMany(r => r.Cells)) cell.Refresh();
        PendingCount = desired.Count;
    }

    IEnumerable<(Guid A, Guid B)> SelectedPairs()
    {
        var selected = Rows.Where(r => r.IsSelected).Select(r => r.Id).ToList();
        for (int i = 0; i < selected.Count; i++)
            for (int j = i + 1; j < selected.Count; j++)
                yield return (selected[i], selected[j]);
    }

    /// <summary>Every selected channel with every other: links are not transitive (A5).</summary>
    [RelayCommand]
    void LinkSelected()
    {
        foreach (var (a, b) in SelectedPairs()) Set(a, b, true);
    }

    [RelayCommand]
    void UnlinkSelected()
    {
        foreach (var (a, b) in SelectedPairs()) Set(a, b, false);
    }

    [RelayCommand]
    Task Apply()
    {
        var add = desired.Where(d => d.Value).Select(d => new LinkInfo(d.Key.Item1, d.Key.Item2)).ToList();
        var remove = desired.Where(d => !d.Value).Select(d => new LinkInfo(d.Key.Item1, d.Key.Item2)).ToList();
        if (add.Count + remove.Count == 0 || Applying.IsRunning) return Task.CompletedTask;
        // The server's ChannelsLinked/Unlinked make them real; until then they stay marked.
        server.SendConfirmed(new SetChannelLinks(add, remove), _ => add.All(l => OnServer(Norm(l.A, l.B))) && remove.All(l => !OnServer(Norm(l.A, l.B))),
            Applying, notify: false);
        return Task.CompletedTask;
    }

    [RelayCommand]
    void Discard()
    {
        desired.Clear();
        Refresh();
    }
}

public sealed partial class LinkRowViewModel(int number, Guid id, string name, bool isSelected, IReadOnlyList<LinkCellViewModel> cells) : ObservableObject
{
    [ObservableProperty] bool isSelected = isSelected;

    public int Number { get; } = number;
    public Guid Id { get; } = id;
    public string Name { get; } = name;
    public string Title => $"{Number}  {Name}";
    public IReadOnlyList<LinkCellViewModel> Cells { get; } = cells;
}

public sealed class LinkCellViewModel(LinkMatrixViewModel matrix, Guid row, Guid column, string tooltip) : ObservableObject
{
    public Guid Row { get; } = row;
    public Guid Column { get; } = column;
    public bool IsDiagonal => Row == Column;
    public string Tooltip { get; } = tooltip;

    /// <summary>Clicking changes the link, and with it the mirrored cell on the other side of the diagonal.</summary>
    public bool IsLinked
    {
        get => matrix.IsLinked(Row, Column);
        set
        {
            if (value != IsLinked) matrix.Set(Row, Column, value);
        }
    }

    public bool IsPending => matrix.IsPending(Row, Column);

    internal void Refresh()
    {
        OnPropertyChanged(nameof(IsLinked));
        OnPropertyChanged(nameof(IsPending));
    }
}
