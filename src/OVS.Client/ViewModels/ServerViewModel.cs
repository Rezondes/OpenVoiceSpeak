using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OVS.Client.Audio;
using OVS.Client.Net;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;

namespace OVS.Client.ViewModels;

public sealed record ChannelEdit(string Name, string Description);
public sealed record BanChoice(string Reason, int? DurationMinutes, bool IncludeIp);

/// <summary>Dialogs the view provides. Unset entries mean "cancelled" (used by tests).</summary>
public sealed class Dialogs
{
    public Func<string, string, string, Task<ChannelEdit?>>? EditChannel { get; init; }
    public Func<string, IReadOnlyList<ChannelViewModel>, Task<ChannelViewModel?>>? PickChannel { get; init; }
    public Func<string, string, Task<string?>>? AskText { get; init; }
    public Func<string, Task<BanChoice?>>? Ban { get; init; }
    public Func<string, Task<bool>>? Confirm { get; init; }
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
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreateChannel), nameof(CanAdminister), nameof(HasSpeakLinked))]
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
        (SelfPermissions & (Permission.GroupsManage | Permission.GroupsAssign | Permission.UserBan | Permission.ServerConfig)) != 0;
    public bool CanRedeemToken => !IsAdmin;

    partial void OnIsAdminChanged(bool value) => OnPropertyChanged(nameof(CanRedeemToken));

    public UserList? LastUserList { get; private set; }
    public BanList? LastBanList { get; private set; }

    public event Action<string>? Notice;
    public event Action<Message>? AdminMessage;
    public event Action? StateChanged;

    public void Apply(Message message)
    {
        switch (message)
        {
            case Error e:
                Notice?.Invoke(ErrorTexts.For(e.Code, e.Detail));
                return;
            case UserList list:
                LastUserList = list;
                AdminMessage?.Invoke(message);
                return;
            case BanList list:
                LastBanList = list;
                AdminMessage?.Invoke(message);
                return;
        }
        if (Mirror.Apply(message)) Rebuild();
    }

    // ---- Tree ----

    public void Rebuild()
    {
        var self = Mirror.Self;
        ServerName = Mirror.Settings.Name;
        WelcomeText = Mirror.Settings.WelcomeText;
        SelfPermissions = self?.Permissions ?? Permission.None;
        IsAdmin = self?.GroupIds.Contains(WellKnownGroups.Admin) ?? false;
        var groupNames = Mirror.Groups.ToDictionary(g => g.Id, g => g.Name);

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
                    user.Update(u, u.SessionId == Mirror.SelfId, SelfPermissions, groupNames);
                    return user;
                })
                .ToList();
            Sync(channel.Users, users);
            desired.Add(channel);
        }
        Sync(Channels, desired);
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
            await send(request with { RequestId = $"r{++requestCounter}" });
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException)
        {
            Notice?.Invoke(ErrorTexts.For(Codes.ConnectionLost));
        }
    }

    public Task JoinAsync(Guid channelId) => SendAsync(new JoinChannel(channelId));
    public Task CreateChannelAsync(string name, string description) => SendAsync(new CreateChannel(name, description));
    public Task EditChannelAsync(Guid id, string name, string description, int order) => SendAsync(new EditChannel(id, name, description, order));
    public Task DeleteChannelAsync(Guid id) => SendAsync(new DeleteChannel(id));
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
        }
        else
        {
            SelfMuted = !SelfMuted;
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
        return SendAsync(new SetSelfState(SelfMuted, SelfDeafened));
    }

    [RelayCommand]
    async Task NewChannel()
    {
        if (Dialogs.EditChannel is { } edit && await edit("Channel anlegen", "", "") is { } result)
            await CreateChannelAsync(result.Name, result.Description);
    }

    [RelayCommand]
    async Task RedeemToken()
    {
        if (Dialogs.AskText is { } ask && await ask("Admin-Token einlösen", "Token aus dem Serverlog:") is { Length: > 0 } token)
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
    [ObservableProperty] bool canEdit;
    [ObservableProperty] bool canDelete;
    [ObservableProperty] bool canLink;
    [ObservableProperty] bool canUnlink;

    public Guid Id { get; } = id;
    public ObservableCollection<UserViewModel> Users { get; } = [];
    public string Tooltip => IsLinked ? $"{Description}\nVerlinkt mit: {LinkedNames}".Trim() : Description;

    internal void Update(ChannelInfo info, IReadOnlyList<string> linked, bool isDefault, bool isCurrent, Permission actor)
    {
        Name = info.Name;
        Description = info.Description;
        Order = info.Order;
        IsLinked = linked.Count > 0;
        LinkedNames = string.Join(", ", linked);
        IsDefault = isDefault;
        IsCurrent = isCurrent;
        CanEdit = actor.Has(Permission.ChannelEdit);
        CanDelete = actor.Has(Permission.ChannelDelete) && !isDefault;
        CanLink = actor.Has(Permission.ChannelLink);
        CanUnlink = CanLink && IsLinked;
        OnPropertyChanged(nameof(Tooltip));
    }

    [RelayCommand]
    Task Join() => owner.JoinAsync(Id);

    [RelayCommand]
    async Task Edit()
    {
        if (owner.Dialogs.EditChannel is { } edit && await edit("Channel bearbeiten", Name, Description) is { } result)
            await owner.EditChannelAsync(Id, result.Name, result.Description, Order);
    }

    [RelayCommand]
    async Task Delete()
    {
        if (owner.Dialogs.Confirm is { } confirm && await confirm($"Channel \"{Name}\" löschen? Nutzer darin landen im Standard-Channel."))
            await owner.DeleteChannelAsync(Id);
    }

    [RelayCommand]
    async Task Link()
    {
        if (owner.Dialogs.PickChannel is { } pick && await pick($"\"{Name}\" verlinken mit", owner.LinkCandidates(this)) is { } other)
            await owner.LinkAsync(Id, other.Id);
    }

    [RelayCommand]
    async Task Unlink()
    {
        if (owner.Dialogs.PickChannel is { } pick && await pick($"Link von \"{Name}\" entfernen", owner.LinkedChannelVms(this)) is { } other)
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
    [ObservableProperty] bool isSpeaking;
    [ObservableProperty] bool isSpeakingViaLink;
    [ObservableProperty] bool canMove;
    [ObservableProperty] bool canMute;
    [ObservableProperty] bool canKick;
    [ObservableProperty] bool canBan;

    public uint SessionId { get; } = sessionId;
    public Permission Permissions { get; private set; }
    public Guid ChannelId { get; private set; }

    internal void Update(UserInfo info, bool isSelf, Permission actor, IReadOnlyDictionary<Guid, string> groups)
    {
        Nickname = info.Nickname;
        Fingerprint = info.Fingerprint;
        Permissions = info.Permissions;
        ChannelId = info.ChannelId;
        IsSelf = isSelf;
        ServerMuted = info.ServerMuted;
        GroupNames = string.Join(", ", info.GroupIds.Select(g => groups.GetValueOrDefault(g)).OfType<string>());
        StatusText = info switch
        {
            { ServerMuted: true } => "(vom Server stumm)",
            { SelfDeafened: true } => "(taub)",
            { SelfMuted: true } => "(stumm)",
            _ => "",
        };
        bool rank = actor.CanActOn(info.Permissions);
        CanMove = actor.Has(Permission.UserMove) && rank;
        CanMute = actor.Has(Permission.UserMute) && rank && !isSelf;
        CanKick = actor.Has(Permission.UserKick) && rank && !isSelf;
        CanBan = actor.Has(Permission.UserBan) && rank && !isSelf;
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
        if (owner.Dialogs.PickChannel is { } pick && await pick($"{Nickname} verschieben nach", targets) is { } channel)
            await owner.MoveAsync(SessionId, channel.Id);
    }

    [RelayCommand]
    Task ToggleServerMute() => owner.ServerMuteAsync(SessionId, !ServerMuted);

    [RelayCommand]
    async Task Kick()
    {
        if (owner.Dialogs.AskText is { } ask && await ask($"{Nickname} kicken", "Grund:") is { } reason)
            await owner.KickAsync(SessionId, reason);
    }

    [RelayCommand]
    async Task Ban()
    {
        if (owner.Dialogs.Ban is { } ban && await ban(Nickname) is { } choice)
            await owner.BanAsync(SessionId, choice);
    }
}
