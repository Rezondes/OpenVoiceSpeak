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
        var count => string.Format(Strings.Links_Pending, count),
    };

    bool OnServer(ChannelPair key) => server.Mirror.Links.Contains(key);

    public bool IsLinked(Guid id, Guid otherId) => desired.TryGetValue(ChannelPair.Of(id, otherId), out var linked) ? linked : OnServer(ChannelPair.Of(id, otherId));

    public bool IsPending(Guid id, Guid otherId) => desired.ContainsKey(ChannelPair.Of(id, otherId));

    public void Set(Guid id, Guid otherId, bool linked)
    {
        if (id == otherId) return;
        var key = ChannelPair.Of(id, otherId);
        if (linked == OnServer(key)) desired.Remove(key);
        else desired[key] = linked;
        Refresh();
    }

    /// <summary>After any server change: new channels, names and links; own pending changes stay.</summary>
    internal void Rebuild()
    {
        var channels = server.Tree.Channels.Live().Where(channel => !channel.IsSeparator).ToList(); // Package 111: nothing to link
        var ids = channels.Select(channel => channel.Id).ToHashSet();
        foreach (var (key, linked) in desired.ToList())
            if (!ids.Contains(key.Lower) || !ids.Contains(key.Upper) || linked == OnServer(key)) desired.Remove(key);

        var selected = Rows.Live().Where(linkRow => linkRow.IsSelected).Select(linkRow => linkRow.Id).ToHashSet();
        var rows = channels.Select((row, index) => new LinkRowViewModel(index + 1, row.Id, row.Name, selected.Contains(row.Id),
            channels.Select(col => new LinkCellViewModel(this, row.Id, col.Id, string.Format(Strings.Links_Pair, row.Name, col.Name))).ToList())).ToList();
        CollectionSync.Sync(Rows, rows); // rows and columns go together: a folding row would leave its column standing
        PendingCount = desired.Count;
    }

    void Refresh()
    {
        foreach (var cell in Rows.SelectMany(linkRow => linkRow.Cells)) cell.Refresh();
        PendingCount = desired.Count;
    }

    IEnumerable<ChannelPair> SelectedPairs()
    {
        var selected = Rows.Live().Where(linkRow => linkRow.IsSelected).Select(linkRow => linkRow.Id).ToList();
        for (int index = 0; index < selected.Count; index++)
            for (int otherIndex = index + 1; otherIndex < selected.Count; otherIndex++)
                yield return ChannelPair.Of(selected[index], selected[otherIndex]);
    }

    /// <summary>Every selected channel with every other: links are not transitive (A5).</summary>
    [RelayCommand]
    void LinkSelected()
    {
        foreach (var (id, otherId) in SelectedPairs()) Set(id, otherId, true);
    }

    [RelayCommand]
    void UnlinkSelected()
    {
        foreach (var (id, otherId) in SelectedPairs()) Set(id, otherId, false);
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
        var add = desired.Where(pair => pair.Value).Select(pair => new LinkInfo(pair.Key.Lower, pair.Key.Upper)).ToList();
        var remove = desired.Where(pair => !pair.Value).Select(pair => new LinkInfo(pair.Key.Lower, pair.Key.Upper)).ToList();
        if (add.Count + remove.Count == 0 || Applying.IsRunning) return null;
        // The server's ChannelsLinked/Unlinked make them real; until then they stay marked.
        return server.SendConfirmed(new SetChannelLinks(add, remove), _ => add.All(link => OnServer(ChannelPair.Of(link.A, link.B))) && remove.All(link => !OnServer(ChannelPair.Of(link.A, link.B))),
            Applying, notify: false);
    }

    [RelayCommand]
    void Discard()
    {
        desired.Clear();
        Refresh();
    }
}
