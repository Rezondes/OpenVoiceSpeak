using OVS.Client.Dialogs;
using OVS.Client.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OVS.Client.Models;
using OVS.Client.Settings;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol.Messages.Groups;
using OVS.Shared.Protocol.Messages.Users;

namespace OVS.Client.ViewModels;

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
    IReadOnlyList<GroupAssignmentViewModel> groups = [];
    public bool HasGroups => Groups.Count > 0;

    /// <summary>
    /// Package 119: group changes sent from the menu, ticked as asked until the server shows or refuses them. Per group only
    /// the last click counts, an earlier one's answer leaves it be. Touched on the UI thread only.
    /// </summary>
    readonly Dictionary<Guid, (bool On, PendingActionViewModel Sent)> pendingGroups = [];

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
        owner.Tree.UserUpdates++;
        Nickname = info.Nickname;
        Fingerprint = info.Fingerprint;
        ShowVolume(owner.VolumeOf(info.Fingerprint));
        ChannelId = info.ChannelId;
        IsSelf = isSelf;
        ServerMuted = info.ServerMuted;
        IsDeafened = info.SelfDeafened;
        // Deafened implies muted and a server mute has its own icon, so the plain mic-off icon only shows for a mute on its own.
        IsSelfMutedOnly = info.SelfMuted && !info.SelfDeafened && !info.ServerMuted;
        GroupNames = string.Join(", ", serverGroups.Where(groupInfo => info.GroupIds.Contains(groupInfo.Id)).Select(groupInfo => groupInfo.Name)); // in the server's group order (Package 37)
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
            ? serverGroups.Select(groupInfo => (groupInfo.Id, groupInfo.Name, Checked: pendingGroups.TryGetValue(groupInfo.Id, out var pending) ? pending.On : info.GroupIds.Contains(groupInfo.Id), groupInfo.AssignableByMe)).ToList()
            : [];
        // compared with the live ticks: a refused click is put back, and an open menu is not rebuilt by unrelated updates
        if (!wanted.SequenceEqual(Groups.Select(groupAssignment => (groupAssignment.GroupId, groupAssignment.Name, groupAssignment.IsChecked, groupAssignment.IsEnabled))))
            Groups = wanted.Select(wantedGroup => new GroupAssignmentViewModel(wantedGroup.Id, wantedGroup.Name, wantedGroup.Checked, wantedGroup.AssignableByMe, ToggleGroupAsync)).ToList();
    }

    /// <summary>Package 119: IsChecked already holds the new value; the row spins until the server shows it.</summary>
    Task ToggleGroupAsync(GroupAssignmentViewModel toggle)
    {
        var sent = owner.SetUserGroup(Fingerprint, SessionId, toggle.GroupId, toggle.IsChecked);
        pendingGroups[toggle.GroupId] = (toggle.IsChecked, sent);
        _ = EndGroupChangeAsync(toggle.GroupId, sent);
        return Task.CompletedTask;
    }

    async Task EndGroupChangeAsync(Guid groupId, PendingActionViewModel sent)
    {
        await sent.Completion;
        if (pendingGroups.GetValueOrDefault(groupId).Sent == sent) pendingGroups.Remove(groupId);
        if (pendingGroups.Values.Any(pending => pending.Sent.IsBusy)) IsBusy = true; // this one ending cleared the spinner a later click still needs
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
        var wanted = CanMove ? channels.Where(channel => channel.Id != ChannelId && !channel.IsSeparator).ToList() : [];
        // only on a change, so an open menu is not rebuilt by unrelated updates
        if (!wanted.SequenceEqual(MoveTargets.Select(moveTarget => moveTarget.Channel)))
            MoveTargets = wanted.Select(channel => new MoveTarget(channel, new RelayCommand(() => owner.Move(SessionId, channel.Id)))).ToList();
    }

    [RelayCommand]
    void Message() => owner.RequestPrivateChat(this);

    [RelayCommand]
    Task ToggleServerMuteAsync()
    {
        owner.ServerMute(SessionId, !ServerMuted);
        return Task.CompletedTask;
    }

    [RelayCommand]
    Task KickAsync() =>
        ServerViewModel.AskAsync<string>(owner.Dialogs.AskText is { } ask
            ? submit => ask(string.Format(Strings.Dialog_Kick, Nickname), Strings.Dialog_KickReason, submit) : null, reason => owner.Kick(SessionId, reason));

    [RelayCommand]
    Task BanAsync() =>
        ServerViewModel.AskAsync<BanChoice>(owner.Dialogs.Ban is { } ban ? submit => ban(Nickname, true, submit) : null, choice => owner.Ban(SessionId, choice));
}
