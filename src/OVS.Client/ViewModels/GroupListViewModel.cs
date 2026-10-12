using OVS.Client.Collections;
using OVS.Client.Localization;
using OVS.Client.Models;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages.Groups;

namespace OVS.Client.ViewModels;

/// <summary>
/// The groups tab: the groups of the server as editable rows, the one selected, a new group, saving it, the order of the
/// saved groups and deleting. The page decides when the tab shows; this holds what happens in it.
/// </summary>
internal sealed partial class GroupListViewModel : ObservableObject
{
    readonly ServerViewModel server;
    readonly Func<AdminPlace, Task<bool>> confirmLeaveAsync;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveGroupCommand), nameof(DeleteGroupCommand), nameof(MoveGroupUpCommand), nameof(MoveGroupDownCommand))]
    GroupEditViewModel? selectedGroup;

    /// <summary>Package 83: why the group would be refused, null while it is fine.</summary>
    [ObservableProperty] string? groupError;

    /// <param name="server">The server whose groups are shown.</param>
    /// <param name="confirmLeaveAsync">Asks before the place is left with changes (the page's question); false = stay.</param>
    internal GroupListViewModel(ServerViewModel server, Func<AdminPlace, Task<bool>> confirmLeaveAsync)
    {
        this.server = server;
        this.confirmLeaveAsync = confirmLeaveAsync;
        GroupSave = server.NewPending();
        GroupReorder = server.NewPending();
        Place = AdminPlace.Create(() => ChangedGroups().Any(), GroupSave, SendChangedGroupsAsync, Discard);
        Rebuild();
    }

    Permission Actor => server.SelfPermissions;

    /// <summary>Package 98: group save and order, until the server's update shows them.</summary>
    internal PendingActionViewModel GroupSave { get; }
    internal PendingActionViewModel GroupReorder { get; }

    internal ObservableCollection<GroupEditViewModel> Groups { get; } = [];

    /// <summary>
    /// Package 114: the place of the tab. It holds changes when any group (or a draft) does, and its save sends every
    /// such group, one after the other (the next once the one before is confirmed); it succeeds when all are confirmed
    /// and fails at the first refusal or invalid name, the later groups are not sent and every unconfirmed group keeps
    /// its edits. The Save button (<see cref="SaveGroupCommand"/>) still sends only the selected group.
    /// </summary>
    internal AdminPlace Place { get; }

    /// <summary>The server's state changed: the groups are built again, and the rights decide the buttons.</summary>
    internal void FollowServer()
    {
        Rebuild();
        NewGroupCommand.NotifyCanExecuteChanged();
        MoveGroupUpCommand.NotifyCanExecuteChanged();
        MoveGroupDownCommand.NotifyCanExecuteChanged();
    }

    void Rebuild()
    {
        var selected = SelectedGroup?.Id;
        var unsaved = Groups.Live().Where(group => group.Id is null).ToList();
        // Package 98: a moved group stays in its new place until the server confirms or refuses it
        var live = Groups.Live().ToList();
        var groups = server.Mirror.Groups.OrderBy(group => pendingGroupOrder?.IndexOf(group.Id) is >= 0 and var position ? position : int.MaxValue)
            .Select(group => Rebuilt(live.FirstOrDefault(row => row.Id == group.Id), group))
            .Concat(unsaved).ToList();
        CollectionSync.Sync(Groups, groups, server.Leave); // Package 102: a deleted group folds away
        SelectGroup(groups.FirstOrDefault(group => group.Id == selected && selected is not null) ?? SelectedGroup switch
        {
            { Id: null } draft => draft,
            _ => groups.FirstOrDefault(),
        });
    }

    // ---- Package 114: unsaved changes ----

    /// <summary>
    /// A rebuilt row would replace the shown one (same key) and drop its edits. An edited row stays (the text box keeps
    /// its focus) and follows the server in what the user did not touch; built for other rights or flags, a new row
    /// takes over only the edits. An untouched row is built anew.
    /// </summary>
    GroupEditViewModel Rebuilt(GroupEditViewModel? old, GroupInfo serverGroup)
    {
        var fresh = new GroupEditViewModel(serverGroup.Id, serverGroup.Name, serverGroup.Permissions, Actor);
        if (old is not { HasChanges: true }) return fresh;
        if (old.BuiltFor != Actor || old.IsReadOnly != fresh.IsReadOnly || old.CanDelete != fresh.CanDelete) return fresh.CarryEdits(old);
        old.Follow(serverGroup.Name, serverGroup.Permissions);
        return old;
    }

    /// <summary>True while the selection changes from code (a rebuild, a new group), not by the user.</summary>
    internal bool IsSelectingInCode { get; private set; }

    void SelectGroup(GroupEditViewModel? group)
    {
        IsSelectingInCode = true;
        try
        {
            SelectedGroup = group;
        }
        finally
        {
            IsSelectingInCode = false;
        }
    }

    void Discard()
    {
        foreach (var group in Groups.Live().Where(group => group.Id is not null)) group.Revert();
        var drafts = Groups.Live().Where(group => group.Id is null).ToList();
        if (drafts.Count == 0) return;
        if (drafts.Contains(SelectedGroup!)) SelectGroup(null);
        CollectionSync.Sync(Groups, Groups.Live().Except(drafts).ToList(), server.Leave);
        if (SelectedGroup is null) SelectGroup(Groups.Live().FirstOrDefault());
    }

    bool CanNewGroup => Actor.Has(Permission.GroupsCreate);

    [RelayCommand(CanExecute = nameof(CanNewGroup))]
    async Task NewGroupAsync()
    {
        if (!await confirmLeaveAsync(Place)) return; // Package 114: the edited group comes first
        var group = new GroupEditViewModel(null, Strings.Group_New, Permission.Speak, Actor);
        Groups.Add(group);
        SelectGroup(group);
    }

    bool CanSaveGroup => SelectedGroup is { IsReadOnly: false };

    [RelayCommand(CanExecute = nameof(CanSaveGroup))]
    Task SaveGroupAsync()
    {
        if (SelectedGroup is { } group && SendGroup(group) is { } sent && group.Id is null) _ = ReplaceDraftAsync(group, sent);
        return Task.CompletedTask;
    }

    /// <summary>The groups (and drafts) that hold edits, in the order of the rows.</summary>
    IEnumerable<GroupEditViewModel> ChangedGroups() => Groups.Live().Where(row => row.HasChanges);

    /// <summary>
    /// Every changed group to the server, one after the other: the next is taken from the rows as they are once the one
    /// before is confirmed (a confirmation rebuilds rows), and a group is tried once. False at the first one that is not
    /// confirmed (refused, no answer, invalid name, no longer editable); that group is selected, so its error shows on its
    /// row, and the later groups are not sent.
    /// </summary>
    async Task<bool> SendChangedGroupsAsync()
    {
        var tried = new HashSet<object>();
        while (ChangedGroups().FirstOrDefault(row => !tried.Contains((object?)row.Id ?? row)) is { } group)
        {
            tried.Add((object?)group.Id ?? group);
            if (SendGroup(group) is not { } sent || await sent.Completion is not null)
            {
                SelectGroup(Groups.Live().FirstOrDefault(row => row.Id is not null && row.Id == group.Id) ?? group); // the answer may have rebuilt the row
                return false;
            }
            if (group.Id is null) RemoveDraft(group);
        }
        return true;
    }

    /// <summary>The group to the server; null when it cannot be saved or the name is invalid.</summary>
    PendingActionViewModel? SendGroup(GroupEditViewModel group)
    {
        if (group.IsReadOnly) return null;
        GroupError = TextRules.NormalizeName(group.Name, ProtocolLimits.MaxGroupNameLength) is null ? Strings.Dlg_NameInvalid : null; // Package 83
        if (GroupError is not null || GroupSave.IsRunning) return null;
        // Package 98: busy until the server's groups arrive; a refused group stays for correcting
        return server.SendConfirmed(group.Id is { } id ? new UpdateGroup(id, group.Name, group.Permissions) : new CreateGroup(group.Name, group.Permissions),
            answer => answer is GroupsChanged, GroupSave, notify: false);
    }

    /// <summary>The draft stays until the server has the group, then gives way to it.</summary>
    async Task ReplaceDraftAsync(GroupEditViewModel draft, PendingActionViewModel sent)
    {
        if (await sent.Completion is null) RemoveDraft(draft);
    }

    void RemoveDraft(GroupEditViewModel draft)
    {
        bool selected = SelectedGroup == draft;
        Groups.Remove(draft);
        if (selected) SelectGroup(Groups.Live().LastOrDefault(group => group.Id is not null && group.Name == draft.Name) ?? Groups.Live().FirstOrDefault());
    }

    // ---- Package 37: order ----

    List<GroupEditViewModel> SavedGroups => Groups.Live().Where(group => group.Id is not null).ToList();

    bool CanMoveGroupUp => Actor.Has(Permission.GroupsManage) && SelectedGroup is { Id: not null } group && SavedGroups.IndexOf(group) > 0;
    bool CanMoveGroupDown => Actor.Has(Permission.GroupsManage) && SelectedGroup is { Id: not null } group &&
                             SavedGroups.IndexOf(group) is var position && position >= 0 && position < SavedGroups.Count - 1;

    [RelayCommand(CanExecute = nameof(CanMoveGroupUp))]
    Task MoveGroupUpAsync() => SelectedGroup is { } group ? MoveGroupAsync(group, SavedGroups[SavedGroups.IndexOf(group) - 1], after: false) : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanMoveGroupDown))]
    Task MoveGroupDownAsync() => SelectedGroup is { } group ? MoveGroupAsync(group, SavedGroups[SavedGroups.IndexOf(group) + 1], after: true) : Task.CompletedTask;

    /// <summary>The group order sent last, shown until the server confirms or refuses it.</summary>
    List<Guid>? pendingGroupOrder;

    /// <summary>
    /// Puts source right before or after target and sends the complete order of the saved groups. Package 98: the new
    /// order shows at once, busy until the server's groups come; refused or unanswered, it goes back.
    /// </summary>
    internal Task MoveGroupAsync(GroupEditViewModel source, GroupEditViewModel target, bool after)
    {
        var saved = SavedGroups;
        if (!Actor.Has(Permission.GroupsManage) || source == target || source.Id is null || target.Id is null) return Task.CompletedTask;
        if (!saved.Contains(source) || !saved.Contains(target)) return Task.CompletedTask; // Package 102: folding away
        var order = saved.Where(group => group != source).ToList();
        order.Insert(order.IndexOf(target) + (after ? 1 : 0), source);
        if (order.SequenceEqual(saved)) return Task.CompletedTask;
        var ids = order.Select(group => group.Id!.Value).ToList();
        pendingGroupOrder = ids;
        Rebuild();
        _ = EndGroupReorderAsync(server.SendConfirmed(new ReorderGroups(ids), answer => answer is GroupsChanged, GroupReorder, notify: false), ids);
        return Task.CompletedTask;
    }

    async Task EndGroupReorderAsync(PendingActionViewModel sent, List<Guid> ids)
    {
        await sent.Completion;
        if (pendingGroupOrder != ids) return; // a newer order is on its way
        pendingGroupOrder = null;
        Rebuild();
    }

    bool CanDeleteGroup => SelectedGroup is { CanDelete: true };

    [RelayCommand(CanExecute = nameof(CanDeleteGroup))]
    async Task DeleteGroupAsync()
    {
        if (SelectedGroup is not { CanDelete: true } group) return;
        if (group.Id is { } id) await server.SendAsync(new DeleteGroup(id));
        else
        {
            // Package 109: a new group dropped before it was saved folds away like a deleted one
            SelectGroup(null);
            CollectionSync.Sync(Groups, Groups.Live().Where(other => other != group).ToList(), server.Leave);
        }
    }
}
