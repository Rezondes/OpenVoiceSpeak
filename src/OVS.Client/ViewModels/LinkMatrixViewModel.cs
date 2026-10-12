using OVS.Client.Net;
using OVS.Client.Collections;
using OVS.Client.Localization;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OVS.Client.Models;
using OVS.Shared.Protocol.Messages.Links;

namespace OVS.Client.ViewModels;

/// <summary>
/// The "Links" tab (Package 38): every channel against every other, one checkbox per link. Changes stay pending
/// until "Übernehmen" sends them as one request. Pending means: differs from the server; a change by someone else
/// that reaches the same state simply ends the pending mark.
/// </summary>
public sealed partial class LinkMatrixViewModel : ObservableObject
{
    readonly ServerViewModel server;
    readonly Dictionary<ChannelPair, bool> desired = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPending), nameof(PendingText))]
    int pendingCount;

    public LinkMatrixViewModel(ServerViewModel server)
    {
        this.server = server;
        Applying = server.NewPending();
        Place = AdminPlace.Create(() => HasPending, Applying, SendPending, Discard);
        Rebuild();
    }

    /// <summary>Package 114: the place of the links tab: the marked changes are sent with one request, or thrown away.</summary>
    internal AdminPlace Place { get; }

    /// <summary>Package 98: "Übernehmen" spins until the server's link changes match what was sent.</summary>
    public PendingActionViewModel Applying { get; }

    public ObservableCollection<LinkRowViewModel> Rows { get; } = [];
    public bool HasPending => PendingCount > 0;
    public string PendingText => PendingCount switch
    {
        0 => Strings.Links_NoPending,
        1 => Strings.Links_OnePending,
        var n => string.Format(Strings.Links_Pending, n),
    };

    bool OnServer(ChannelPair key) => server.Mirror.Links.Contains(key);

    public bool IsLinked(Guid a, Guid b) => desired.TryGetValue(ChannelPair.Of(a, b), out var linked) ? linked : OnServer(ChannelPair.Of(a, b));

    public bool IsPending(Guid a, Guid b) => desired.ContainsKey(ChannelPair.Of(a, b));

    public void Set(Guid a, Guid b, bool linked)
    {
        if (a == b) return;
        var key = ChannelPair.Of(a, b);
        if (linked == OnServer(key)) desired.Remove(key);
        else desired[key] = linked;
        Refresh();
    }

    /// <summary>After any server change: new channels, names and links; own pending changes stay.</summary>
    internal void Rebuild()
    {
        var channels = server.Tree.Channels.Live().Where(c => !c.IsSeparator).ToList(); // Package 111: nothing to link
        var ids = channels.Select(c => c.Id).ToHashSet();
        foreach (var (key, linked) in desired.ToList())
            if (!ids.Contains(key.Lower) || !ids.Contains(key.Upper) || linked == OnServer(key)) desired.Remove(key);

        var selected = Rows.Live().Where(r => r.IsSelected).Select(r => r.Id).ToHashSet();
        var rows = channels.Select((row, i) => new LinkRowViewModel(i + 1, row.Id, row.Name, selected.Contains(row.Id),
            channels.Select(col => new LinkCellViewModel(this, row.Id, col.Id, string.Format(Strings.Links_Pair, row.Name, col.Name))).ToList())).ToList();
        CollectionSync.Sync(Rows, rows); // rows and columns go together: a folding row would leave its column standing
        PendingCount = desired.Count;
    }

    void Refresh()
    {
        foreach (var cell in Rows.SelectMany(r => r.Cells)) cell.Refresh();
        PendingCount = desired.Count;
    }

    IEnumerable<ChannelPair> SelectedPairs()
    {
        var selected = Rows.Live().Where(r => r.IsSelected).Select(r => r.Id).ToList();
        for (int i = 0; i < selected.Count; i++)
            for (int j = i + 1; j < selected.Count; j++)
                yield return ChannelPair.Of(selected[i], selected[j]);
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
        SendPending();
        return Task.CompletedTask;
    }

    /// <summary>Package 114: the marked changes to the server; null when there is nothing to send.</summary>
    PendingActionViewModel? SendPending()
    {
        var add = desired.Where(d => d.Value).Select(d => new LinkInfo(d.Key.Lower, d.Key.Upper)).ToList();
        var remove = desired.Where(d => !d.Value).Select(d => new LinkInfo(d.Key.Lower, d.Key.Upper)).ToList();
        if (add.Count + remove.Count == 0 || Applying.IsRunning) return null;
        // The server's ChannelsLinked/Unlinked make them real; until then they stay marked.
        return server.SendConfirmed(new SetChannelLinks(add, remove), _ => add.All(l => OnServer(ChannelPair.Of(l.A, l.B))) && remove.All(l => !OnServer(ChannelPair.Of(l.A, l.B))),
            Applying, notify: false);
    }

    [RelayCommand]
    void Discard()
    {
        desired.Clear();
        Refresh();
    }
}
