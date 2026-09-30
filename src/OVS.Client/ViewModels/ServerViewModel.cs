using OVS.Client.Localization;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OVS.Client.Audio;
using OVS.Client.Input;
using OVS.Client.Net;
using OVS.Client.Settings;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;

namespace OVS.Client.ViewModels;

/// <param name="AllowedGroupIds">Package 93: the channel's group lock; from the dialog null while unchanged, empty to remove it.</param>
/// <param name="HasPassword">Package 94: into the dialog, whether the channel has a password.</param>
/// <param name="Password">Package 94: out of the dialog, null = unchanged, empty = remove, else the new password.</param>
public sealed record ChannelEdit(string Name, string Description, bool IsMuted = false, int MaxUsers = 0, IReadOnlyList<Guid>? AllowedGroupIds = null,
    bool HasPassword = false, string? Password = null);

/// <summary>The channel dialog only offers the channel options when editing (Package 34).</summary>
public enum ChannelDialogMode { Create, Edit, EditDefault }
public sealed record BanChoice(string Reason, int? DurationMinutes, bool IncludeIp)
{
    public static IReadOnlyList<(string Label, int? Minutes)> Durations =>
        [(Strings.Ban_Hour, 60), (Strings.Ban_Day, 1440), (Strings.Ban_Week, 10080), (Strings.Ban_Permanent, null)];
}

/// <summary>Dialogs the view provides. Unset entries mean "cancelled" (used by tests).</summary>
public sealed class Dialogs
{
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
        Rebuild();
    }

    public StateMirror Mirror { get; }
    public TimeProvider Time => time;
    public Dialogs Dialogs { get; }
    public ObservableCollection<ChannelViewModel> Channels { get; } = [];

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
        if (Mirror.Apply(message)) Rebuild();
        if (sound is { } s) SoundRequested?.Invoke(s);
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

    public void Rebuild()
    {
        var self = Mirror.Self;
        ServerName = Mirror.Settings.Name;
        WelcomeText = Mirror.Settings.WelcomeText;
        SelfPermissions = self?.Permissions ?? Permission.None;
        IsAdmin = self?.GroupIds.Contains(WellKnownGroups.Admin) ?? false;
        var groups = Mirror.Groups;

        foreach (var id in channelVms.Keys.Except(Mirror.Channels.Keys).ToList()) channelVms.Remove(id);
        foreach (var id in userVms.Keys.Except(Mirror.Users.Keys).ToList()) userVms.Remove(id);

        var desired = new List<ChannelViewModel>();
        foreach (var c in ServerOrder().OrderBy(c => pendingOrder?.IndexOf(c.Id) is >= 0 and var i ? i : int.MaxValue)) // Package 98
        {
            if (!channelVms.TryGetValue(c.Id, out var channel)) channelVms[c.Id] = channel = new ChannelViewModel(this, c.Id);
            var linked = Mirror.LinkedChannels(c.Id).Select(id => Mirror.Channels.GetValueOrDefault(id)?.Name).OfType<string>().Order().ToList();
            channel.Update(c, linked, c.Id == Mirror.DefaultChannelId, self?.ChannelId == c.Id, SelfPermissions);
            channel.UpdateLock(c, groups, self?.GroupIds ?? [], IsAdmin);

            var users = Mirror.Users.Values.Where(u => u.ChannelId == c.Id)
                .OrderBy(u => u.Nickname, StringComparer.CurrentCultureIgnoreCase)
                .Select(u =>
                {
                    if (!userVms.TryGetValue(u.SessionId, out var user)) userVms[u.SessionId] = user = new UserViewModel(this, u.SessionId);
                    user.Update(u, u.SessionId == Mirror.SelfId, SelfPermissions, groups);
                    return user;
                })
                .ToList();
            Sync(channel.Users, users);
            channel.SlotText = c.MaxUsers > 0 ? $"{users.Count}/{c.MaxUsers}" : users.Count.ToString();
            desired.Add(channel);
        }
        Sync(Channels, desired);
        for (int i = 0; i < desired.Count; i++) desired[i].SetPosition(first: i == 0, last: i == desired.Count - 1);
        CurrentChannel = self is null ? null : channelVms.GetValueOrDefault(self.ChannelId);
        Self = userVms.GetValueOrDefault(Mirror.SelfId);
        RefreshSpeaking();
        StateChanged?.Invoke();
    }

    IEnumerable<ChannelInfo> ServerOrder() => Mirror.Channels.Values.OrderBy(c => c.Order).ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase);

    /// <summary>Moves/inserts/removes so the collection equals desired, keeping existing instances (and selection).</summary>
    static void Sync<T>(ObservableCollection<T> target, IReadOnlyList<T> desired) where T : class
    {
        for (int i = 0; i < desired.Count; i++)
        {
            int index = target.IndexOf(desired[i]);
            if (index < 0) target.Insert(i, desired[i]);
            else if (index != i) target.Move(index, i);
        }
        while (target.Count > desired.Count) target.RemoveAt(target.Count - 1);
    }

    public IReadOnlyList<ChannelViewModel> LinkCandidates(ChannelViewModel channel)
    {
        var linked = Mirror.LinkedChannels(channel.Id).ToHashSet();
        return Channels.Where(c => c != channel && !linked.Contains(c.Id)).ToList();
    }

    public IReadOnlyList<ChannelViewModel> LinkedChannelVms(ChannelViewModel channel)
    {
        var linked = Mirror.LinkedChannels(channel.Id).ToHashSet();
        return Channels.Where(c => linked.Contains(c.Id)).ToList();
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

    public void RefreshSpeaking()
    {
        var now = time.GetUtcNow();
        foreach (var (id, user) in userVms)
        {
            if (id == Mirror.SelfId)
            {
                user.SetSpeaking(selfTarget is not null, selfTarget == Shared.Voice.VoiceHeader.TargetLinked);
                continue;
            }
            bool on = speaking.TryGetValue(id, out var s) && now - s.At < SpeakingHold;
            user.SetSpeaking(on, on && s.ViaLink);
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

    public Pending CreateChannel(ChannelEdit e) =>
        SendConfirmed(new CreateChannel(e.Name, e.Description, e.IsMuted, e.MaxUsers, e.AllowedGroupIds, e.Password),
            m => m is ChannelAdded a && a.Channel.Name == e.Name, notify: false);
    public Pending EditChannel(Guid id, ChannelEdit edit, int order) =>
        SendConfirmed(new EditChannel(id, edit.Name, edit.Description, order, edit.IsMuted, edit.MaxUsers, edit.AllowedGroupIds, edit.Password),
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
        var order = Channels.Where(c => c != source).ToList();
        order.Insert(order.IndexOf(target) + (after ? 1 : 0), source);
        if (order.SequenceEqual(Channels)) return Task.CompletedTask;
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
    public Pending Move(uint sessionId, Guid channelId) =>
        SendConfirmed(new MoveUser(sessionId, channelId), m => m is UserUpdated u && u.User.SessionId == sessionId && u.User.ChannelId == channelId, notify: false);
    public Pending Kick(uint sessionId, string reason) => SendConfirmed(new Kick(sessionId, reason), m => m is UserLeft l && l.SessionId == sessionId, notify: false);
    public Pending Ban(uint sessionId, BanChoice choice) =>
        SendConfirmed(new Ban(sessionId, choice.Reason, choice.DurationMinutes, choice.IncludeIp), m => m is UserLeft l && l.SessionId == sessionId, notify: false);
    public Pending ServerMute(uint sessionId, bool muted)
    {
        var row = userVms.GetValueOrDefault(sessionId);
        return SendConfirmed(new SetServerMute(sessionId, muted), m => m is UserUpdated u && u.User.SessionId == sessionId && u.User.ServerMuted == muted,
            busy: on => row?.IsBusy = on);
    }

    /// <summary>Package 53: both at once and without tones, for the self test and to restore the state before it.</summary>
    public Task SetSelfStateAsync(bool muted, bool deafened)
    {
        if (deafened && !SelfDeafened) mutedBeforeDeafen = SelfMuted;
        SelfDeafened = deafened;
        SelfMuted = muted || deafened;
        return SendAsync(new SetSelfState(SelfMuted, SelfDeafened));
    }

    [RelayCommand]
    Task ToggleMute()
    {
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
    /// <summary>"3/5" for a limited channel, otherwise just the count.</summary>
    [ObservableProperty] string slotText = "0";
    [ObservableProperty] bool canCreate;
    [ObservableProperty] bool canEdit;
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
    public string Tooltip => IsLinked ? $"{Description}\n{string.Format(Strings.Tooltip_LinkedWith, LinkedNames)}".Trim() : Description;

    internal void Update(ChannelInfo info, IReadOnlyList<string> linked, bool isDefault, bool isCurrent, Permission actor)
    {
        Name = info.Name;
        Description = info.Description;
        Order = info.Order;
        IsLinked = linked.Count > 0;
        LinkedNames = string.Join(", ", linked);
        IsDefault = isDefault;
        IsCurrent = isCurrent;
        IsMuted = info.IsMuted;
        MaxUsers = info.MaxUsers;
        CanCreate = actor.Has(Permission.ChannelCreate);
        CanEdit = actor.Has(Permission.ChannelEdit);
        CanDelete = actor.Has(Permission.ChannelDelete) && !isDefault;
        CanLink = actor.Has(Permission.ChannelLink);
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
        CanJoin = isAdmin || info.AllowedGroupIds is not { } allowed || ownGroups.Any(allowed.Contains);
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
    Task MoveUp() => first ? Task.CompletedTask : owner.MoveChannelAsync(this, owner.Channels[owner.Channels.IndexOf(this) - 1], after: false);

    [RelayCommand]
    Task MoveDown() => last ? Task.CompletedTask : owner.MoveChannelAsync(this, owner.Channels[owner.Channels.IndexOf(this) + 1], after: true);

    [RelayCommand]
    Task Join()
    {
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
        var current = new ChannelEdit(Name, Description, IsMuted, MaxUsers, AllowedGroupIds, HasPassword);
        return ServerViewModel.Ask<ChannelEdit>(owner.Dialogs.EditChannel is { } edit ? submit => edit(current, mode, submit) : null,
            result => owner.EditChannel(Id, result, Order));
    }

    [RelayCommand]
    async Task Delete()
    {
        if (owner.Dialogs.Confirm is { } confirm && await confirm(string.Format(Strings.Confirm_DeleteChannel, Name)))
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
    [ObservableProperty] string groupNames = "";
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
    /// <summary>Package 98: a spinner on the row while the server mute waits for the server.</summary>
    [ObservableProperty] bool isBusy;

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

    internal void Update(UserInfo info, bool isSelf, Permission actor, IReadOnlyList<GroupInfo> groups)
    {
        Nickname = info.Nickname;
        Fingerprint = info.Fingerprint;
        ShowVolume(owner.VolumeOf(info.Fingerprint));
        ChannelId = info.ChannelId;
        IsSelf = isSelf;
        ServerMuted = info.ServerMuted;
        IsDeafened = info.SelfDeafened;
        // Deafened implies muted and a server mute has its own icon, so the plain mic-off icon only shows for a mute on its own.
        IsSelfMutedOnly = info.SelfMuted && !info.SelfDeafened && !info.ServerMuted;
        GroupNames = string.Join(", ", groups.Where(g => info.GroupIds.Contains(g.Id)).Select(g => g.Name)); // in the server's group order (Package 37)
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
    }

    internal void SetSpeaking(bool on, bool viaLink)
    {
        IsSpeaking = on && !viaLink;
        IsSpeakingViaLink = on && viaLink;
    }

    [RelayCommand]
    Task Move()
    {
        var targets = owner.Channels.Where(c => c.Id != ChannelId).ToList();
        return ServerViewModel.Ask<ChannelViewModel>(owner.Dialogs.PickChannel is { } pick
            ? submit => pick(string.Format(Strings.Dialog_MoveTo, Nickname), targets, submit) : null, channel => owner.Move(SessionId, channel.Id));
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
