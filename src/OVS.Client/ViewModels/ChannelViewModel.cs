using System.Globalization;
using OVS.Client.Collections;
using OVS.Client.Dialogs;
using OVS.Client.Localization;
using OVS.Client.Models;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Groups;

namespace OVS.Client.ViewModels;

/// <summary>A channel in the tree, with its users.</summary>
internal sealed partial class ChannelViewModel(ServerViewModel owner, Guid id) : ObservableObject
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
    public int MaxUsers => Slots.MaxUsers;
    /// <summary>Package 132 (A141): kbps its senders encode with; 0 for a separator.</summary>
    [ObservableProperty] int bitrate = ProtocolDefaults.Bitrate;
    /// <summary>The users in this channel (Package 102: not those still folding away) and its limit, for the slot counter.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SlotText))]
    [NotifyPropertyChangedFor(nameof(MaxUsers))]
    ChannelSlots slots;
    /// <summary>"3/5" for a limited channel, otherwise just the count.</summary>
    public string SlotText => Slots.ToString();
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
    /// <summary>A voice channel, not a separator.</summary>
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

    /// <summary>The channel's id.</summary>
    public Guid Id { get; } = id;
    /// <summary>The users in the channel, in the tree's order.</summary>
    public ObservableCollection<UserViewModel> Users { get; } = [];
    /// <summary>Package 112: null without description and links, so no empty tooltip opens.</summary>
    public string? Tooltip
    {
        get
        {
            if (IsSeparator) return null;
            var text = IsLinked ? $"{Description}{(char)10}{string.Format(CultureInfo.CurrentCulture, Strings.Tooltip_LinkedWith, LinkedNames)}".Trim() : Description.Trim();
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
            var ids => string.Format(CultureInfo.CurrentCulture, Strings.Ui_LockedGroupsFmt,
                string.Join(", ", groups.Where(groupInfo => ids.Contains(groupInfo.Id)).Select(groupInfo => groupInfo.Name))), // in the server's group order
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
    Task MoveUpAsync() => first ? Task.CompletedTask : owner.Tree.MoveChannelAsync(this, Neighbour(-1), after: false);

    [RelayCommand]
    Task MoveDownAsync() => last ? Task.CompletedTask : owner.Tree.MoveChannelAsync(this, Neighbour(+1), after: true);

    ChannelViewModel Neighbour(int step)
    {
        var live = owner.Tree.Channels.Live().ToList();
        return live[live.IndexOf(this) + step];
    }

    [RelayCommand]
    Task JoinAsync()
    {
        if (IsSeparator) return Task.CompletedTask; // Package 111: a double click on the line does nothing
        if (CanJoin) return owner.JoinAsync(Id);
        owner.ShowNotice(ErrorTexts.For(ReasonCodes.ChannelLocked)); // Package 93: the server would refuse it anyway
        return Task.CompletedTask;
    }

    [RelayCommand]
    Task CreateAsync() => owner.NewChannelCommand.ExecuteAsync(null);

    [RelayCommand]
    Task EditAsync()
    {
        var mode = IsDefault ? ChannelDialogMode.EditDefault : ChannelDialogMode.Edit;
        var current = new ChannelEdit(Name, Description, IsMuted, MaxUsers, AllowedGroupIds, HasPassword, Bitrate: Bitrate);
        return ServerViewModel.AskAsync<ChannelEdit>(owner.Dialogs.EditChannel is { } edit ? submit => edit(current, mode, submit) : null,
            result => owner.EditChannel(Id, result, Order));
    }

    [RelayCommand]
    async Task DeleteAsync()
    {
        var question = IsSeparator ? Strings.Confirm_DeleteSeparator : string.Format(CultureInfo.CurrentCulture, Strings.Confirm_DeleteChannel, Name);
        if (owner.Dialogs.Confirm is { } confirm && await confirm(question))
            owner.DeleteChannel(Id);
    }

    [RelayCommand]
    Task LinkAsync() =>
        ServerViewModel.AskAsync<ChannelViewModel>(owner.Dialogs.PickChannel is { } pick
            ? submit => pick(string.Format(CultureInfo.CurrentCulture, Strings.Dialog_LinkWith, Name), owner.Tree.LinkCandidates(this), submit) : null, other => owner.Link(Id, other.Id));

    [RelayCommand]
    Task UnlinkAsync() =>
        ServerViewModel.AskAsync<ChannelViewModel>(owner.Dialogs.PickChannel is { } pick
            ? submit => pick(string.Format(CultureInfo.CurrentCulture, Strings.Dialog_Unlink, Name), owner.Tree.LinkedChannelVms(this), submit) : null, other => owner.Unlink(Id, other.Id));
}
