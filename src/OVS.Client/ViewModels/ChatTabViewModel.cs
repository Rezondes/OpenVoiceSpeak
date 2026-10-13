using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OVS.Client.Models;

namespace OVS.Client.ViewModels;

/// <param name="partner">Private tabs (Package 33): the partner's fingerprint, so the tab survives a reconnect.</param>
/// <param name="kind">General, channel or private.</param>
/// <param name="title">The tab's title.</param>
public sealed partial class ChatTabViewModel(ChatTabKind kind, string title, string? partner = null) : ObservableObject
{
    const int MaxEntries = 500;

    [ObservableProperty] string title = title;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnread))]
    int unread;
    /// <summary>Private tabs: whether the partner is online.</summary>
    [ObservableProperty] bool isOnline = true;

    /// <summary>General, channel or private.</summary>
    public ChatTabKind Kind { get; } = kind;
    /// <summary>The partner's fingerprint of a private tab.</summary>
    public string? Partner { get; } = partner;
    /// <summary>The partner's session while online.</summary>
    public uint? PartnerSession { get; internal set; }
    /// <summary>The tab of "Allgemein".</summary>
    public bool IsGeneral => Kind == ChatTabKind.General;
    /// <summary>The tab of the own channel.</summary>
    public bool IsChannel => Kind == ChatTabKind.Channel;
    /// <summary>A private tab.</summary>
    public bool IsPrivate => Kind == ChatTabKind.Private;
    /// <summary>The tab has unread messages.</summary>
    public bool HasUnread => Unread > 0;
    /// <summary>The tab's lines.</summary>
    public ObservableCollection<ChatEntry> Entries { get; } = [];

    internal Action<ChatTabViewModel>? Closing { get; init; }

    [RelayCommand]
    void Close() => Closing?.Invoke(this);

    internal void Add(ChatEntry entry, bool countUnread)
    {
        Entries.Add(entry);
        while (Entries.Count > MaxEntries) Entries.RemoveAt(0);
        if (countUnread) Unread++;
    }
}
