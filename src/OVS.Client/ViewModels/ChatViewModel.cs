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
public sealed record ChatEntry(DateTime Time, string Text, string? From = null, bool IsOwn = false, NoticeKind? Notice = null)
{
    public string TimeText => Time.ToString("HH:mm");
    public bool IsMessage => From is not null;
    public bool IsNotice => Notice is not null;
    public bool IsMarker => From is null && Notice is null;
    public bool IsInfo => Notice is NoticeKind.Info;
    public bool IsWelcome => Notice is NoticeKind.Welcome;
    public bool IsWarning => Notice is NoticeKind.Warning;
    public bool IsError => Notice is NoticeKind.Error;
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
        General = new ChatTab(ChatTabKind.General, "Allgemein");
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
        ChatTabKind.General => "Dir fehlt das Recht, serverweit zu schreiben.",
        ChatTabKind.Channel => "Dir fehlt das Recht, im Channel zu schreiben.",
        _ when !Selected.IsOnline => $"{Selected.Title} ist nicht online.",
        _ => "Dir fehlt das Recht, privat zu schreiben.",
    };

    public string Placeholder => CanWrite ? $"Nachricht an {Selected.Title}" : "";
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
        switch (m.Target)
        {
            case ChatTarget.Server:
                Add(General, entry);
                break;
            case ChatTarget.Channel when m.ChannelId == channelId: // a message from the channel just left is dropped
                Add(ChannelTab, entry);
                break;
            case ChatTarget.Private:
                // The server sends a message before its sender can leave, so the partner is always known here.
                var partnerId = entry.IsOwn ? m.ToSessionId : m.FromSessionId;
                if (partnerId is { } id && server.Mirror.Users.GetValueOrDefault(id) is { } partner)
                    Add(PrivateTab(partner.Fingerprint), entry); // an incoming message opens the tab in the background
                break;
        }
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
        tab.Add(new ChatEntry(DateTime.Now, online ? $"{tab.Title} ist wieder online." : $"{tab.Title} ist offline."), countUnread: false);
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
            if (channel is not null) Add(ChannelTab, new ChatEntry(DateTime.Now, $"Du hast \"{channel.Name}\" betreten."), countUnread: false);
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
        var text = Draft.Trim();
        var tab = Selected;
        var target = tab.Kind switch
        {
            ChatTabKind.General => ChatTarget.Server,
            ChatTabKind.Channel => ChatTarget.Channel,
            _ => ChatTarget.Private,
        };
        Draft = "";
        ComposerError = null;
        await server.SendChatAsync(target, text, tab.PartnerSession);
    }
}
