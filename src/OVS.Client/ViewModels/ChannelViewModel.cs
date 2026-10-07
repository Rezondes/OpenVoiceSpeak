using OVS.Client.Collections;
using OVS.Client.Dialogs;
using OVS.Client.Localization;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Groups;

namespace OVS.Client.ViewModels;

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
    [ObservableProperty] int bitrate = ProtocolDefaults.Bitrate;
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
    internal void UpdateLock(ChannelInfo info, IReadOnlyList<GroupInfo> groups, IReadOnlyList<Guid> ownGroups)
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
        CanJoin = ChannelAccess.IsJoinable(info.Kind) && ChannelAccess.GroupsAllow(info.AllowedGroupIds, ownGroups);
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
        owner.ShowNotice(ErrorTexts.For(ReasonCodes.ChannelLocked)); // Package 93: the server would refuse it anyway
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
