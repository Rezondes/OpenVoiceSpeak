using OVS.Client.Collections;
using OVS.Client.Dialogs;
using OVS.Client.Localization;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OVS.Client.Audio.Playback;
using OVS.Client.Audio.Sounds;
using OVS.Client.Models;
using OVS.Client.Net;
using OVS.Client.Services;
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

    public ServerViewModel(StateMirror mirror, Func<Request, Task> send, TimeProvider time, ServerDialogs? dialogs = null)
    {
        Mirror = mirror;
        this.send = send;
        this.time = time;
        Dialogs = dialogs ?? new ServerDialogs();
        Leave = new LeaveTimer(time, () => Post);
        Rebuild();
    }

    public StateMirror Mirror { get; }
    public TimeProvider Time => time;
    public ServerDialogs Dialogs { get; }
    public ObservableCollection<ChannelViewModel> Channels { get; } = [];

    /// <summary>Package 102: how long a user or channel that is gone stays to fold away (the window sets it).</summary>
    public LeaveTimer Leave { get; }

    public bool CanCreateChannel => SelfPermissions.Has(Permission.ChannelCreate);
    public bool HasSpeakLinked => SelfPermissions.Has(Permission.SpeakLinked);
    /// <summary>The own channel is muted, so nobody hears anybody there.</summary>
    public bool InMutedChannel => CurrentChannel?.IsMuted == true;
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

    /// <summary>The file transfer of this server's administration, wired to its requests, routes, notices and clock.</summary>
    internal AdminTransfer NewAdminTransfer() => new((request, route) => SendAsync(request, route: route), Routes.RemoveAll, ShowNotice, time);

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
                if (e.Code is ReasonCodes.WrongChannelPassword or ReasonCodes.ChannelPasswordRequired && pendingPassword is { } wrong)
                {
                    channelPasswords.Remove(wrong.Channel); // Package 94: changed meanwhile, ask again next time
                    pendingPassword = null;
                }
                var text = ErrorTexts.For(e.Code, e.Detail);
                if (e.RequestId is { } failed && waiting.Remove(failed, out var pending) && pending.Answers(failed)) pending.Fail(text); // Package 97
                if (e.RequestId?.StartsWith(ChatRequestPrefix) == true && ChatError is { } chatError) chatError(text);
                else Notice?.Invoke(text);
                Routes.Deliver(e.RequestId, message); // Package 73: the sender drops what the server refused
                return;
            case UserList list:
                LastUserList = list;
                Routes.Deliver(list.RequestId, message);
                return;
            case BanList list:
                LastBanList = list;
                Routes.Deliver(list.RequestId, message);
                return;
            case BackupList a: Routes.Deliver(a.RequestId, a); return; // Package 74
            case BackupChunk a: Routes.Deliver(a.RequestId, a); return; // Package 75
            case UploadBackupAck a: Routes.Deliver(a.RequestId, a); return;
            case BackupUploaded a: Routes.Deliver(a.RequestId, a); return;
            case LogList a: Routes.Deliver(a.RequestId, a); return; // Package 81
            case LogPage a: Routes.Deliver(a.RequestId, a); return;
            case LogSearchResult a: Routes.Deliver(a.RequestId, a); return;
            case LogDownloadReady a: Routes.Deliver(a.RequestId, a); return; // Package 82
            case LogChunk a: Routes.Deliver(a.RequestId, a); return;
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
        ShowUsers(channel, info, Mirror.Users.Values.Where(u => u.ChannelId == channelId), u => userVms[u.SessionId]);
    }

    /// <summary>The users inside a channel by name into its list (Package 102: one who left folds away) and its slot counter.</summary>
    void ShowUsers(ChannelViewModel channel, ChannelInfo info, IEnumerable<UserInfo> inside, Func<UserInfo, UserViewModel> viewModelOf)
    {
        var users = inside.OrderBy(u => u.Nickname, StringComparer.CurrentCultureIgnoreCase).Select(viewModelOf).ToList();
        CollectionSync.Sync(channel.Users, users, Leave);
        channel.Slots = new ChannelSlots(users.Count, info.MaxUsers);
    }

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
            channel.UpdateLock(c, groups, self?.GroupIds ?? []);

            ShowUsers(channel, c, byChannel[c.Id], u =>
            {
                if (!userVms.TryGetValue(u.SessionId, out var user)) userVms[u.SessionId] = user = new UserViewModel(this, u.SessionId);
                user.Update(u, u.SessionId == Mirror.SelfId, SelfPermissions, groups);
                return user;
            });
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
    /// <param name="route">Gets the answers to the request and the server's error for it, until its owner removes the route.</param>
    public async Task<string> SendAsync(Request request, string? id = null, PendingViewModel? pending = null, Action<Message>? route = null)
    {
        id ??= $"{(request is SendChat ? ChatRequestPrefix : "r")}{++requestCounter}";
        if (route is not null) Routes.Add(id, route); // before sending: the answer can come at once
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
            var text = ErrorTexts.For(ReasonCodes.ConnectionLost);
            if (pending?.Answers(id) == true) pending.Fail(text);
            Notice?.Invoke(text);
        }
        return id;
    }

    /// <summary>Who gets the answers to each request (by its id); the owners remove their routes.</summary>
    internal AnswerRoutes Routes { get; } = new();

    // ---- Package 97 (A113): waiting states ----

    /// <summary>The running marks by request id; the server's error for one of them ends it.</summary>
    readonly Dictionary<string, PendingViewModel> waiting = [];

    /// <summary>
    /// Runs work on the UI thread; the main view model sets its dispatcher. Made on Avalonia's UI thread (headless tests)
    /// it posts there, otherwise (view model tests with a manual clock) it runs inline.
    /// </summary>
    public Action<Action> Post { get; set; } = SynchronizationContext.Current is Avalonia.Threading.AvaloniaSynchronizationContext ui
        ? action => ui.Post(_ => action(), null)
        : action => action();

    /// <summary>A waiting mark on this server's clock, its timers handed to the UI thread.</summary>
    public PendingViewModel NewPending() => new(time, action => Post(action));

    /// <summary>Package 98: marks that end with a message showing the action done (most requests have no answer of their own).</summary>
    readonly List<(PendingViewModel Pending, Func<Message, bool> Confirms)> confirming = [];

    /// <summary>
    /// Package 98: sends under a waiting mark that ends once a message confirms it, or with the server's error for it or
    /// the timeout. Unless a dialog shows it (notify false), the timeout's error shows as a notice like the server's errors.
    /// </summary>
    /// <param name="busy">Gets IsBusy of the mark, e.g. for the spinner on a row.</param>
    public PendingViewModel SendConfirmed(Request request, Func<Message, bool> confirms, PendingViewModel? pending = null, Action<bool>? busy = null, bool notify = true)
    {
        pending ??= NewPending();
        if (busy is not null) pending.Show(busy);
        if (notify) pending.TimedOut += () => Notice?.Invoke(Strings.Pending_NoAnswer);
        confirming.RemoveAll(entry => entry.Pending == pending); // an older entry of a mark that is sent again must not confirm the new run
        confirming.Add((pending, confirms));
        _ = SendAsync(request, pending: pending);
        return pending;
    }

    /// <summary>Package 98: asks with a dialog that sends itself (see <see cref="Dialogs"/>); a dialog that cannot wait has it sent afterwards.</summary>
    internal static async Task Ask<T>(Func<Func<T, PendingViewModel>, Task<T?>>? ask, Func<T, PendingViewModel> send) where T : class
    {
        if (ask is null) return;
        PendingViewModel? sent = null;
        if (await ask(value => sent = send(value)) is { } result && sent is null) send(result);
    }

    bool IsSelf(UserInfo user) => user.SessionId == Mirror.SelfId;

    /// <summary>Package 94: asks for the password of a password-locked channel unless remembered, the bypass right or an admin.</summary>
    public async Task JoinAsync(Guid channelId)
    {
        if (IsJoining) return; // Package 98
        string? password = null;
        if (Mirror.Channels.GetValueOrDefault(channelId) is { } channel
            && ChannelAccess.NeedsPassword(channel.HasPassword, Mirror.Self?.GroupIds ?? [], SelfPermissions, moving: false)
            && Mirror.Self?.ChannelId != channelId
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

    PendingViewModel? joining;
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
    public PendingViewModel CreateChannel(ChannelEdit e)
    {
        var pending = NewPending();
        return SendConfirmed(new CreateChannel(e.Name, e.Description, e.IsMuted, e.MaxUsers, e.AllowedGroupIds, e.Password, e.Kind, e.Bitrate),
            m => m is ChannelAdded { RequestId: { } id } && pending.Answers(id), pending, notify: false);
    }
    public PendingViewModel EditChannel(Guid id, ChannelEdit edit, int order) =>
        SendConfirmed(new EditChannel(id, edit.Name, edit.Description, order, edit.IsMuted, edit.MaxUsers, edit.AllowedGroupIds, edit.Password, edit.Bitrate),
            m => m is ChannelUpdated u && u.Channel.Id == id, notify: false);
    public PendingViewModel DeleteChannel(Guid id)
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

    async Task EndReorderAsync(PendingViewModel sent, List<Guid> ids)
    {
        await sent.Completion;
        if (pendingOrder != ids) return; // a newer order is on its way
        pendingOrder = null;
        Rebuild(); // done: the server's order is the same; refused or no answer: back to it
    }

    public PendingViewModel Link(Guid a, Guid b) =>
        SendConfirmed(new LinkChannels(a, b), m => m is ChannelsLinked l && (l.A, l.B) is var p && (p == (a, b) || p == (b, a)), notify: false);
    public PendingViewModel Unlink(Guid a, Guid b) =>
        SendConfirmed(new UnlinkChannels(a, b), m => m is ChannelsUnlinked l && (l.A, l.B) is var p && (p == (a, b) || p == (b, a)), notify: false);
    /// <summary>Package 120: without the dialog that waited, the moved user's row spins until the server shows the move.</summary>
    public PendingViewModel Move(uint sessionId, Guid channelId)
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
    public PendingViewModel Kick(uint sessionId, string reason) => SendConfirmed(new KickSession(sessionId, reason), m => m is UserLeft l && l.SessionId == sessionId, notify: false);
    public PendingViewModel Ban(uint sessionId, BanChoice choice) =>
        SendConfirmed(new BanSession(sessionId, choice.Reason, choice.DurationMinutes, choice.IncludeIp), m => m is UserLeft l && l.SessionId == sessionId, notify: false);
    public PendingViewModel ServerMute(uint sessionId, bool muted)
    {
        var row = userVms.GetValueOrDefault(sessionId);
        return SendConfirmed(new SetSessionServerMute(sessionId, muted), m => m is UserUpdated u && u.User.SessionId == sessionId && u.User.ServerMuted == muted,
            busy: on => row?.IsBusy = on);
    }

    /// <summary>Package 119: confirmed by the user's update showing the group; the server answers nothing else.</summary>
    public PendingViewModel SetUserGroup(string fingerprint, uint sessionId, Guid groupId, bool assigned)
    {
        var row = userVms.GetValueOrDefault(sessionId);
        return SendConfirmed(assigned ? new AssignGroup(fingerprint, groupId) : new UnassignGroup(fingerprint, groupId),
            m => m is UserUpdated u && u.User.SessionId == sessionId && u.User.GroupIds.Contains(groupId) == assigned, busy: on => row?.IsBusy = on);
    }

    /// <summary>Package 53: both at once and without tones, for the self test and to restore the state before it.</summary>
    internal Task SetSelfStateAsync(SelfState state)
    {
        mutedByHold = false;
        if (state.Deafened && !SelfDeafened) mutedBeforeDeafen = SelfMuted;
        SelfDeafened = state.Deafened;
        SelfMuted = state.Muted || state.Deafened;
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
    internal Task RestoreSelfStateAsync(SelfState state, bool mutedBeforeDeafen)
    {
        if (state.IsQuiet) mutedByHold = false; // a chosen mute outlasts the key's release
        this.mutedBeforeDeafen = mutedBeforeDeafen;
        SelfDeafened = state.Deafened;
        SelfMuted = state.Muted || state.Deafened || mutedByHold;
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
