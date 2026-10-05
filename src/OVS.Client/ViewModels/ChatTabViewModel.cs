using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OVS.Client.Models;

namespace OVS.Client.ViewModels;

/// <param name="partner">Private tabs (Package 33): the partner's fingerprint, so the tab survives a reconnect.</param>
public sealed partial class ChatTabViewModel(ChatTabKind kind, string title, string? partner = null) : ObservableObject
{
    const int MaxEntries = 500;

    [ObservableProperty] string title = title;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnread))]
    int unread;
    /// <summary>Private tabs: whether the partner is online.</summary>
    [ObservableProperty] bool isOnline = true;

    public ChatTabKind Kind { get; } = kind;
    public string? Partner { get; } = partner;
    public uint? PartnerSession { get; internal set; }
    public bool IsGeneral => Kind == ChatTabKind.General;
    public bool IsChannel => Kind == ChatTabKind.Channel;
    public bool IsPrivate => Kind == ChatTabKind.Private;
    public bool HasUnread => Unread > 0;
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
