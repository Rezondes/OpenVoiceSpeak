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
    public Func<ChannelEdit, ChannelDialogMode, Task<ChannelEdit?>>? EditChannel { get; init; }
    public Func<string, IReadOnlyList<ChannelViewModel>, Task<ChannelViewModel?>>? PickChannel { get; init; }
    public Func<string, string, Task<string?>>? AskText { get; init; }
    /// <summary>Nickname, and whether an IP is known to ban as well (Package 72: not for every offline user).</summary>
    public Func<string, bool, Task<BanChoice?>>? Ban { get; init; }
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
        switch (message)
        {
            case Error e:
                if (e.Code is Codes.WrongChannelPassword or Codes.ChannelPasswordRequired && pendingPassword is { } wrong)
                {
                    channelPasswords.Remove(wrong.Channel); // Package 94: changed meanwhile, ask again next time
                    pendingPassword = null;
                }
                var text = ErrorTexts.For(e.Code, e.Detail);
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
        foreach (var c in Mirror.Channels.Values.OrderBy(c => c.Order).ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase))
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
    public async Task<string> SendAsync(Request request, string? id = null)
    {
        id ??= $"{(request is SendChat ? ChatRequestPrefix : "r")}{++requestCounter}";
        try
        {
            await send(request with { RequestId = id });
        }
        // InvalidOperationException: a request raced a disconnect that had already shut TLS down.
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException or InvalidOperationException)
        {
            Notice?.Invoke(ErrorTexts.For(Codes.ConnectionLost));
        }
        return id;
    }

    /// <summary>Package 94: asks for the password of a password-locked channel unless remembered, the bypass right or an admin.</summary>
    public async Task JoinAsync(Guid channelId)
    {
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
        await SendAsync(new JoinChannel(channelId, password));
    }
    public Task CreateChannelAsync(string name, string description, bool isMuted = false, int maxUsers = 0,
        IReadOnlyList<Guid>? allowedGroupIds = null, string? password = null) =>
        SendAsync(new CreateChannel(name, description, isMuted, maxUsers, allowedGroupIds, password));
    public Task EditChannelAsync(Guid id, ChannelEdit edit, int order) =>
        SendAsync(new EditChannel(id, edit.Name, edit.Description, order, edit.IsMuted, edit.MaxUsers, edit.AllowedGroupIds, edit.Password));
    public Task DeleteChannelAsync(Guid id) => SendAsync(new DeleteChannel(id));

    /// <summary>Package 36: puts source right before or after target and sends the complete new order.</summary>
    public Task MoveChannelAsync(ChannelViewModel source, ChannelViewModel target, bool after)
    {
        if (source == target) return Task.CompletedTask;
        var order = Channels.Where(c => c != source).ToList();
        order.Insert(order.IndexOf(target) + (after ? 1 : 0), source);
        return order.SequenceEqual(Channels) ? Task.CompletedTask : SendAsync(new ReorderChannels(order.Select(c => c.Id).ToList()));
    }
    public Task LinkAsync(Guid a, Guid b) => SendAsync(new LinkChannels(a, b));
    public Task UnlinkAsync(Guid a, Guid b) => SendAsync(new UnlinkChannels(a, b));
    public Task MoveAsync(uint sessionId, Guid channelId) => SendAsync(new MoveUser(sessionId, channelId));
    public Task KickAsync(uint sessionId, string reason) => SendAsync(new Kick(sessionId, reason));
    public Task BanAsync(uint sessionId, BanChoice choice) => SendAsync(new Ban(sessionId, choice.Reason, choice.DurationMinutes, choice.IncludeIp));
    public Task ServerMuteAsync(uint sessionId, bool muted) => SendAsync(new SetServerMute(sessionId, muted));

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
    async Task NewChannel()
    {
        if (Dialogs.EditChannel is { } edit && await edit(new ChannelEdit("", ""), ChannelDialogMode.Create) is { } result)
            await CreateChannelAsync(result.Name, result.Description, result.IsMuted, result.MaxUsers, result.AllowedGroupIds, result.Password);
    }

    [RelayCommand]
    async Task RedeemToken()
    {
        if (Dialogs.AskText is { } ask && await ask(Strings.Dialog_RedeemToken, Strings.Dialog_RedeemTokenPrompt) is { Length: > 0 } token)
            await SendAsync(new RedeemAdminToken(token.Trim()));
    }
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
    /// <summary>Package 36: where a dragged channel would land, shown as a line above or below this one.</summary>
    [ObservableProperty] bool isDropAbove;
    [ObservableProperty] bool isDropBelow;
    /// <summary>Package 93: the lock icon behind the name and its tooltip.</summary>
    [ObservableProperty] bool isLocked;
    [ObservableProperty] string lockText = "";
    /// <summary>Package 93: "Beitreten" and the double click, only with one of the lock's groups or as admin (a password is asked for).</summary>
    [ObservableProperty] bool canJoin = true;
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
    async Task Edit()
    {
        var mode = IsDefault ? ChannelDialogMode.EditDefault : ChannelDialogMode.Edit;
        if (owner.Dialogs.EditChannel is { } edit && await edit(new ChannelEdit(Name, Description, IsMuted, MaxUsers, AllowedGroupIds, HasPassword), mode) is { } result)
            await owner.EditChannelAsync(Id, result, Order);
    }

    [RelayCommand]
    async Task Delete()
    {
        if (owner.Dialogs.Confirm is { } confirm && await confirm(string.Format(Strings.Confirm_DeleteChannel, Name)))
            await owner.DeleteChannelAsync(Id);
    }

    [RelayCommand]
    async Task Link()
    {
        if (owner.Dialogs.PickChannel is { } pick && await pick(string.Format(Strings.Dialog_LinkWith, Name), owner.LinkCandidates(this)) is { } other)
            await owner.LinkAsync(Id, other.Id);
    }

    [RelayCommand]
    async Task Unlink()
    {
        if (owner.Dialogs.PickChannel is { } pick && await pick(string.Format(Strings.Dialog_Unlink, Name), owner.LinkedChannelVms(this)) is { } other)
            await owner.UnlinkAsync(Id, other.Id);
    }
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
    async Task Move()
    {
        var targets = owner.Channels.Where(c => c.Id != ChannelId).ToList();
        if (owner.Dialogs.PickChannel is { } pick && await pick(string.Format(Strings.Dialog_MoveTo, Nickname), targets) is { } channel)
            await owner.MoveAsync(SessionId, channel.Id);
    }

    [RelayCommand]
    void Message() => owner.RequestPrivateChat(this);

    [RelayCommand]
    Task ToggleServerMute() => owner.ServerMuteAsync(SessionId, !ServerMuted);

    [RelayCommand]
    async Task Kick()
    {
        if (owner.Dialogs.AskText is { } ask && await ask(string.Format(Strings.Dialog_Kick, Nickname), Strings.Dialog_KickReason) is { } reason)
            await owner.KickAsync(SessionId, reason);
    }

    [RelayCommand]
    async Task Ban()
    {
        if (owner.Dialogs.Ban is { } ban && await ban(Nickname, true) is { } choice)
            await owner.BanAsync(SessionId, choice);
    }
}
