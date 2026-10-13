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
    readonly Func<Request, Task> send;
    readonly TimeProvider time;
    int requestCounter;

    [ObservableProperty] string serverName = "";
    [ObservableProperty] string welcomeText = "";
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
        ChatFeed = new ChatFeed(RequestSound);
        Leave = new LeaveTimer(time, () => Post);
        Tree = new ChannelTreeViewModel(this, RequestSound);
        SelfState = new SelfStateViewModel(request => SendAsync(request), RequestSound);
        Rebuild();
    }

    public StateMirror Mirror { get; }
    public TimeProvider Time => time;
    public ServerDialogs Dialogs { get; }
    /// <summary>The sidebar's channel tree: the channel and user rows, their order and their speaking marks.</summary>
    public ChannelTreeViewModel Tree { get; }

    /// <summary>The own mute and deafen, bound with the two buttons.</summary>
    public SelfStateViewModel SelfState { get; }

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

    /// <summary>Package 31: the chat as it comes, its tones, the kept messages and the composer's refusals.</summary>
    internal ChatFeed ChatFeed { get; }
    /// <summary>Package 47: a moment that deserves a tone. The main view model hands it to the audio engine.</summary>
    public event Action<SoundEvent>? SoundRequested;
    internal void RequestSound(SoundEvent sound) => SoundRequested?.Invoke(sound);
    /// <summary>Package 73: the admin page changes the own groups; the tone for the one acting follows from there.</summary>
    internal bool OwnGroupChangePending { get; set; }
    /// <summary>Packages 94 and 139: the own join in flight and the channel passwords that worked on this connection.</summary>
    readonly ChannelJoin join = new();

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
            foreach (var user in Tree.Users) user.ShowVolume(value(user.Fingerprint));
        }
    }

    /// <summary>Package 51: someone moved a volume slider (fingerprint, new volume).</summary>
    public event Action<string, float>? VolumeChanged;
    internal void OnVolumeChanged(UserViewModel user) => VolumeChanged?.Invoke(user.Fingerprint, (float)(user.VolumePercent / 100));
    internal void RequestPrivateChat(UserViewModel user) => PrivateChatRequested?.Invoke(user);

    public bool CanChatServer => SelfPermissions.Has(Permission.ChatServer);
    public bool CanChatChannel => SelfPermissions.Has(Permission.ChatChannel);
    public bool CanChatPrivate => SelfPermissions.Has(Permission.ChatPrivate);
    public Task SendChatAsync(ChatTarget target, string text, uint? to = null) => SendAsync(new SendChat(target, to, text));
    public string? IconHash => Mirror.Settings.IconHash;
    public event Action? StateChanged;
    /// <summary>Package 122: what the last <see cref="StateChanged"/> was about.</summary>
    public StateChange LastChange { get; private set; } = StateChange.All;


    public void Apply(Message message)
    {
        ApplyMessage(message);
        // Package 98: actions without an answer of their own end with the message that shows them done
        foreach (var waiting in confirming.ToList())
        {
            if (!waiting.Pending.IsRunning) confirming.Remove(waiting);
            else if (waiting.Confirms(message))
            {
                confirming.Remove(waiting);
                waiting.Pending.Done();
            }
        }
    }

    void ApplyMessage(Message message)
    {
        switch (message)
        {
            case Error error:
                join.Refused(error.Code); // Package 94: a wrong channel password is asked for again next time
                var text = ErrorTexts.For(error.Code, error.Detail);
                if (error.RequestId is { } failed && waiting.Remove(failed, out var pending) && pending.Answers(failed)) pending.Fail(text); // Package 97
                if (!ChatFeed.TryRefuse(error.RequestId, text)) Notice?.Invoke(text);
                Routes.Deliver(error.RequestId, message); // Package 73: the sender drops what the server refused
                return;
            case UserList list:
                LastUserList = list;
                break;
            case BanList list:
                LastBanList = list;
                break;
            case ServerIcon icon:
                IconReceived?.Invoke(icon);
                return;
            case ChatMessage chat:
                ChatFeed.Receive(chat, Mirror.SelfId);
                return;
        }
        if (Routes.DeliverAnswer(message)) return; // Packages 73 to 75, 81 and 82: the answers go to their request's owner
        var sound = SoundFor(message); // compared with the state before the change
        var before = message switch
        {
            UserUpdated userUpdated => Mirror.Users.GetValueOrDefault(userUpdated.User.SessionId),
            UserJoined userJoined => Mirror.Users.GetValueOrDefault(userJoined.User.SessionId), // a session already there is moved, not shown twice
            UserLeft userLeft => Mirror.Users.GetValueOrDefault(userLeft.SessionId),
            _ => null,
        };
        if (Mirror.Apply(message)) Show(message, before);
        if (sound is { } soundEvent) SoundRequested?.Invoke(soundEvent);
    }

    /// <summary>
    /// Package 122: a message about another user (or about me that leaves my channel, rights and groups as they were)
    /// only updates that user and the lists they leave or come into; everything else rebuilds the tree.
    /// </summary>
    void Show(Message message, UserInfo? before)
    {
        switch (message)
        {
            case UserJoined userJoined when userJoined.User.SessionId != Mirror.SelfId:
                ShowUser(userJoined.User, before);
                return;
            case UserLeft when before is not null && before.SessionId != Mirror.SelfId:
                ShowUser(null, before);
                return;
            case UserUpdated userUpdated when before is not null && (userUpdated.User.SessionId != Mirror.SelfId
                || (userUpdated.User.ChannelId == before.ChannelId && userUpdated.User.Permissions == before.Permissions && userUpdated.User.GroupIds.SequenceEqual(before.GroupIds))):
                ShowUser(userUpdated.User, before);
                return;
        }
        Rebuild(message is UserJoined or UserLeft ? StateChange.All : StateChange.Names | StateChange.Sidebar | StateChange.Tree);
    }

    void ShowUser(UserInfo? now, UserInfo? before)
    {
        LastChange = Tree.ShowUser(now, before);
        StateChanged?.Invoke();
    }

    SoundEvent? SoundFor(Message message)
    {
        var self = Mirror.Self;
        var mine = self?.ChannelId;
        switch (message)
        {
            case UserJoined userJoined when userJoined.User.SessionId != Mirror.SelfId && userJoined.User.ChannelId == mine:
                return SoundEvent.UserJoined;
            case UserLeft userLeft when userLeft.SessionId != Mirror.SelfId && Mirror.Users.TryGetValue(userLeft.SessionId, out var gone) && gone.ChannelId == mine:
                return SoundEvent.UserLeft;
            case UserUpdated userUpdated when userUpdated.User.SessionId == Mirror.SelfId && self is not null:
                if (userUpdated.User.ChannelId != self.ChannelId)
                {
                    return join.Arrived(userUpdated.User.ChannelId) ? SoundEvent.ChannelEntered : SoundEvent.Moved;
                }
                if (userUpdated.User.ServerMuted && !self.ServerMuted) return SoundEvent.ServerMuted;
                // Package 73: the own groups changed; a change made by myself only gives the tone for the one acting
                return !OwnGroupChangePending && !userUpdated.User.GroupIds.ToHashSet().SetEquals(self.GroupIds) ? SoundEvent.GroupChanged : null;
            case UserUpdated userUpdated when Mirror.Users.TryGetValue(userUpdated.User.SessionId, out var before):
                if (before.ChannelId != mine && userUpdated.User.ChannelId == mine) return SoundEvent.UserJoined;
                if (before.ChannelId == mine && userUpdated.User.ChannelId != mine) return SoundEvent.UserLeft;
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
        Tree.Rebuild(self);
        CurrentChannel = self is null ? null : Tree.FindChannel(self.ChannelId);
        Self = Tree.FindUser(Mirror.SelfId);
        Tree.RefreshSpeaking();
        LastChange = change;
        StateChanged?.Invoke();
    }

    // ---- Requests ----

    /// <summary>Sends a request and returns the id it got, so an answer or error can be matched (Package 73).</summary>
    /// <param name="id">A fixed id instead of a new one, e.g. the round a later list page belongs to.</param>
    /// <param name="pending">Package 97: marked running under the request's id before sending; the server's error for it ends it.</param>
    /// <param name="route">Gets the answers to the request and the server's error for it, until its owner removes the route.</param>
    public async Task<string> SendAsync(Request request, string? id = null, PendingActionViewModel? pending = null, Action<Message>? route = null)
    {
        id ??= $"{(request is SendChat ? ChatFeed.RequestPrefix : "r")}{++requestCounter}";
        if (route is not null) Routes.Add(id, route); // before sending: the answer can come at once
        if (pending is not null)
        {
            foreach (var over in waiting.Where(pair => !pair.Value.Answers(pair.Key)).Select(pair => pair.Key).ToList()) waiting.Remove(over);
            pending.Start(id); // before sending: the answer can come at once
            waiting[id] = pending;
        }
        try
        {
            await send(request with { RequestId = id });
        }
        // InvalidOperationException: a request raced a disconnect that had already shut TLS down.
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException or InvalidOperationException)
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
    readonly Dictionary<string, PendingActionViewModel> waiting = [];

    /// <summary>
    /// Runs work on the UI thread; the main view model sets its dispatcher. Made on Avalonia's UI thread (headless tests)
    /// it posts there, otherwise (view model tests with a manual clock) it runs inline.
    /// </summary>
    public Action<Action> Post { get; set; } = SynchronizationContext.Current is Avalonia.Threading.AvaloniaSynchronizationContext ui
        ? action => ui.Post(_ => action(), null)
        : action => action();

    /// <summary>A waiting mark on this server's clock, its timers handed to the UI thread.</summary>
    public PendingActionViewModel NewPending() => new(time, action => Post(action));

    /// <summary>Package 98: marks that end with a message showing the action done (most requests have no answer of their own).</summary>
    readonly List<(PendingActionViewModel Pending, Func<Message, bool> Confirms)> confirming = [];

    /// <summary>
    /// Package 98: sends under a waiting mark that ends once a message confirms it, or with the server's error for it or
    /// the timeout. Unless a dialog shows it (notify false), the timeout's error shows as a notice like the server's errors.
    /// </summary>
    /// <param name="busy">Gets IsBusy of the mark, e.g. for the spinner on a row.</param>
    public PendingActionViewModel SendConfirmed(Request request, Func<Message, bool> confirms, PendingActionViewModel? pending = null, Action<bool>? busy = null, bool notify = true)
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
    internal static async Task Ask<T>(Func<Func<T, PendingActionViewModel>, Task<T?>>? ask, Func<T, PendingActionViewModel> send) where T : class
    {
        if (ask is null) return;
        PendingActionViewModel? sent = null;
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
            && !join.TryGetPassword(channelId, out password))
        {
            if (Dialogs.AskChannelPassword is not { } ask || await ask(channel.Name) is not { Length: > 0 } entered) return;
            password = entered;
        }
        join.Begin(channelId, password);
        if (Mirror.Self?.ChannelId is not { } before || before == channelId)
        {
            await SendAsync(new JoinChannel(channelId, password));
            return;
        }
        // Package 98: the row spins until the own channel changes; a second join meanwhile is ignored
        var row = Tree.FindChannel(channelId);
        joining = SendConfirmed(new JoinChannel(channelId, password), message => message is UserUpdated userUpdated && IsSelf(userUpdated.User) && userUpdated.User.ChannelId != before,
            busy: on => row?.IsBusy = on);
    }

    PendingActionViewModel? joining;
    public bool IsJoining => joining?.IsRunning == true;

    /// <summary>Package 139 (A148): the password that let me into a channel on this connection, kept in memory only.</summary>
    internal string? PasswordFor(Guid channelId) => join.PasswordFor(channelId);

    /// <summary>Package 139: back into the channel from before a reconnect, with its password from then and never a dialog.</summary>
    internal Task RejoinAsync(Guid channelId, string? password)
    {
        join.Begin(channelId, password);
        return SendAsync(new JoinChannel(channelId, password));
    }

    /// <summary>Package 110: names may repeat, so the own channel is the one the server answers this request with.</summary>
    public PendingActionViewModel CreateChannel(ChannelEdit channelEdit)
    {
        var pending = NewPending();
        return SendConfirmed(new CreateChannel(channelEdit.Name, channelEdit.Description, channelEdit.IsMuted, channelEdit.MaxUsers, channelEdit.AllowedGroupIds, channelEdit.Password, channelEdit.Kind, channelEdit.Bitrate),
            message => message is ChannelAdded { RequestId: { } id } && pending.Answers(id), pending, notify: false);
    }
    public PendingActionViewModel EditChannel(Guid id, ChannelEdit edit, int order) =>
        SendConfirmed(new EditChannel(id, edit.Name, edit.Description, order, edit.IsMuted, edit.MaxUsers, edit.AllowedGroupIds, edit.Password, edit.Bitrate),
            message => message is ChannelUpdated channelUpdated && channelUpdated.Channel.Id == id, notify: false);
    public PendingActionViewModel DeleteChannel(Guid id)
    {
        var row = Tree.FindChannel(id); // gone from the tree when confirmed
        return SendConfirmed(new DeleteChannel(id), message => message is ChannelRemoved channelRemoved && channelRemoved.ChannelId == id, busy: on => row?.IsBusy = on);
    }


    public PendingActionViewModel Link(Guid id, Guid otherId) =>
        SendConfirmed(new LinkChannels(id, otherId), message => message is ChannelsLinked linked && ChannelPair.Of(linked.A, linked.B) == ChannelPair.Of(id, otherId), notify: false);
    public PendingActionViewModel Unlink(Guid id, Guid otherId) =>
        SendConfirmed(new UnlinkChannels(id, otherId), message => message is ChannelsUnlinked unlinked && ChannelPair.Of(unlinked.A, unlinked.B) == ChannelPair.Of(id, otherId), notify: false);
    /// <summary>Package 120: without the dialog that waited, the moved user's row spins until the server shows the move.</summary>
    public PendingActionViewModel Move(uint sessionId, Guid channelId)
    {
        var row = Tree.FindUser(sessionId);
        return SendConfirmed(new MoveUser(sessionId, channelId), message => message is UserUpdated userUpdated && userUpdated.User.SessionId == sessionId && userUpdated.User.ChannelId == channelId,
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
        if (!IsDropTarget(user, channel) || !Mirror.Users.ContainsKey(user.SessionId) || Tree.FindUser(user.SessionId) != user) return;
        if (!user.IsSelf)
        {
            await Move(user.SessionId, channel.Id).Completion;
            return;
        }
        await channel.JoinCommand.ExecuteAsync(null);
        if (joining is { } join) await join.Completion;
    }
    public PendingActionViewModel Kick(uint sessionId, string reason) => SendConfirmed(new KickSession(sessionId, reason), message => message is UserLeft userLeft && userLeft.SessionId == sessionId, notify: false);
    public PendingActionViewModel Ban(uint sessionId, BanChoice choice) =>
        SendConfirmed(new BanSession(sessionId, choice.Reason, choice.DurationMinutes, choice.IncludeIp), message => message is UserLeft userLeft && userLeft.SessionId == sessionId, notify: false);
    public PendingActionViewModel ServerMute(uint sessionId, bool muted)
    {
        var row = Tree.FindUser(sessionId);
        return SendConfirmed(new SetSessionServerMute(sessionId, muted), message => message is UserUpdated userUpdated && userUpdated.User.SessionId == sessionId && userUpdated.User.ServerMuted == muted,
            busy: on => row?.IsBusy = on);
    }

    /// <summary>Package 119: confirmed by the user's update showing the group; the server answers nothing else.</summary>
    public PendingActionViewModel SetUserGroup(string fingerprint, uint sessionId, Guid groupId, bool assigned)
    {
        var row = Tree.FindUser(sessionId);
        return SendConfirmed(assigned ? new AssignGroup(fingerprint, groupId) : new UnassignGroup(fingerprint, groupId),
            message => message is UserUpdated userUpdated && userUpdated.User.SessionId == sessionId && userUpdated.User.GroupIds.Contains(groupId) == assigned, busy: on => row?.IsBusy = on);
    }

    [RelayCommand]
    Task NewChannel() =>
        Ask<ChannelEdit>(Dialogs.EditChannel is { } edit ? submit => edit(new ChannelEdit("", ""), ChannelDialogMode.Create, submit) : null, CreateChannel);

    /// <summary>Package 98: the dialog waits until the own user comes back with the new groups (or the token is refused).</summary>
    [RelayCommand]
    Task RedeemToken() =>
        Ask<string>(Dialogs.AskText is { } ask ? submit => ask(Strings.Dialog_RedeemToken, Strings.Dialog_RedeemTokenPrompt, submit) : null,
            token => SendConfirmed(new RedeemAdminToken(token.Trim()), message => message is UserUpdated userUpdated && IsSelf(userUpdated.User), notify: false));
}
