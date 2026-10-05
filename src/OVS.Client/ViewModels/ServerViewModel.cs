using OVS.Client.Localization;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OVS.Client.Audio.Playback;
using OVS.Client.Audio.Sounds;
using OVS.Client.Input;
using OVS.Client.Net;
using OVS.Client.Settings;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Chat;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.Groups;
using OVS.Shared.Protocol.Messages.Links;
using OVS.Shared.Protocol.Messages.LogAccess;
using OVS.Shared.Protocol.Messages.Moderation;
using OVS.Shared.Protocol.Messages.ServerBackups;
using OVS.Shared.Protocol.Messages.ServerInfo;
using OVS.Shared.Protocol.Messages.Users;

namespace OVS.Client.ViewModels;

/// <param name="AllowedGroupIds">Package 93: the channel's group lock; from the dialog null while unchanged, empty to remove it.</param>
/// <param name="HasPassword">Package 94: into the dialog, whether the channel has a password.</param>
/// <param name="Password">Package 94: out of the dialog, null = unchanged, empty = remove, else the new password.</param>
/// <param name="Kind">Package 111: out of the create dialog, a voice channel or a separator.</param>
/// <param name="Bitrate">Package 132: the channel's kbps, in and out of the dialog.</param>
public sealed record ChannelEdit(string Name, string Description, bool IsMuted = false, int MaxUsers = 0, IReadOnlyList<Guid>? AllowedGroupIds = null,
    bool HasPassword = false, string? Password = null, ChannelKind Kind = ChannelKind.Voice, int Bitrate = ProtocolInfo.DefaultBitrate);

/// <summary>The channel dialog only offers the channel options when editing (Package 34).</summary>
public enum ChannelDialogMode { Create, Edit, EditDefault }
public sealed record BanChoice(string Reason, int? DurationMinutes, bool IncludeIp)
{
    public static IReadOnlyList<(string Label, int? Minutes)> Durations =>
        [(Strings.Ban_Hour, 60), (Strings.Ban_Day, 1440), (Strings.Ban_Week, 10080), (Strings.Ban_Permanent, null)];
}

/// <summary>Package 114: the answer to "Ungespeicherte Änderungen": keep editing, save and leave, or leave without saving.</summary>
public enum LeaveChoice { Stay, Save, Discard }

/// <summary>Dialogs the view provides. Unset entries mean "cancelled" (used by tests).</summary>
public sealed class Dialogs
{
    /// <summary>Package 114: unsaved changes would be lost; unset means leave without saving (as before).</summary>
    public Func<Task<LeaveChoice>>? AskLeave { get; init; }
    /// <summary>Package 54: one dialog for creating (empty) and editing (prefilled); the mode sets title and button.</summary>
    /// <remarks>
    /// Package 98: the dialogs that send get the send as their last argument. It returns the waiting mark; the dialog keeps
    /// its card open with a busy button until the mark ends, closes when confirmed and shows the error otherwise. A dialog
    /// that never calls it (a test fake) has the answer sent after it closed.
    /// </remarks>
    public Func<ChannelEdit, ChannelDialogMode, Func<ChannelEdit, Pending>?, Task<ChannelEdit?>>? EditChannel { get; init; }
    public Func<string, IReadOnlyList<ChannelViewModel>, Func<ChannelViewModel, Pending>?, Task<ChannelViewModel?>>? PickChannel { get; init; }
    public Func<string, string, Func<string, Pending>?, Task<string?>>? AskText { get; init; }
    /// <summary>Nickname, and whether an IP is known to ban as well (Package 72: not for every offline user).</summary>
    public Func<string, bool, Func<BanChoice, Pending>?, Task<BanChoice?>>? Ban { get; init; }
    public Func<string, Task<bool>>? Confirm { get; init; }
    /// <summary>Package 72: the red "Alle Daten von {0} löschen?" dialog.</summary>
    public Func<string, Task<bool>>? ConfirmDeleteUser { get; init; }
    /// <summary>Package 74: the red question before a backup replaces the server's state (argument: the backup's title).</summary>
    public Func<string, Task<bool>>? ConfirmRestore { get; init; }
    /// <summary>Package 89 (A101): the warning before a download, the file holds the server's private key and user data.</summary>
    public Func<Task<bool>>? ConfirmBackupDownload { get; init; }
    /// <summary>Package 40: the server wants a password (none stored or the stored one is wrong).</summary>
    public Func<string, Task<PasswordAnswer?>>? AskPassword { get; init; }
    /// <summary>Package 94: the password of a channel (argument: its name); null = cancelled.</summary>
    public Func<string, Task<string?>>? AskChannelPassword { get; init; }
    public Func<Bookmark, Task<BookmarkEdit?>>? EditBookmark { get; init; }
    /// <summary>Package 41: action and key; null binding = add. The second argument captures the next key.</summary>
    public Func<KeyBinding?, Func<KeyAction, Task<KeyChord?>>, Task<KeyBinding?>>? EditKeyBinding { get; init; }
    /// <summary>Package 43: "Version X ist verfügbar. Jetzt installieren?"</summary>
    public Func<UpdateOffer, Task<bool>>? OfferUpdate { get; init; }
    /// <summary>Package 133: the "Verbindungsdetails" card; it shows the figures as they are refreshed until closed.</summary>
    public Func<ConnectionStats, Task>? ConnectionDetails { get; init; }
}

/// <summary>Package 122: what a state message changed, so whoever listens only redoes what depends on it.</summary>
[Flags]
public enum StateChange
{
    None = 0,
    /// <summary>Someone joined or left.</summary>
    Members = 1,
    /// <summary>Someone was renamed.</summary>
    Names = 2,
    /// <summary>A channel's name, count or icons, the channel list or my own name: what the sidebar measures.</summary>
    Sidebar = 4,
    /// <summary>Rebuilt as a whole: the own rights, channel or groups, links or the settings may have changed too.</summary>
    Tree = 8,
    All = Members | Names | Sidebar | Tree,
}

/// <summary>One connected server: channel tree, own state and every request the UI can make.</summary>
public sealed partial class ServerViewModel : ObservableObject
{
    static readonly TimeSpan SpeakingHold = TimeSpan.FromMilliseconds(300);

    readonly Func<Request, Task> send;
    readonly TimeProvider time;
    readonly Dictionary<Guid, ChannelViewModel> channelVms = [];
    readonly Dictionary<uint, UserViewModel> userVms = [];
    readonly Dictionary<uint, (DateTimeOffset At, bool ViaLink)> speaking = [];
    byte? selfTarget;
    bool mutedBeforeDeafen;
    int requestCounter;

    [ObservableProperty] string serverName = "";
    [ObservableProperty] string welcomeText = "";
    [ObservableProperty] bool selfMuted;
    [ObservableProperty] bool selfDeafened;
    [ObservableProperty] bool isAdmin;
    [ObservableProperty] ChannelViewModel? currentChannel;
    [ObservableProperty] UserViewModel? self;
    /// <summary>The server logo as PNG, null while the server has none or it is still loading.</summary>
    [ObservableProperty] byte[]? iconPng;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreateChannel), nameof(CanAdminister), nameof(HasSpeakLinked),
        nameof(CanChatServer), nameof(CanChatChannel), nameof(CanChatPrivate))]
    Permission selfPermissions;

    public ServerViewModel(StateMirror mirror, Func<Request, Task> send, TimeProvider time, Dialogs? dialogs = null)
    {
        Mirror = mirror;
        this.send = send;
        this.time = time;
        Dialogs = dialogs ?? new Dialogs();
        Leave = new LeaveTimer(time, () => Post);
        Rebuild();
    }

    public StateMirror Mirror { get; }
    public TimeProvider Time => time;
    public Dialogs Dialogs { get; }
    public ObservableCollection<ChannelViewModel> Channels { get; } = [];

    /// <summary>Package 102: how long a user or channel that is gone stays to fold away (the window sets it).</summary>
    public LeaveTimer Leave { get; }

    public bool CanCreateChannel => SelfPermissions.Has(Permission.ChannelCreate);
    public bool HasSpeakLinked => SelfPermissions.Has(Permission.SpeakLinked);
    /// <summary>Package 76: as soon as any administration tab would show.</summary>
    public bool CanAdminister =>
        (SelfPermissions & (Permission.GroupsView | Permission.UsersView | Permission.BansView | Permission.ServerConfig | Permission.ChannelLink
                            | Permission.LogsView | Permission.BackupsManage)) != 0;
    public bool CanRedeemToken => !IsAdmin;

    partial void OnIsAdminChanged(bool value) => OnPropertyChanged(nameof(CanRedeemToken));

    public UserList? LastUserList { get; private set; }
    public BanList? LastBanList { get; private set; }

    public event Action<string>? Notice;
    /// <summary>Package 75: a notice from the administration page (e.g. a failed backup transfer).</summary>
    internal void ShowNotice(string text) => Notice?.Invoke(text);
    public event Action<Message>? AdminMessage;
    public event Action<ServerIcon>? IconReceived;

    // ---- Chat (Package 31) ----
    /// <summary>Chat requests get their own id prefix, so their errors can go to the composer instead of the feed.</summary>
    const string ChatRequestPrefix = "c";
    readonly Queue<ChatMessage> recentChat = new();
    public event Action<ChatMessage>? ChatReceived;
    /// <summary>Package 47: a moment that deserves a tone. The main view model hands it to the audio engine.</summary>
    public event Action<SoundEvent>? SoundRequested;
    internal void RequestSound(SoundEvent sound) => SoundRequested?.Invoke(sound);
    /// <summary>Package 73: the admin page changes the own groups; the tone for the one acting follows from there.</summary>
    internal bool OwnGroupChangePending { get; set; }
    /// <summary>The channel the own "Betreten" asked for: arriving there is "entered", anywhere else "moved".</summary>
    Guid? pendingJoin;
    /// <summary>Package 94 (A109): channel passwords that worked, in memory for this connection only.</summary>
    readonly Dictionary<Guid, string> channelPasswords = [];
    (Guid Channel, string Password)? pendingPassword;
    public event Action<string>? ChatError;
    /// <summary>"Privatnachricht" in a user's context menu; the chat opens the tab.</summary>
    public event Action<UserViewModel>? PrivateChatRequested;

    Func<string, float> volumeOf = _ => 1f;

    /// <summary>Package 51: the stored volume of a person (fingerprint, 1 = 100 %), provided by the main view model.</summary>
    public Func<string, float> VolumeOf
    {
        get => volumeOf;
        set
        {
            volumeOf = value;
            foreach (var user in userVms.Values) user.ShowVolume(value(user.Fingerprint));
        }
    }

    /// <summary>Package 51: someone moved a volume slider (fingerprint, new volume).</summary>
    public event Action<string, float>? VolumeChanged;
    internal void OnVolumeChanged(UserViewModel user) => VolumeChanged?.Invoke(user.Fingerprint, (float)(user.VolumePercent / 100));
    internal void RequestPrivateChat(UserViewModel user) => PrivateChatRequested?.Invoke(user);
    public IReadOnlyCollection<ChatMessage> RecentChat => recentChat;
    public bool CanChatServer => SelfPermissions.Has(Permission.ChatServer);
    public bool CanChatChannel => SelfPermissions.Has(Permission.ChatChannel);
    public bool CanChatPrivate => SelfPermissions.Has(Permission.ChatPrivate);
    public Task SendChatAsync(ChatTarget target, string text, uint? to = null) => SendAsync(new SendChat(target, to, text));
    public string? IconHash => Mirror.Settings.IconHash;
    public event Action? StateChanged;
    /// <summary>Package 122: what the last <see cref="StateChanged"/> was about.</summary>
    public StateChange LastChange { get; private set; } = StateChange.All;

    /// <summary>Package 122: how often a user's view model was updated, so tests can count the work a message does.</summary>
    internal int UserUpdates { get; set; }
    /// <summary>Package 122: how often a user's speaking state was set (tests count it).</summary>
    internal int SpeakingTouches { get; private set; }

    public void Apply(Message message)
    {
        ApplyMessage(message);
        // Package 98: actions without an answer of their own end with the message that shows them done
        foreach (var c in confirming.ToList())
        {
            if (!c.Pending.IsRunning) confirming.Remove(c);
            else if (c.Confirms(message))
            {
                confirming.Remove(c);
                c.Pending.Done();
            }
        }
    }

    void ApplyMessage(Message message)
    {
        switch (message)
        {
            case Error e:
                if (e.Code is Codes.WrongChannelPassword or Codes.ChannelPasswordRequired && pendingPassword is { } wrong)
                {
                    channelPasswords.Remove(wrong.Channel); // Package 94: changed meanwhile, ask again next time
                    pendingPassword = null;
                }
                var text = ErrorTexts.For(e.Code, e.Detail);
                if (e.RequestId is { } failed && waiting.Remove(failed, out var pending) && pending.Answers(failed)) pending.Fail(text); // Package 97
                if (e.RequestId?.StartsWith(ChatRequestPrefix) == true && ChatError is { } chatError) chatError(text);
                else Notice?.Invoke(text);
                AdminMessage?.Invoke(message); // Package 73: the admin page drops what the server refused
                return;
            case UserList list:
                LastUserList = list;
                AdminMessage?.Invoke(message);
                return;
            case BanList list:
                LastBanList = list;
                AdminMessage?.Invoke(message);
                return;
            case BackupList or BackupChunk or UploadBackupAck or BackupUploaded or LogList or LogPage or LogSearchResult or LogDownloadReady or LogChunk: // Packages 74, 75, 81, 82
                AdminMessage?.Invoke(message);
                return;
            case ServerIcon icon:
                IconReceived?.Invoke(icon);
                return;
            case ChatMessage chat:
                if (chat.FromSessionId != Mirror.SelfId) // Package 56: a sound per chat, never for the own messages
                    SoundRequested?.Invoke(chat.Target switch
                    {
                        ChatTarget.Private => SoundEvent.PrivateMessage,
                        ChatTarget.Channel => SoundEvent.ChannelMessage,
                        _ => SoundEvent.ServerMessage,
                    });
                recentChat.Enqueue(chat);
                while (recentChat.Count > 200) recentChat.Dequeue();
                ChatReceived?.Invoke(chat);
                return;
        }
        var sound = SoundFor(message); // compared with the state before the change
        var before = message switch
        {
            UserUpdated u => Mirror.Users.GetValueOrDefault(u.User.SessionId),
            UserJoined j => Mirror.Users.GetValueOrDefault(j.User.SessionId), // a session already there is moved, not shown twice
            UserLeft l => Mirror.Users.GetValueOrDefault(l.SessionId),
            _ => null,
        };
        if (Mirror.Apply(message)) Show(message, before);
        if (sound is { } s) SoundRequested?.Invoke(s);
    }

    /// <summary>
    /// Package 122: a message about another user (or about me that leaves my channel, rights and groups as they were)
    /// only updates that user and the lists they leave or come into; everything else rebuilds the tree.
    /// </summary>
    void Show(Message message, UserInfo? before)
    {
        switch (message)
        {
            case UserJoined j when j.User.SessionId != Mirror.SelfId:
                ShowUser(j.User, before);
                return;
            case UserLeft when before is not null && before.SessionId != Mirror.SelfId:
                ShowUser(null, before);
                return;
            case UserUpdated u when before is not null && (u.User.SessionId != Mirror.SelfId
                || (u.User.ChannelId == before.ChannelId && u.User.Permissions == before.Permissions && u.User.GroupIds.SequenceEqual(before.GroupIds))):
                ShowUser(u.User, before);
                return;
        }
        Rebuild(message is UserJoined or UserLeft ? StateChange.All : StateChange.Names | StateChange.Sidebar | StateChange.Tree);
    }

    void ShowUser(UserInfo? now, UserInfo? before)
    {
        var change = StateChange.None;
        if (now is not null)
        {
            if (!userVms.TryGetValue(now.SessionId, out var user)) userVms[now.SessionId] = user = new UserViewModel(this, now.SessionId);
            user.Update(now, now.SessionId == Mirror.SelfId, SelfPermissions, Mirror.Groups);
            user.UpdateMoveTargets(Channels.Live().ToList());
        }
        if (now is null || before is null) change |= StateChange.Members;
        if (now?.Nickname != before?.Nickname) change |= StateChange.Names; // also its place in the list
        if (now?.ChannelId != before?.ChannelId || (change.HasFlag(StateChange.Names) && now?.SessionId == Mirror.SelfId))
            change |= StateChange.Sidebar; // the counts, or my name below the tree
        if ((change & (StateChange.Names | StateChange.Sidebar)) != 0)
            foreach (var id in new[] { before?.ChannelId, now?.ChannelId }.OfType<Guid>().Distinct()) ShowUsers(id);
        if (now is null) ForgetGoneUsers();
        RefreshSpeaking(); // a new row shows whether its user talks
        LastChange = change;
        StateChanged?.Invoke();
    }

    /// <summary>Package 122: one channel's list, from the users' view models as they are.</summary>
    void ShowUsers(Guid channelId)
    {
        if (!channelVms.TryGetValue(channelId, out var channel) || Mirror.Channels.GetValueOrDefault(channelId) is not { } info) return;
        var users = Mirror.Users.Values.Where(u => u.ChannelId == channelId).OrderBy(u => u.Nickname, StringComparer.CurrentCultureIgnoreCase)
            .Select(u => userVms[u.SessionId]).ToList();
        CollectionSync.Sync(channel.Users, users, Leave);
        channel.SlotText = SlotText(info, users.Count);
    }

    static string SlotText(ChannelInfo channel, int count) => channel.MaxUsers > 0 ? $"{count}/{channel.MaxUsers}" : count.ToString();

    /// <summary>Package 102: one that is still folding away stays known, so coming back meanwhile is the same entry again.</summary>
    void ForgetGoneUsers()
    {
        var shownUsers = Channels.SelectMany(c => c.Users).ToHashSet();
        foreach (var id in userVms.Keys.Except(Mirror.Users.Keys).Where(id => !shownUsers.Contains(userVms[id])).ToList()) userVms.Remove(id);
    }

    SoundEvent? SoundFor(Message message)
    {
        var self = Mirror.Self;
        var mine = self?.ChannelId;
        switch (message)
        {
            case UserJoined j when j.User.SessionId != Mirror.SelfId && j.User.ChannelId == mine:
                return SoundEvent.UserJoined;
            case UserLeft l when l.SessionId != Mirror.SelfId && Mirror.Users.TryGetValue(l.SessionId, out var gone) && gone.ChannelId == mine:
                return SoundEvent.UserLeft;
            case UserUpdated u when u.User.SessionId == Mirror.SelfId && self is not null:
                if (u.User.ChannelId != self.ChannelId)
                {
                    if (pendingPassword is { } worked && worked.Channel == u.User.ChannelId) channelPasswords[worked.Channel] = worked.Password;
                    pendingPassword = null;
                    bool own = pendingJoin == u.User.ChannelId;
                    pendingJoin = null;
                    return own ? SoundEvent.ChannelEntered : SoundEvent.Moved;
                }
                if (u.User.ServerMuted && !self.ServerMuted) return SoundEvent.ServerMuted;
                // Package 73: the own groups changed; a change made by myself only gives the tone for the one acting
                return !OwnGroupChangePending && !u.User.GroupIds.ToHashSet().SetEquals(self.GroupIds) ? SoundEvent.GroupChanged : null;
            case UserUpdated u when Mirror.Users.TryGetValue(u.User.SessionId, out var before):
                if (before.ChannelId != mine && u.User.ChannelId == mine) return SoundEvent.UserJoined;
                if (before.ChannelId == mine && u.User.ChannelId != mine) return SoundEvent.UserLeft;
                return null;
            default:
                return null;
        }
    }

    // ---- Tree ----

    /// <param name="change">Package 122: what the listeners are told changed.</param>
    public void Rebuild(StateChange change = StateChange.Names | StateChange.Sidebar | StateChange.Tree)
    {
        var self = Mirror.Self;
        ServerName = Mirror.Settings.Name;
        WelcomeText = Mirror.Settings.WelcomeText;
        SelfPermissions = self?.Permissions ?? Permission.None;
        IsAdmin = self?.GroupIds.Contains(WellKnownGroups.Admin) ?? false;
        var groups = Mirror.Groups;

        // Package 102: one that is still folding away stays known, so coming back meanwhile is the same entry again
        foreach (var id in channelVms.Keys.Except(Mirror.Channels.Keys).Where(id => !Channels.Contains(channelVms[id])).ToList()) channelVms.Remove(id);
        ForgetGoneUsers();

        var byChannel = Mirror.Users.Values.ToLookup(u => u.ChannelId); // Package 122: not every user per channel
        var desired = new List<ChannelViewModel>();
        foreach (var c in ServerOrder().OrderBy(c => pendingOrder?.IndexOf(c.Id) is >= 0 and var i ? i : int.MaxValue)) // Package 98
        {
            if (!channelVms.TryGetValue(c.Id, out var channel)) channelVms[c.Id] = channel = new ChannelViewModel(this, c.Id);
            var linked = Mirror.LinkedChannels(c.Id).Select(id => Mirror.Channels.GetValueOrDefault(id)?.Name).OfType<string>().Order().ToList();
            channel.Update(c, linked, c.Id == Mirror.DefaultChannelId, self?.ChannelId == c.Id, SelfPermissions);
            channel.UpdateLock(c, groups, self?.GroupIds ?? [], IsAdmin);

            var users = byChannel[c.Id]
                .OrderBy(u => u.Nickname, StringComparer.CurrentCultureIgnoreCase)
                .Select(u =>
                {
                    if (!userVms.TryGetValue(u.SessionId, out var user)) userVms[u.SessionId] = user = new UserViewModel(this, u.SessionId);
                    user.Update(u, u.SessionId == Mirror.SelfId, SelfPermissions, groups);
                    return user;
                })
                .ToList();
            CollectionSync.Sync(channel.Users, users, Leave);
            channel.SlotText = SlotText(c, users.Count);
            desired.Add(channel);
        }
        CollectionSync.Sync(Channels, desired, Leave);
        for (int i = 0; i < desired.Count; i++) desired[i].SetPosition(first: i == 0, last: i == desired.Count - 1);
        foreach (var user in userVms.Values) user.UpdateMoveTargets(desired); // Package 120: in the new order of the tree
        CurrentChannel = self is null ? null : channelVms.GetValueOrDefault(self.ChannelId);
        Self = userVms.GetValueOrDefault(Mirror.SelfId);
        RefreshSpeaking();
        LastChange = change;
        StateChanged?.Invoke();
    }

    IEnumerable<ChannelInfo> ServerOrder() => Mirror.Channels.Values.OrderBy(c => c.Order).ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase);

    public IReadOnlyList<ChannelViewModel> LinkCandidates(ChannelViewModel channel)
    {
        var linked = Mirror.LinkedChannels(channel.Id).ToHashSet();
        return Channels.Live().Where(c => c != channel && !c.IsSeparator && !linked.Contains(c.Id)).ToList(); // Package 111
    }

    public IReadOnlyList<ChannelViewModel> LinkedChannelVms(ChannelViewModel channel)
    {
        var linked = Mirror.LinkedChannels(channel.Id).ToHashSet();
        return Channels.Live().Where(c => linked.Contains(c.Id)).ToList();
    }

    // ---- Speaking indicators ----

    /// <summary>Package 57: a pause longer than this starts a new link transmission, with a new tone.</summary>
    public static readonly TimeSpan LinkPause = TimeSpan.FromMilliseconds(300);

    readonly Dictionary<uint, DateTimeOffset> lastLinkVoice = [];

    public void OnSpeakers(IEnumerable<ActiveSpeaker> active)
    {
        var now = time.GetUtcNow();
        foreach (var a in active)
        {
            speaking[a.SessionId] = (now, a.ViaLink);
            // Someone from another channel talks in over a link: without a tone it sounds like the own channel.
            if (!a.ViaLink || a.SessionId == Mirror.SelfId || !Mirror.Users.ContainsKey(a.SessionId)) continue;
            if (!lastLinkVoice.TryGetValue(a.SessionId, out var last) || now - last > LinkPause) SoundRequested?.Invoke(SoundEvent.LinkVoice);
            lastLinkVoice[a.SessionId] = now;
        }
        RefreshSpeaking();
    }

    DateTimeOffset? ownLinkEnded;

    public void SetSelfTransmitting(byte? target)
    {
        const byte Linked = Shared.Voice.VoiceHeader.TargetLinked;
        var now = time.GetUtcNow();
        // Package 60: the own voice starts going over a link; only then do other channels really hear it
        if (target == Linked && selfTarget != Linked && CurrentChannel is { IsLinked: true }
            && (ownLinkEnded is not { } ended || now - ended > LinkPause))
            SoundRequested?.Invoke(SoundEvent.OwnLinkVoice);
        if (selfTarget == Linked && target != Linked) ownLinkEnded = now;
        selfTarget = target;
        RefreshSpeaking();
    }

    /// <summary>Package 122: the users shown speaking; only they, myself and those reported since can change.</summary>
    readonly HashSet<uint> shownSpeaking = [];

    public void RefreshSpeaking()
    {
        var now = time.GetUtcNow();
        foreach (var gone in speaking.Where(s => now - s.Value.At >= SpeakingHold).Select(s => s.Key).ToList()) speaking.Remove(gone);
        foreach (var id in shownSpeaking.Union(speaking.Keys).Append(Mirror.SelfId).ToList())
        {
            var (on, viaLink) = id == Mirror.SelfId ? (selfTarget is not null, selfTarget == Shared.Voice.VoiceHeader.TargetLinked)
                : speaking.TryGetValue(id, out var s) ? (true, s.ViaLink) : (false, false);
            if (on) shownSpeaking.Add(id);
            else shownSpeaking.Remove(id);
            if (!userVms.TryGetValue(id, out var user) || (user.IsSpeaking, user.IsSpeakingViaLink) == (on && !viaLink, on && viaLink)) continue;
            user.SetSpeaking(on, viaLink);
            SpeakingTouches++;
        }
    }

    // ---- Requests ----

    /// <summary>Sends a request and returns the id it got, so an answer or error can be matched (Package 73).</summary>
    /// <param name="id">A fixed id instead of a new one, e.g. the round a later list page belongs to.</param>
    /// <param name="pending">Package 97: marked running under the request's id before sending; the server's error for it ends it.</param>
    public async Task<string> SendAsync(Request request, string? id = null, Pending? pending = null)
    {
        id ??= $"{(request is SendChat ? ChatRequestPrefix : "r")}{++requestCounter}";
        if (pending is not null)
        {
            foreach (var over in waiting.Where(w => !w.Value.Answers(w.Key)).Select(w => w.Key).ToList()) waiting.Remove(over);
            pending.Start(id); // before sending: the answer can come at once
            waiting[id] = pending;
        }
        try
        {
            await send(request with { RequestId = id });
        }
        // InvalidOperationException: a request raced a disconnect that had already shut TLS down.
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException or InvalidOperationException)
        {
            var text = ErrorTexts.For(Codes.ConnectionLost);
            if (pending?.Answers(id) == true) pending.Fail(text);
            Notice?.Invoke(text);
        }
        return id;
    }

    // ---- Package 97 (A113): waiting states ----

    /// <summary>The running marks by request id; the server's error for one of them ends it.</summary>
    readonly Dictionary<string, Pending> waiting = [];

    /// <summary>
    /// Runs work on the UI thread; the main view model sets its dispatcher. Made on Avalonia's UI thread (headless tests)
    /// it posts there, otherwise (view model tests with a manual clock) it runs inline.
    /// </summary>
    public Action<Action> Post { get; set; } = SynchronizationContext.Current is Avalonia.Threading.AvaloniaSynchronizationContext ui
        ? action => ui.Post(_ => action(), null)
        : action => action();

    /// <summary>A waiting mark on this server's clock, its timers handed to the UI thread.</summary>
    public Pending NewPending() => new(time, action => Post(action));

    /// <summary>Package 98: marks that end with a message showing the action done (most requests have no answer of their own).</summary>
    readonly List<(Pending Pending, Func<Message, bool> Confirms)> confirming = [];

    /// <summary>
    /// Package 98: sends under a waiting mark that ends once a message confirms it, or with the server's error for it or
    /// the timeout. Unless a dialog shows it (notify false), the timeout's error shows as a notice like the server's errors.
    /// </summary>
    /// <param name="busy">Gets IsBusy of the mark, e.g. for the spinner on a row.</param>
    public Pending SendConfirmed(Request request, Func<Message, bool> confirms, Pending? pending = null, Action<bool>? busy = null, bool notify = true)
    {
        pending ??= NewPending();
        if (busy is not null) pending.Show(busy);
        if (notify) pending.TimedOut += () => Notice?.Invoke(Strings.Pending_NoAnswer);
        confirming.Add((pending, confirms));
        _ = SendAsync(request, pending: pending);
        return pending;
    }

    /// <summary>Package 98: asks with a dialog that sends itself (see <see cref="Dialogs"/>); a dialog that cannot wait has it sent afterwards.</summary>
    internal static async Task Ask<T>(Func<Func<T, Pending>, Task<T?>>? ask, Func<T, Pending> send) where T : class
    {
        if (ask is null) return;
        Pending? sent = null;
        if (await ask(value => sent = send(value)) is { } result && sent is null) send(result);
    }

    bool IsSelf(UserInfo user) => user.SessionId == Mirror.SelfId;

    /// <summary>Package 94: asks for the password of a password-locked channel unless remembered, the bypass right or an admin.</summary>
    public async Task JoinAsync(Guid channelId)
    {
        if (IsJoining) return; // Package 98
        string? password = null;
        if (Mirror.Channels.GetValueOrDefault(channelId) is { HasPassword: true } channel && !IsAdmin
            && !SelfPermissions.Has(Permission.ChannelPasswordBypass) && Mirror.Self?.ChannelId != channelId
            && !channelPasswords.TryGetValue(channelId, out password))
        {
            if (Dialogs.AskChannelPassword is not { } ask || await ask(channel.Name) is not { Length: > 0 } entered) return;
            password = entered;
        }
        pendingJoin = channelId;
        pendingPassword = password is null ? null : (channelId, password);
        if (Mirror.Self?.ChannelId is not { } before || before == channelId)
        {
            await SendAsync(new JoinChannel(channelId, password));
            return;
        }
        // Package 98: the row spins until the own channel changes; a second join meanwhile is ignored
        var row = channelVms.GetValueOrDefault(channelId);
        joining = SendConfirmed(new JoinChannel(channelId, password), m => m is UserUpdated u && IsSelf(u.User) && u.User.ChannelId != before,
            busy: on => row?.IsBusy = on);
    }

    Pending? joining;
    public bool IsJoining => joining?.IsRunning == true;

    /// <summary>Package 139 (A148): the password that let me into a channel on this connection, kept in memory only.</summary>
    internal string? PasswordFor(Guid channelId) => channelPasswords.GetValueOrDefault(channelId);

    /// <summary>Package 139: back into the channel from before a reconnect, with its password from then and never a dialog.</summary>
    internal Task RejoinAsync(Guid channelId, string? password)
    {
        pendingJoin = channelId;
        pendingPassword = password is null ? null : (channelId, password);
        return SendAsync(new JoinChannel(channelId, password));
    }

    /// <summary>Package 110: names may repeat, so the own channel is the one the server answers this request with.</summary>
    public Pending CreateChannel(ChannelEdit e)
    {
        var pending = NewPending();
        return SendConfirmed(new CreateChannel(e.Name, e.Description, e.IsMuted, e.MaxUsers, e.AllowedGroupIds, e.Password, e.Kind, e.Bitrate),
            m => m is ChannelAdded { RequestId: { } id } && pending.Answers(id), pending, notify: false);
    }
    public Pending EditChannel(Guid id, ChannelEdit edit, int order) =>
        SendConfirmed(new EditChannel(id, edit.Name, edit.Description, order, edit.IsMuted, edit.MaxUsers, edit.AllowedGroupIds, edit.Password, edit.Bitrate),
            m => m is ChannelUpdated u && u.Channel.Id == id, notify: false);
    public Pending DeleteChannel(Guid id)
    {
        var row = channelVms.GetValueOrDefault(id); // gone from the tree when confirmed
        return SendConfirmed(new DeleteChannel(id), m => m is ChannelRemoved r && r.ChannelId == id, busy: on => row?.IsBusy = on);
    }

    /// <summary>The order sent last, shown until the server confirms or refuses it.</summary>
    List<Guid>? pendingOrder;

    /// <summary>
    /// Package 36: puts source right before or after target and sends the complete new order. Package 98: the new order
    /// shows at once and the moved row spins until the server's updates match it; refused or unanswered, it goes back.
    /// </summary>
    public Task MoveChannelAsync(ChannelViewModel source, ChannelViewModel target, bool after)
    {
        if (source == target) return Task.CompletedTask;
        var live = Channels.Live().ToList();
        if (!live.Contains(source) || !live.Contains(target)) return Task.CompletedTask; // Package 102: folding away
        var order = live.Where(c => c != source).ToList();
        order.Insert(order.IndexOf(target) + (after ? 1 : 0), source);
        if (order.SequenceEqual(live)) return Task.CompletedTask;
        var ids = order.Select(c => c.Id).ToList();
        pendingOrder = ids;
        Rebuild();
        var sent = SendConfirmed(new ReorderChannels(ids), _ => ServerOrder().Select(c => c.Id).SequenceEqual(ids), busy: on => source.IsBusy = on);
        _ = EndReorderAsync(sent, ids);
        return Task.CompletedTask;
    }

    async Task EndReorderAsync(Pending sent, List<Guid> ids)
    {
        await sent.Completion;
        if (pendingOrder != ids) return; // a newer order is on its way
        pendingOrder = null;
        Rebuild(); // done: the server's order is the same; refused or no answer: back to it
    }

    public Pending Link(Guid a, Guid b) =>
        SendConfirmed(new LinkChannels(a, b), m => m is ChannelsLinked l && (l.A, l.B) is var p && (p == (a, b) || p == (b, a)), notify: false);
    public Pending Unlink(Guid a, Guid b) =>
        SendConfirmed(new UnlinkChannels(a, b), m => m is ChannelsUnlinked l && (l.A, l.B) is var p && (p == (a, b) || p == (b, a)), notify: false);
    /// <summary>Package 120: without the dialog that waited, the moved user's row spins until the server shows the move.</summary>
    public Pending Move(uint sessionId, Guid channelId)
    {
        var row = userVms.GetValueOrDefault(sessionId);
        return SendConfirmed(new MoveUser(sessionId, channelId), m => m is UserUpdated u && u.User.SessionId == sessionId && u.User.ChannelId == channelId,
            busy: on => row?.IsBusy = on, notify: false);
    }

    /// <summary>
    /// Package 120 (A129): where a dragged user row may be dropped. Never the user's own channel (the server would silently
    /// do nothing) and never a separator; locked or full channels count, the server refuses them as from the menu.
    /// </summary>
    public static bool IsDropTarget(UserViewModel user, ChannelViewModel channel) => user.CanDrag && channel.Id != user.ChannelId && !channel.IsSeparator;

    /// <summary>Package 120 (A129): my own row joins the channel (with its password prompt), another user is moved there. Ends with the server's answer.</summary>
    public async Task DropUser(UserViewModel user, ChannelViewModel channel)
    {
        // gone from the server while dragged (or the row stands for an earlier entry): nobody to move
        if (!IsDropTarget(user, channel) || !Mirror.Users.ContainsKey(user.SessionId) || userVms.GetValueOrDefault(user.SessionId) != user) return;
        if (!user.IsSelf)
        {
            await Move(user.SessionId, channel.Id).Completion;
            return;
        }
        await channel.JoinCommand.ExecuteAsync(null);
        if (joining is { } join) await join.Completion;
    }
    public Pending Kick(uint sessionId, string reason) => SendConfirmed(new Kick(sessionId, reason), m => m is UserLeft l && l.SessionId == sessionId, notify: false);
    public Pending Ban(uint sessionId, BanChoice choice) =>
        SendConfirmed(new Ban(sessionId, choice.Reason, choice.DurationMinutes, choice.IncludeIp), m => m is UserLeft l && l.SessionId == sessionId, notify: false);
    public Pending ServerMute(uint sessionId, bool muted)
    {
        var row = userVms.GetValueOrDefault(sessionId);
        return SendConfirmed(new SetServerMute(sessionId, muted), m => m is UserUpdated u && u.User.SessionId == sessionId && u.User.ServerMuted == muted,
            busy: on => row?.IsBusy = on);
    }

    /// <summary>Package 119: confirmed by the user's update showing the group; the server answers nothing else.</summary>
    public Pending SetUserGroup(string fingerprint, uint sessionId, Guid groupId, bool assigned)
    {
        var row = userVms.GetValueOrDefault(sessionId);
        return SendConfirmed(assigned ? new AssignGroup(fingerprint, groupId) : new UnassignGroup(fingerprint, groupId),
            m => m is UserUpdated u && u.User.SessionId == sessionId && u.User.GroupIds.Contains(groupId) == assigned, busy: on => row?.IsBusy = on);
    }

    /// <summary>Package 53: both at once and without tones, for the self test and to restore the state before it.</summary>
    public Task SetSelfStateAsync(bool muted, bool deafened)
    {
        mutedByHold = false;
        if (deafened && !SelfDeafened) mutedBeforeDeafen = SelfMuted;
        SelfDeafened = deafened;
        SelfMuted = muted || deafened;
        return SendAsync(new SetSelfState(SelfMuted, SelfDeafened));
    }

    /// <summary>Package 125: push-to-mute muted, so letting go unmutes. A choice made otherwise meanwhile clears it and sticks.</summary>
    bool mutedByHold;

    /// <summary>Package 139 (review): a reconnect brings back the own choice, not the mute of a key that may be let go meanwhile.</summary>
    internal bool MutedByHold => mutedByHold;
    /// <summary>Package 139 (review): undeafening after a reconnect must not turn on a microphone that was muted before.</summary>
    internal bool MutedBeforeDeafen => mutedBeforeDeafen;

    /// <summary>
    /// Package 139 (review): the own state from the connection before, in one SetSelfState and without tones. A mute of the
    /// key still held now (Package 125) stays, so letting go unmutes as usual.
    /// </summary>
    internal Task RestoreSelfStateAsync(bool muted, bool deafened, bool mutedBeforeDeafen)
    {
        if (muted || deafened) mutedByHold = false; // a chosen mute outlasts the key's release
        this.mutedBeforeDeafen = mutedBeforeDeafen;
        SelfDeafened = deafened;
        SelfMuted = muted || deafened || mutedByHold;
        return SendAsync(new SetSelfState(SelfMuted, SelfDeafened));
    }

    /// <summary>
    /// Package 125: holding push-to-mute does what the mute button does (tone, server), letting go undoes exactly that.
    /// Already muted or deafened, it changes nothing.
    /// </summary>
    public Task HoldMute(bool held)
    {
        if (held ? SelfMuted : !mutedByHold) return Task.CompletedTask;
        mutedByHold = held;
        SelfMuted = held;
        SoundRequested?.Invoke(held ? SoundEvent.MicOff : SoundEvent.MicOn);
        return SendAsync(new SetSelfState(SelfMuted, SelfDeafened));
    }

    [RelayCommand]
    Task ToggleMute()
    {
        mutedByHold = false;
        if (SelfDeafened)
        {
            SelfDeafened = false;
            SelfMuted = false;
            SoundRequested?.Invoke(SoundEvent.SoundOn);
        }
        else
        {
            SelfMuted = !SelfMuted;
            SoundRequested?.Invoke(SelfMuted ? SoundEvent.MicOff : SoundEvent.MicOn);
        }
        return SendAsync(new SetSelfState(SelfMuted, SelfDeafened));
    }

    [RelayCommand]
    Task ToggleDeafen()
    {
        mutedByHold = false;
        if (SelfDeafened)
        {
            SelfDeafened = false;
            SelfMuted = mutedBeforeDeafen;
        }
        else
        {
            mutedBeforeDeafen = SelfMuted;
            SelfDeafened = true;
            SelfMuted = true;
        }
        SoundRequested?.Invoke(SelfDeafened ? SoundEvent.SoundOff : SoundEvent.SoundOn);
        return SendAsync(new SetSelfState(SelfMuted, SelfDeafened));
    }

    [RelayCommand]
    Task NewChannel() =>
        Ask<ChannelEdit>(Dialogs.EditChannel is { } edit ? submit => edit(new ChannelEdit("", ""), ChannelDialogMode.Create, submit) : null, CreateChannel);

    /// <summary>Package 98: the dialog waits until the own user comes back with the new groups (or the token is refused).</summary>
    [RelayCommand]
    Task RedeemToken() =>
        Ask<string>(Dialogs.AskText is { } ask ? submit => ask(Strings.Dialog_RedeemToken, Strings.Dialog_RedeemTokenPrompt, submit) : null,
            token => SendConfirmed(new RedeemAdminToken(token.Trim()), m => m is UserUpdated u && IsSelf(u.User), notify: false));
}

public sealed partial class ChannelViewModel(ServerViewModel owner, Guid id) : ObservableObject
{
    [ObservableProperty] string name = "";
    [ObservableProperty] string description = "";
    [ObservableProperty] int order;
    [ObservableProperty] bool isLinked;
    [ObservableProperty] string linkedNames = "";
    [ObservableProperty] bool isCurrent;
    [ObservableProperty] bool isDefault;
    /// <summary>Package 34: nobody in this channel is heard.</summary>
    [ObservableProperty] bool isMuted;
    /// <summary>Package 35: 0 = unlimited.</summary>
    [ObservableProperty] int maxUsers;
    /// <summary>Package 132 (A141): kbps its senders encode with; 0 for a separator.</summary>
    [ObservableProperty] int bitrate = ProtocolInfo.DefaultBitrate;
    /// <summary>"3/5" for a limited channel, otherwise just the count.</summary>
    [ObservableProperty] string slotText = "0";
    [ObservableProperty] bool canCreate;
    /// <summary>The right to edit channels; also moves them (up, down, drag), separators too.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditDetails))]
    bool canEdit;
    /// <summary>Package 111: a line in the list that nobody can join, link or edit.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditDetails))]
    [NotifyPropertyChangedFor(nameof(IsVoice))]
    bool isSeparator;
    public bool IsVoice => !IsSeparator;
    /// <summary>"Bearbeiten": name and options, which a separator does not have.</summary>
    public bool CanEditDetails => CanEdit && !IsSeparator;
    [ObservableProperty] bool canDelete;
    [ObservableProperty] bool canLink;
    [ObservableProperty] bool canUnlink;
    [ObservableProperty] bool canMoveUp;
    [ObservableProperty] bool canMoveDown;
    /// <summary>Package 93: the lock icon behind the name and its tooltip.</summary>
    [ObservableProperty] bool isLocked;
    [ObservableProperty] string lockText = "";
    /// <summary>Package 93: "Beitreten" and the double click, only with one of the lock's groups or as admin (a password is asked for).</summary>
    [ObservableProperty] bool canJoin = true;
    /// <summary>Package 98: a spinner on the row while joining it, moving it or deleting it waits for the server.</summary>
    [ObservableProperty] bool isBusy;
    /// <summary>Package 93: null = no group lock, empty = admins only.</summary>
    public IReadOnlyList<Guid>? AllowedGroupIds { get; private set; }
    /// <summary>Package 94: joining needs a password (without the bypass right).</summary>
    public bool HasPassword { get; private set; }

    public Guid Id { get; } = id;
    public ObservableCollection<UserViewModel> Users { get; } = [];
    /// <summary>Package 112: null without description and links, so no empty tooltip opens.</summary>
    public string? Tooltip
    {
        get
        {
            if (IsSeparator) return null;
            var text = IsLinked ? $"{Description}{(char)10}{string.Format(Strings.Tooltip_LinkedWith, LinkedNames)}".Trim() : Description.Trim();
            return text.Length > 0 ? text : null;
        }
    }

    internal void Update(ChannelInfo info, IReadOnlyList<string> linked, bool isDefault, bool isCurrent, Permission actor)
    {
        IsSeparator = info.Kind == ChannelKind.Separator;
        Name = info.Name;
        Description = info.Description;
        Order = info.Order;
        IsLinked = linked.Count > 0;
        LinkedNames = string.Join(", ", linked);
        IsDefault = isDefault;
        IsCurrent = isCurrent;
        IsMuted = info.IsMuted;
        MaxUsers = info.MaxUsers;
        Bitrate = info.Bitrate;
        CanCreate = actor.Has(Permission.ChannelCreate);
        CanEdit = actor.Has(Permission.ChannelEdit);
        CanDelete = actor.Has(Permission.ChannelDelete) && !isDefault;
        CanLink = actor.Has(Permission.ChannelLink) && !IsSeparator;
        CanUnlink = CanLink && IsLinked;
        OnPropertyChanged(nameof(Tooltip));
    }

    /// <summary>Package 93 (A108, A109): everyone sees the lock; the own groups decide whether joining is offered.</summary>
    internal void UpdateLock(ChannelInfo info, IReadOnlyList<GroupInfo> groups, IReadOnlyList<Guid> ownGroups, bool isAdmin)
    {
        AllowedGroupIds = info.AllowedGroupIds;
        HasPassword = info.HasPassword;
        IsLocked = info.AllowedGroupIds is not null || info.HasPassword;
        var groupText = info.AllowedGroupIds switch
        {
            null => null,
            [] => Strings.Ui_LockedAdmins,
            var ids => string.Format(Strings.Ui_LockedGroupsFmt,
                string.Join(", ", groups.Where(g => ids.Contains(g.Id)).Select(g => g.Name))), // in the server's group order
        };
        LockText = string.Join("\n", new[] { groupText, info.HasPassword ? Strings.Ui_LockedPassword : null }.OfType<string>());
        CanJoin = info.Kind == ChannelKind.Voice && (isAdmin || info.AllowedGroupIds is not { } allowed || ownGroups.Any(allowed.Contains));
    }

    bool first, last;

    internal void SetPosition(bool first, bool last)
    {
        this.first = first;
        this.last = last;
        CanMoveUp = CanEdit && !first;
        CanMoveDown = CanEdit && !last;
    }

    [RelayCommand]
    Task MoveUp() => first ? Task.CompletedTask : owner.MoveChannelAsync(this, Neighbour(-1), after: false);

    [RelayCommand]
    Task MoveDown() => last ? Task.CompletedTask : owner.MoveChannelAsync(this, Neighbour(+1), after: true);

    ChannelViewModel Neighbour(int step)
    {
        var live = owner.Channels.Live().ToList();
        return live[live.IndexOf(this) + step];
    }

    [RelayCommand]
    Task Join()
    {
        if (IsSeparator) return Task.CompletedTask; // Package 111: a double click on the line does nothing
        if (CanJoin) return owner.JoinAsync(Id);
        owner.ShowNotice(ErrorTexts.For(Codes.ChannelLocked)); // Package 93: the server would refuse it anyway
        return Task.CompletedTask;
    }

    [RelayCommand]
    Task Create() => owner.NewChannelCommand.ExecuteAsync(null);

    [RelayCommand]
    Task Edit()
    {
        var mode = IsDefault ? ChannelDialogMode.EditDefault : ChannelDialogMode.Edit;
        var current = new ChannelEdit(Name, Description, IsMuted, MaxUsers, AllowedGroupIds, HasPassword, Bitrate: Bitrate);
        return ServerViewModel.Ask<ChannelEdit>(owner.Dialogs.EditChannel is { } edit ? submit => edit(current, mode, submit) : null,
            result => owner.EditChannel(Id, result, Order));
    }

    [RelayCommand]
    async Task Delete()
    {
        var question = IsSeparator ? Strings.Confirm_DeleteSeparator : string.Format(Strings.Confirm_DeleteChannel, Name);
        if (owner.Dialogs.Confirm is { } confirm && await confirm(question))
            owner.DeleteChannel(Id);
    }

    [RelayCommand]
    Task Link() =>
        ServerViewModel.Ask<ChannelViewModel>(owner.Dialogs.PickChannel is { } pick
            ? submit => pick(string.Format(Strings.Dialog_LinkWith, Name), owner.LinkCandidates(this), submit) : null, other => owner.Link(Id, other.Id));

    [RelayCommand]
    Task Unlink() =>
        ServerViewModel.Ask<ChannelViewModel>(owner.Dialogs.PickChannel is { } pick
            ? submit => pick(string.Format(Strings.Dialog_Unlink, Name), owner.LinkedChannelVms(this), submit) : null, other => owner.Unlink(Id, other.Id));
}

public sealed partial class UserViewModel(ServerViewModel owner, uint sessionId) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumeLabel))]
    string nickname = "";
    [ObservableProperty] string fingerprint = "";
    [ObservableProperty] string statusText = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GroupTip))]
    string groupNames = "";
    /// <summary>Package 112: the row tooltip, null in no known group (an empty text opened an empty box).</summary>
    public string? GroupTip => GroupNames.Length > 0 ? GroupNames : null;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdjustVolume))]
    bool isSelf;
    [ObservableProperty] bool serverMuted;
    [ObservableProperty] bool isDeafened;
    [ObservableProperty] bool isSelfMutedOnly;
    [ObservableProperty] bool isSpeaking;
    [ObservableProperty] bool isSpeakingViaLink;
    [ObservableProperty] bool canMove;
    [ObservableProperty] bool canMute;
    [ObservableProperty] bool canKick;
    [ObservableProperty] bool canBan;
    [ObservableProperty] bool canMessage;
    /// <summary>Package 98: a spinner on the row while the server mute (Package 119: or a group change) waits for the server.</summary>
    [ObservableProperty] bool isBusy;
    /// <summary>Package 119 (A128): the context menu's groups with their ticks, empty when I may not change this user's groups.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGroups))]
    IReadOnlyList<GroupToggle> groups = [];
    public bool HasGroups => Groups.Count > 0;

    /// <summary>
    /// Package 119: group changes sent from the menu, ticked as asked until the server shows or refuses them. Per group only
    /// the last click counts, an earlier one's answer leaves it be. Touched on the UI thread only.
    /// </summary>
    readonly Dictionary<Guid, (bool On, Pending Sent)> pendingGroups = [];

    /// <summary>Package 51: this person's volume for me, 0 to 200 %.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVolumeChanged), nameof(IsLocallyMuted), nameof(VolumeText))]
    double volumePercent = 100;

    bool showingStored;

    public bool CanAdjustVolume => !IsSelf;
    public bool IsVolumeChanged => VolumePercent is > 0 and not 100;
    public bool IsLocallyMuted => VolumePercent == 0;
    public string VolumeText => IsLocallyMuted ? Strings.Ui_LocallyMuted : string.Format(Strings.Ui_UserVolumeFmt, VolumePercent);
    public string VolumeLabel => string.Format(Strings.Ui_UserVolumeOfFmt, Nickname);

    /// <summary>Shows the stored volume without reporting it as a change.</summary>
    internal void ShowVolume(float volume)
    {
        showingStored = true;
        VolumePercent = Math.Round(volume * 100);
        showingStored = false;
    }

    partial void OnVolumePercentChanged(double value)
    {
        double bounded = Math.Clamp(value, 0, ClientSettings.MaxUserVolume * 100);
        if (bounded != value) VolumePercent = bounded;
        else if (!showingStored) owner.OnVolumeChanged(this);
    }

    [RelayCommand]
    void ResetVolume() => VolumePercent = 100;

    public uint SessionId { get; } = sessionId;
    public Guid ChannelId { get; private set; }

    internal void Update(UserInfo info, bool isSelf, Permission actor, IReadOnlyList<GroupInfo> serverGroups)
    {
        owner.UserUpdates++;
        Nickname = info.Nickname;
        Fingerprint = info.Fingerprint;
        ShowVolume(owner.VolumeOf(info.Fingerprint));
        ChannelId = info.ChannelId;
        IsSelf = isSelf;
        ServerMuted = info.ServerMuted;
        IsDeafened = info.SelfDeafened;
        // Deafened implies muted and a server mute has its own icon, so the plain mic-off icon only shows for a mute on its own.
        IsSelfMutedOnly = info.SelfMuted && !info.SelfDeafened && !info.ServerMuted;
        GroupNames = string.Join(", ", serverGroups.Where(g => info.GroupIds.Contains(g.Id)).Select(g => g.Name)); // in the server's group order (Package 37)
        StatusText = info switch
        {
            { ServerMuted: true } => Strings.UserStatus_ServerMuted,
            { SelfDeafened: true } => Strings.UserStatus_Deafened,
            { SelfMuted: true } => Strings.UserStatus_Muted,
            _ => "",
        };
        // Package 84 (A102): never oneself (moving oneself is joining), only strictly weaker users.
        // Package 92: the server judges the rank per recipient, the client never sees others' rights.
        bool rank = !isSelf && info.CanBeModeratedByMe;
        CanMove = actor.Has(Permission.UserMove) && rank;
        CanMute = actor.Has(Permission.UserMute) && rank;
        CanKick = actor.Has(Permission.UserKick) && rank;
        CanBan = actor.Has(Permission.UserBan) && rank;
        CanMessage = actor.Has(Permission.ChatPrivate) && !isSelf;
        // Package 119 (A128): the administration's rule; the rank also keeps the last admin's Admin group, an admin is never weaker
        var wanted = actor.Has(Permission.GroupsAssign) && rank
            ? serverGroups.Select(g => (g.Id, g.Name, Checked: pendingGroups.TryGetValue(g.Id, out var p) ? p.On : info.GroupIds.Contains(g.Id), g.AssignableByMe)).ToList()
            : [];
        // compared with the live ticks: a refused click is put back, and an open menu is not rebuilt by unrelated updates
        if (!wanted.SequenceEqual(Groups.Select(t => (t.GroupId, t.Name, t.IsChecked, t.IsEnabled))))
            Groups = wanted.Select(w => new GroupToggle(w.Id, w.Name, w.Checked, w.AssignableByMe, ToggleGroup)).ToList();
    }

    /// <summary>Package 119: IsChecked already holds the new value; the row spins until the server shows it.</summary>
    Task ToggleGroup(GroupToggle toggle)
    {
        var sent = owner.SetUserGroup(Fingerprint, SessionId, toggle.GroupId, toggle.IsChecked);
        pendingGroups[toggle.GroupId] = (toggle.IsChecked, sent);
        _ = EndGroupChangeAsync(toggle.GroupId, sent);
        return Task.CompletedTask;
    }

    async Task EndGroupChangeAsync(Guid groupId, Pending sent)
    {
        await sent.Completion;
        if (pendingGroups.GetValueOrDefault(groupId).Sent == sent) pendingGroups.Remove(groupId);
        if (pendingGroups.Values.Any(p => p.Sent.IsBusy)) IsBusy = true; // this one ending cleared the spinner a later click still needs
        owner.Rebuild(); // done: the server's state is shown already; refused or no answer: the tick goes back
    }

    internal void SetSpeaking(bool on, bool viaLink)
    {
        IsSpeaking = on && !viaLink;
        IsSpeakingViaLink = on && viaLink;
    }

    /// <summary>Package 120 (A129): the context menu's "Verschieben" submenu, empty when I may not move this user.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMoveTargets))]
    IReadOnlyList<MoveTarget> moveTargets = [];
    public bool HasMoveTargets => MoveTargets.Count > 0;

    /// <summary>Package 120: my own row (joining) or one I may move can be dragged onto a channel.</summary>
    public bool CanDrag => IsSelf || CanMove;

    /// <summary>The channels in tree order, without the user's own (a silent no-op on the server) and separators (Package 111).</summary>
    internal void UpdateMoveTargets(IReadOnlyList<ChannelViewModel> channels)
    {
        var wanted = CanMove ? channels.Where(c => c.Id != ChannelId && !c.IsSeparator).ToList() : [];
        // only on a change, so an open menu is not rebuilt by unrelated updates
        if (!wanted.SequenceEqual(MoveTargets.Select(t => t.Channel)))
            MoveTargets = wanted.Select(c => new MoveTarget(c, new RelayCommand(() => owner.Move(SessionId, c.Id)))).ToList();
    }

    [RelayCommand]
    void Message() => owner.RequestPrivateChat(this);

    [RelayCommand]
    Task ToggleServerMute()
    {
        owner.ServerMute(SessionId, !ServerMuted);
        return Task.CompletedTask;
    }

    [RelayCommand]
    Task Kick() =>
        ServerViewModel.Ask<string>(owner.Dialogs.AskText is { } ask
            ? submit => ask(string.Format(Strings.Dialog_Kick, Nickname), Strings.Dialog_KickReason, submit) : null, reason => owner.Kick(SessionId, reason));

    [RelayCommand]
    Task Ban() =>
        ServerViewModel.Ask<BanChoice>(owner.Dialogs.Ban is { } ban ? submit => ban(Nickname, true, submit) : null, choice => owner.Ban(SessionId, choice));
}

/// <summary>Package 120 (A129): one channel in a user's "Verschieben" submenu; the command moves the user there.</summary>
public sealed record MoveTarget(ChannelViewModel Channel, System.Windows.Input.ICommand Command);
