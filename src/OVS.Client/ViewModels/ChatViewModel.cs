using System.Globalization;
using OVS.Client.Localization;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OVS.Client.Models;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages.Chat;

namespace OVS.Client.ViewModels;

/// <summary>
/// The chat in the main area (Package 32): "Allgemein" with server-wide messages and system notices, the
/// current channel and private tabs (Package 33). Lives as long as the connection, the server keeps no history (A27).
/// </summary>
internal sealed partial class ChatViewModel : ObservableObject
{
    /// <summary>The counter shows from this many characters on.</summary>
    public const int CounterFrom = 1800;

    readonly ServerViewModel server;
    /// <summary>Private tabs by partner fingerprint, closed ones included: reopening keeps the history (A27).</summary>
    readonly Dictionary<string, ChatTabViewModel> privateTabs = [];
    Guid? channelId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanWrite), nameof(NoRightHint), nameof(Placeholder))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    ChatTabViewModel selected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCounter), nameof(CounterText), nameof(IsTooLong))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    string draft = "";

    /// <summary>The server's answer to the last message, e.g. too many messages. Shown under the input.</summary>
    [ObservableProperty] string? composerError;

    /// <param name="earlier">Notices from before the connection, oldest first, so nothing gets lost.</param>
    /// <param name="server">The server the chat belongs to.</param>
    public ChatViewModel(ServerViewModel server, IEnumerable<Notice>? earlier = null)
    {
        this.server = server;
        General = new ChatTabViewModel(ChatTabKind.General, Strings.Chat_General);
        ChannelTab = new ChatTabViewModel(ChatTabKind.Channel, "");
        Tabs = [General, ChannelTab];
        selected = General;
        foreach (var notice in earlier ?? []) General.Add(new ChatEntry(notice.Time, notice.Text, Notice: notice.Kind), countUnread: false);

        server.ChatFeed.Received += OnChat;
        server.ChatFeed.Refused += text => ComposerError = text;
        server.StateChanged += () => OnStateChanged(server.LastChange);
        server.PrivateChatRequested += user => OpenPrivate(user.Fingerprint);
        OnStateChanged(StateChange.All);
    }

    /// <summary>General, the channel tab and the private tabs.</summary>
    public ObservableCollection<ChatTabViewModel> Tabs { get; }
    /// <summary>The tab of "Allgemein".</summary>
    public ChatTabViewModel General { get; }
    /// <summary>The tab of the own channel.</summary>
    public ChatTabViewModel ChannelTab { get; }

    /// <summary>The own rights allow writing in the selected tab.</summary>
    public bool CanWrite => Selected.Kind switch
    {
        ChatTabKind.General => server.CanChatServer,
        ChatTabKind.Channel => server.CanChatChannel,
        _ => server.CanChatPrivate && Selected.IsOnline,
    };

    /// <summary>Why the own user cannot write here, null when they can.</summary>
    public string? NoRightHint => CanWrite ? null : Selected.Kind switch
    {
        ChatTabKind.General => Strings.Chat_NoRightServer,
        ChatTabKind.Channel => Strings.Chat_NoRightChannel,
        _ when !Selected.IsOnline => TextFormat.Of(Strings.Chat_NotOnline, Selected.Title),
        _ => Strings.Chat_NoRightPrivate,
    };

    /// <summary>The input's placeholder.</summary>
    public string Placeholder => CanWrite ? TextFormat.Of(Strings.Chat_MessageTo, Selected.Title) : "";
    /// <summary>The draft is long enough to show the counter.</summary>
    public bool ShowCounter => Draft.Length >= CounterFrom;
    /// <summary>The draft is over the longest message.</summary>
    public bool IsTooLong => Draft.Length > ProtocolLimits.MaxChatLength;
    /// <summary>The draft's length and the limit.</summary>
    public string CounterText => $"{Draft.Length} / {ProtocolLimits.MaxChatLength}";

    partial void OnSelectedChanged(ChatTabViewModel value)
    {
        value.Unread = 0;
        ComposerError = null;
    }

    /// <summary>Shows a system notice in "Allgemein".</summary>
    public void AddNotice(Notice notice) =>
        Add(General, new ChatEntry(notice.Time, notice.Text, Notice: notice.Kind));

    void Add(ChatTabViewModel tab, ChatEntry entry, bool countUnread = true) => tab.Add(entry, countUnread && tab != Selected && !entry.IsOwn);

    /// <summary>The time of the connection's clock as the chat shows it: local, like a received message's.</summary>
    DateTime LocalNow() => DateTime.SpecifyKind(server.Time.GetLocalNow().DateTime, DateTimeKind.Local);

    void OnChat(ChatMessage chatMessage)
    {
        var entry = new ChatEntry(chatMessage.SentAt.LocalDateTime, chatMessage.Text, chatMessage.FromNickname, chatMessage.FromSessionId == server.Mirror.SelfId)
        {
            NameColor = server.Mirror.Users.GetValueOrDefault(chatMessage.FromSessionId) is { } sender ? NameColors.For(sender.GroupIds, server.Mirror.Groups) : null, // Package 167
            Sender = server.Tree.FindUser(chatMessage.FromSessionId), // Package 168: the picture beside the message
        };
        var tab = chatMessage.Target switch
        {
            ChatTarget.Server => General,
            ChatTarget.Channel when chatMessage.ChannelId == channelId => ChannelTab, // a message from the channel just left is dropped
            // The server sends a message before its sender can leave, so the partner is always known here.
            // An incoming message opens the tab in the background.
            ChatTarget.Private when (entry.IsOwn ? chatMessage.ToSessionId : chatMessage.FromSessionId) is { } id && server.Mirror.Users.GetValueOrDefault(id) is { } partner
                => PrivateTab(partner.Fingerprint),
            _ => null,
        };
        if (tab is null) return;
        // Package 98: the echo of an own message already shown: it turns normal instead of showing twice
        if (entry.IsOwn && tab.Entries.FirstOrDefault(chatEntry => chatEntry.Sending is { } outgoingChatMessage && (outgoingChatMessage.IsWaiting || outgoingChatMessage.IsFailed) && chatEntry.Text == chatMessage.Text) is { Sending: { } sent })
        {
            sent.Pending.Done();
            return;
        }
        Add(tab, entry);
    }

    /// <summary>"Privatnachricht" on a user: opens or brings back the tab and switches to it.</summary>
    public void OpenPrivate(string fingerprint) => Selected = PrivateTab(fingerprint);

    ChatTabViewModel PrivateTab(string fingerprint)
    {
        if (!privateTabs.TryGetValue(fingerprint, out var tab))
        {
            privateTabs[fingerprint] = tab = new ChatTabViewModel(ChatTabKind.Private, "", fingerprint) { Closing = CloseTab };
            SyncPartner(tab);
        }
        if (!Tabs.Contains(tab)) Tabs.Add(tab);
        return tab;
    }

    void CloseTab(ChatTabViewModel tab)
    {
        if (Selected == tab) Selected = General;
        Tabs.Remove(tab);
    }

    /// <summary>The title follows the nickname, the input follows whether the partner is online.</summary>
    void SyncPartner(ChatTabViewModel tab)
    {
        var user = server.Mirror.Users.Values.FirstOrDefault(userInfo => userInfo.Fingerprint == tab.Partner);
        if (user is not null) tab.Title = "@" + user.Nickname;
        tab.PartnerSession = user?.SessionId;
        bool online = user is not null;
        if (online == tab.IsOnline) return;
        tab.IsOnline = online;
        tab.Add(new ChatEntry(LocalNow(), online ? TextFormat.Of(Strings.Chat_BackOnline, tab.Title) : TextFormat.Of(Strings.Chat_Offline, tab.Title)), countUnread: false);
    }

    /// <summary>
    /// A new channel starts an empty tab (A27). Also picks up renames and changed rights. Package 122: only what the change
    /// can touch, another user's mute changes nothing here.
    /// </summary>
    void OnStateChanged(StateChange change)
    {
        if (change.HasFlag(StateChange.Members) || change.HasFlag(StateChange.Names))
            foreach (var tab in privateTabs.Values) SyncPartner(tab);
        if (!change.HasFlag(StateChange.Tree)) return;
        var channel = server.CurrentChannel;
        if (channel?.Id != channelId)
        {
            channelId = channel?.Id;
            ChannelTab.Entries.Clear();
            ChannelTab.Unread = 0;
            if (channel is not null) Add(ChannelTab, new ChatEntry(LocalNow(), TextFormat.Of(Strings.Chat_Entered, channel.Name)), countUnread: false);
        }
        ChannelTab.Title = channel?.Name ?? "";
        OnPropertyChanged(nameof(CanWrite));
        OnPropertyChanged(nameof(NoRightHint));
        OnPropertyChanged(nameof(Placeholder));
        SendCommand.NotifyCanExecuteChanged();
    }

    bool CanSend() => CanWrite && !string.IsNullOrWhiteSpace(Draft) && !IsTooLong;

    [RelayCommand(CanExecute = nameof(CanSend))]
    async Task SendAsync()
    {
        // Package 83: the server's text rules, so the draft stays for correcting
        if (TextRules.NormalizeText(Draft, ProtocolLimits.MaxChatLength) is not { } checkedText)
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
        Task SendAsync(PendingActionViewModel pending) => server.SendAsync(new SendChat(target, tab.PartnerSession, text), pending: pending);
        var sending = new OutgoingChatMessageViewModel(server.NewPending(), SendAsync);
        Add(tab, new ChatEntry(LocalNow(), text, server.Mirror.Self?.Nickname ?? "", IsOwn: true, Sending: sending));
        await SendAsync(sending.Pending);
    }
}
