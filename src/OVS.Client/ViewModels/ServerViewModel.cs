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

public sealed record ChannelEdit(string Name, string Description, bool IsMuted = false, int MaxUsers = 0);

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
    public Func<string, ChannelEdit, ChannelDialogMode, Task<ChannelEdit?>>? EditChannel { get; init; }
    public Func<string, IReadOnlyList<ChannelViewModel>, Task<ChannelViewModel?>>? PickChannel { get; init; }
    public Func<string, string, Task<string?>>? AskText { get; init; }
    public Func<string, Task<BanChoice?>>? Ban { get; init; }
    public Func<string, Task<bool>>? Confirm { get; init; }
    /// <summary>Package 40: the server wants a password (none stored or the stored one is wrong).</summary>
    public Func<string, Task<PasswordAnswer?>>? AskPassword { get; init; }
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
    public Dialogs Dialogs { get; }
    public ObservableCollection<ChannelViewModel> Channels { get; } = [];

    public bool CanCreateChannel => SelfPermissions.Has(Permission.ChannelCreate);
    public bool HasSpeakLinked => SelfPermissions.Has(Permission.SpeakLinked);
    public bool CanAdminister =>
        (SelfPermissions & (Permission.GroupsManage | Permission.GroupsAssign | Permission.UserBan | Permission.ServerConfig | Permission.ChannelLink)) != 0;
    public bool CanRedeemToken => !IsAdmin;

    partial void OnIsAdminChanged(bool value) => OnPropertyChanged(nameof(CanRedeemToken));

    public UserList? LastUserList { get; private set; }
    public BanList? LastBanList { get; private set; }

    public event Action<string>? Notice;
    public event Action<Message>? AdminMessage;
    public event Action<ServerIcon>? IconReceived;

    // ---- Chat (Package 31) ----
    /// <summary>Chat requests get their own id prefix, so their errors can go to the composer instead of the feed.</summary>
    const string ChatRequestPrefix = "c";
    readonly Queue<ChatMessage> recentChat = new();
    public event Action<ChatMessage>? ChatReceived;
    /// <summary>Package 47: a moment that deserves a tone. The main view model hands it to the audio engine.</summary>
    public event Action<SoundEvent>? SoundRequested;
    /// <summary>The channel the own "Betreten" asked for: arriving there is "entered", anywhere else "moved".</summary>
    Guid? pendingJoin;
    public event Action<string>? ChatError;
    /// <summary>"Privatnachricht" in a user's context menu; the chat opens the tab.</summary>
    public event Action<UserViewModel>? PrivateChatRequested;
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
                var text = ErrorTexts.For(e.Code, e.Detail);
                if (e.RequestId?.StartsWith(ChatRequestPrefix) == true && ChatError is { } chatError) chatError(text);
                else Notice?.Invoke(text);
                return;
            case UserList list:
                LastUserList = list;
                AdminMessage?.Invoke(message);
                return;
            case BanList list:
                LastBanList = list;
                AdminMessage?.Invoke(message);
                return;
            case ServerIcon icon:
                IconReceived?.Invoke(icon);
                return;
            case ChatMessage chat:
                if (chat.Target == ChatTarget.Private && chat.FromSessionId != Mirror.SelfId) SoundRequested?.Invoke(SoundEvent.PrivateMessage);
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
                    bool own = pendingJoin == u.User.ChannelId;
                    pendingJoin = null;
                    return own ? SoundEvent.ChannelEntered : SoundEvent.Moved;
                }
                return u.User.ServerMuted && !self.ServerMuted ? SoundEvent.ServerMuted : null;
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

    public void OnSpeakers(IEnumerable<ActiveSpeaker> active)
    {
        var now = time.GetUtcNow();
        foreach (var a in active) speaking[a.SessionId] = (now, a.ViaLink);
        RefreshSpeaking();
    }

    public void SetSelfTransmitting(byte? target)
    {
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

    public async Task SendAsync(Request request)
    {
        try
        {
            await send(request with { RequestId = $"{(request is SendChat ? ChatRequestPrefix : "r")}{++requestCounter}" });
        }
        // InvalidOperationException: a request raced a disconnect that had already shut TLS down.
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException or InvalidOperationException)
        {
            Notice?.Invoke(ErrorTexts.For(Codes.ConnectionLost));
        }
    }

    public Task JoinAsync(Guid channelId)
    {
        pendingJoin = channelId;
        return SendAsync(new JoinChannel(channelId));
    }
    public Task CreateChannelAsync(string name, string description) => SendAsync(new CreateChannel(name, description));
    public Task EditChannelAsync(Guid id, ChannelEdit edit, int order) => SendAsync(new EditChannel(id, edit.Name, edit.Description, order, edit.IsMuted, edit.MaxUsers));
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
        if (Dialogs.EditChannel is { } edit && await edit(Strings.Dialog_CreateChannel, new ChannelEdit("", ""), ChannelDialogMode.Create) is { } result)
            await CreateChannelAsync(result.Name, result.Description);
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
    Task Join() => owner.JoinAsync(Id);

    [RelayCommand]
    Task Create() => owner.NewChannelCommand.ExecuteAsync(null);

    [RelayCommand]
    async Task Edit()
    {
        var mode = IsDefault ? ChannelDialogMode.EditDefault : ChannelDialogMode.Edit;
        if (owner.Dialogs.EditChannel is { } edit && await edit(Strings.Dialog_EditChannel, new ChannelEdit(Name, Description, IsMuted, MaxUsers), mode) is { } result)
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
    [ObservableProperty] string nickname = "";
    [ObservableProperty] string fingerprint = "";
    [ObservableProperty] string statusText = "";
    [ObservableProperty] string groupNames = "";
    [ObservableProperty] bool isSelf;
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

    public uint SessionId { get; } = sessionId;
    public Permission Permissions { get; private set; }
    public Guid ChannelId { get; private set; }

    internal void Update(UserInfo info, bool isSelf, Permission actor, IReadOnlyList<GroupInfo> groups)
    {
        Nickname = info.Nickname;
        Fingerprint = info.Fingerprint;
        Permissions = info.Permissions;
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
        bool rank = actor.CanActOn(info.Permissions);
        CanMove = actor.Has(Permission.UserMove) && rank;
        CanMute = actor.Has(Permission.UserMute) && rank && !isSelf;
        CanKick = actor.Has(Permission.UserKick) && rank && !isSelf;
        CanBan = actor.Has(Permission.UserBan) && rank && !isSelf;
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
        if (owner.Dialogs.Ban is { } ban && await ban(Nickname) is { } choice)
            await owner.BanAsync(SessionId, choice);
    }
}
