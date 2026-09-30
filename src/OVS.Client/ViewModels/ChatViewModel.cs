using OVS.Client.Localization;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OVS.Shared.Protocol;

namespace OVS.Client.ViewModels;

public enum ChatTabKind { General, Channel, Private }

/// <summary>
/// One line in a chat tab: a message (From set), a system notice (Notice set) or a plain marker such as
/// "you entered the channel" (neither).
/// </summary>
/// <param name="Sending">Package 98: an own message on its way to the server, shown before the server's echo.</param>
public sealed record ChatEntry(DateTime Time, string Text, string? From = null, bool IsOwn = false, NoticeKind? Notice = null, ChatSending? Sending = null)
{
    public string TimeText => Time.ToString("HH:mm");
    public bool IsMessage => From is not null;
    public bool IsOwnMessage => IsMessage && IsOwn;     // Package 64: right, as a bubble without avatar
    public bool IsOtherMessage => IsMessage && !IsOwn;  // left, with avatar and name
    public bool IsNotice => Notice is not null;
    public bool IsMarker => From is null && Notice is null;
    public bool IsInfo => Notice is NoticeKind.Info;
    public bool IsWelcome => Notice is NoticeKind.Welcome;
    public bool IsWarning => Notice is NoticeKind.Warning;
    public bool IsError => Notice is NoticeKind.Error;
}

/// <summary>
/// Package 98 (A113): an own message shown at once, greyed with a clock until the server's echo; refused or without an
/// echo within 10 s it is marked "nicht gesendet" and can be sent again.
/// </summary>
public sealed partial class ChatSending : ObservableObject
{
    readonly Func<Pending, Task> send;

    public ChatSending(Pending pending, Func<Pending, Task> send)
    {
        Pending = pending;
        this.send = send;
        pending.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not (nameof(Pending.IsRunning) or nameof(Pending.Error))) return;
            OnPropertyChanged(nameof(IsWaiting));
            OnPropertyChanged(nameof(IsFailed));
        };
    }

    public Pending Pending { get; }
    public bool IsWaiting => Pending.IsRunning;
    public bool IsFailed => Pending.HasError;

    [RelayCommand]
    Task Retry() => IsWaiting ? Task.CompletedTask : send(Pending);
}

/// <param name="partner">Private tabs (Package 33): the partner's fingerprint, so the tab survives a reconnect.</param>
public sealed partial class ChatTab(ChatTabKind kind, string title, string? partner = null) : ObservableObject
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

    internal Action<ChatTab>? Closing { get; init; }

    [RelayCommand]
    void Close() => Closing?.Invoke(this);

    internal void Add(ChatEntry entry, bool countUnread)
    {
        Entries.Add(entry);
        while (Entries.Count > MaxEntries) Entries.RemoveAt(0);
        if (countUnread) Unread++;
    }
}

/// <summary>
/// The chat in the main area (Package 32): "Allgemein" with server-wide messages and system notices, the
/// current channel and private tabs (Package 33). Lives as long as the connection, the server keeps no history (A27).
/// </summary>
public sealed partial class ChatViewModel : ObservableObject
{
    public const int CounterFrom = 1800;

    readonly ServerViewModel server;
    /// <summary>Private tabs by partner fingerprint, closed ones included: reopening keeps the history (A27).</summary>
    readonly Dictionary<string, ChatTab> privateTabs = [];
    Guid? channelId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanWrite), nameof(NoRightHint), nameof(Placeholder))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    ChatTab selected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCounter), nameof(CounterText), nameof(IsTooLong))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    string draft = "";

    /// <summary>The server's answer to the last message, e.g. too many messages. Shown under the input.</summary>
    [ObservableProperty] string? composerError;

    /// <param name="earlier">Notices from before the connection, oldest first, so nothing gets lost.</param>
    public ChatViewModel(ServerViewModel server, IEnumerable<Notice>? earlier = null)
    {
        this.server = server;
        General = new ChatTab(ChatTabKind.General, Strings.Chat_General);
        ChannelTab = new ChatTab(ChatTabKind.Channel, "");
        Tabs = [General, ChannelTab];
        selected = General;
        foreach (var n in earlier ?? []) General.Add(new ChatEntry(n.Time, n.Text, Notice: n.Kind), countUnread: false);

        server.ChatReceived += OnChat;
        server.ChatError += text => ComposerError = text;
        server.StateChanged += OnStateChanged;
        server.PrivateChatRequested += user => OpenPrivate(user.Fingerprint);
        OnStateChanged();
    }

    public ObservableCollection<ChatTab> Tabs { get; }
    public ChatTab General { get; }
    public ChatTab ChannelTab { get; }

    public bool CanWrite => Selected.Kind switch
    {
        ChatTabKind.General => server.CanChatServer,
        ChatTabKind.Channel => server.CanChatChannel,
        _ => server.CanChatPrivate && Selected.IsOnline,
    };

    public string? NoRightHint => CanWrite ? null : Selected.Kind switch
    {
        ChatTabKind.General => Strings.Chat_NoRightServer,
        ChatTabKind.Channel => Strings.Chat_NoRightChannel,
        _ when !Selected.IsOnline => string.Format(Strings.Chat_NotOnline, Selected.Title),
        _ => Strings.Chat_NoRightPrivate,
    };

    public string Placeholder => CanWrite ? string.Format(Strings.Chat_MessageTo, Selected.Title) : "";
    public bool ShowCounter => Draft.Length >= CounterFrom;
    public bool IsTooLong => Draft.Length > ProtocolInfo.MaxChatLength;
    public string CounterText => $"{Draft.Length} / {ProtocolInfo.MaxChatLength}";

    partial void OnSelectedChanged(ChatTab value)
    {
        value.Unread = 0;
        ComposerError = null;
    }

    public void AddNotice(Notice notice) =>
        Add(General, new ChatEntry(notice.Time, notice.Text, Notice: notice.Kind));

    void Add(ChatTab tab, ChatEntry entry, bool countUnread = true) => tab.Add(entry, countUnread && tab != Selected && !entry.IsOwn);

    void OnChat(ChatMessage m)
    {
        var entry = new ChatEntry(m.SentAt.LocalDateTime, m.Text, m.FromNickname, m.FromSessionId == server.Mirror.SelfId);
        var tab = m.Target switch
        {
            ChatTarget.Server => General,
            ChatTarget.Channel when m.ChannelId == channelId => ChannelTab, // a message from the channel just left is dropped
            // The server sends a message before its sender can leave, so the partner is always known here.
            // An incoming message opens the tab in the background.
            ChatTarget.Private when (entry.IsOwn ? m.ToSessionId : m.FromSessionId) is { } id && server.Mirror.Users.GetValueOrDefault(id) is { } partner
                => PrivateTab(partner.Fingerprint),
            _ => null,
        };
        if (tab is null) return;
        // Package 98: the echo of an own message already shown: it turns normal instead of showing twice
        if (entry.IsOwn && tab.Entries.FirstOrDefault(e => e.Sending is { } s && (s.IsWaiting || s.IsFailed) && e.Text == m.Text) is { Sending: { } sent })
        {
            sent.Pending.Done();
            return;
        }
        Add(tab, entry);
    }

    /// <summary>"Privatnachricht" on a user: opens or brings back the tab and switches to it.</summary>
    public void OpenPrivate(string fingerprint) => Selected = PrivateTab(fingerprint);

    ChatTab PrivateTab(string fingerprint)
    {
        if (!privateTabs.TryGetValue(fingerprint, out var tab))
        {
            privateTabs[fingerprint] = tab = new ChatTab(ChatTabKind.Private, "", fingerprint) { Closing = CloseTab };
            SyncPartner(tab);
        }
        if (!Tabs.Contains(tab)) Tabs.Add(tab);
        return tab;
    }

    void CloseTab(ChatTab tab)
    {
        if (Selected == tab) Selected = General;
        Tabs.Remove(tab);
    }

    /// <summary>The title follows the nickname, the input follows whether the partner is online.</summary>
    void SyncPartner(ChatTab tab)
    {
        var user = server.Mirror.Users.Values.FirstOrDefault(u => u.Fingerprint == tab.Partner);
        if (user is not null) tab.Title = "@" + user.Nickname;
        tab.PartnerSession = user?.SessionId;
        bool online = user is not null;
        if (online == tab.IsOnline) return;
        tab.IsOnline = online;
        tab.Add(new ChatEntry(DateTime.Now, online ? string.Format(Strings.Chat_BackOnline, tab.Title) : string.Format(Strings.Chat_Offline, tab.Title)), countUnread: false);
    }

    /// <summary>A new channel starts an empty tab (A27). Also picks up renames and changed rights.</summary>
    void OnStateChanged()
    {
        var channel = server.CurrentChannel;
        if (channel?.Id != channelId)
        {
            channelId = channel?.Id;
            ChannelTab.Entries.Clear();
            ChannelTab.Unread = 0;
            if (channel is not null) Add(ChannelTab, new ChatEntry(DateTime.Now, string.Format(Strings.Chat_Entered, channel.Name)), countUnread: false);
        }
        ChannelTab.Title = channel?.Name ?? "";
        foreach (var tab in privateTabs.Values) SyncPartner(tab);
        OnPropertyChanged(nameof(CanWrite));
        OnPropertyChanged(nameof(NoRightHint));
        OnPropertyChanged(nameof(Placeholder));
        SendCommand.NotifyCanExecuteChanged();
    }

    bool CanSend() => CanWrite && !string.IsNullOrWhiteSpace(Draft) && !IsTooLong;

    [RelayCommand(CanExecute = nameof(CanSend))]
    async Task Send()
    {
        // Package 83: the server's text rules, so the draft stays for correcting
        if (TextRules.Text(Draft, ProtocolInfo.MaxChatLength) is not { } checkedText)
        {
            ComposerError = Strings.Chat_InvalidChars;
            return;
        }
        var text = checkedText.Trim();
        var tab = Selected;
        var target = tab.Kind switch
        {
            ChatTabKind.General => ChatTarget.Server,
            ChatTabKind.Channel => ChatTarget.Channel,
            _ => ChatTarget.Private,
        };
        Draft = "";
        ComposerError = null;
        // Package 98: shown at once, greyed until the server's echo
        Task Send(Pending pending) => server.SendAsync(new SendChat(target, tab.PartnerSession, text), pending: pending);
        var sending = new ChatSending(server.NewPending(), Send);
        Add(tab, new ChatEntry(DateTime.Now, text, server.Mirror.Self?.Nickname ?? "", IsOwn: true, Sending: sending));
        await Send(sending.Pending);
    }
}
