using OVS.Client.Collections;
using OVS.Client.Dialogs;
using OVS.Client.Localization;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OVS.Client.Models;
using OVS.Client.Services;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Groups;
using OVS.Shared.Protocol.Messages.ServerInfo;

namespace OVS.Client.ViewModels;

/// <summary>
/// The administration page: it composes the users and bans tabs, the backup and settings cards of the server tab, the links and logs tabs
/// and holds the groups itself. Every tab only shows with the matching right.
/// </summary>
public sealed partial class AdminViewModel : ObservableObject
{
    readonly ServerViewModel server;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveGroupCommand), nameof(DeleteGroupCommand), nameof(MoveGroupUpCommand), nameof(MoveGroupDownCommand))]
    GroupEditViewModel? selectedGroup;
    public AdminViewModel(ServerViewModel server)
    {
        this.server = server;
        UsersAndBans = new UsersAndBansViewModel(server);
        GroupSave = server.NewPending();
        GroupReorder = server.NewPending();
        IconUpload = server.NewPending();
        server.StateChanged += OnStateChanged;
        server.PropertyChanged += OnServerPropertyChanged;
        ServerSettings = new ServerSettingsViewModel(server);
        Links = new LinkMatrixViewModel(server);
        Transfer = new AdminTransfer((request, route) => server.SendAsync(request, route: route), server.Routes.RemoveAll, server.ShowNotice, server.Time);
        BackupCard = new BackupCardViewModel(server, Transfer);
        Logs = new LogsViewModel(server, Transfer);
        GroupsPlace = AdminPlace.Create(() => Groups.Live().Any(g => g.HasChanges), GroupSave,
            () => SelectedGroup is { HasChanges: true } ? SendGroup() : null, DiscardGroups);
        LinksPlace = AdminPlace.Create(() => Links.HasPending, Links.Applying, Links.SendPending, () => Links.DiscardCommand.Execute(null));
        ServerPlace = AdminPlace.Create(() => ServerSettings.HasChanges, null, ServerSettings.Send, ServerSettings.Reload);
        Places = [GroupsPlace, LinksPlace, ServerPlace];
        RebuildGroups();
    }

    Permission Actor => server.SelfPermissions;
    // Package 76: seeing a tab and acting in it are separate rights
    public bool ShowGroups => Actor.Has(Permission.GroupsView);
    public bool ShowUsers => UsersAndBans.ShowUsers;
    public bool ShowBans => UsersAndBans.ShowBans;
    /// <summary>Package 89: the server tab holds the settings (ServerConfig) and the backups (BackupsManage).</summary>
    public bool ShowServer => ShowServerSettings || ShowBackups;
    public bool ShowServerSettings => ServerSettings.ShowServerSettings;
    public bool ShowBackups => BackupCard.ShowBackups;
    public bool ShowLinks => Actor.Has(Permission.ChannelLink);
    public bool ShowLogs => Actor.Has(Permission.LogsView); // Package 81
    public LinkMatrixViewModel Links { get; }
    public LogsViewModel Logs { get; }
    /// <summary>The users and bans tabs.</summary>
    internal UsersAndBansViewModel UsersAndBans { get; }
    /// <summary>The backup card of the server tab.</summary>
    internal BackupCardViewModel BackupCard { get; }
    /// <summary>The settings card of the server tab.</summary>
    internal ServerSettingsViewModel ServerSettings { get; }
    /// <summary>Package 98: group save and order, and the server logo, until the server's update shows them.</summary>
    public PendingViewModel GroupSave { get; }
    public PendingViewModel GroupReorder { get; }
    public PendingViewModel IconUpload { get; }

    public ObservableCollection<GroupEditViewModel> Groups { get; } = [];
    /// <summary>The page asks to be closed (close button or Esc).</summary>
    public event Action? CloseRequested;

    [RelayCommand]
    void Close() => CloseRequested?.Invoke();

    public void Detach()
    {
        UsersAndBans.Detach();
        BackupCard.Detach();
        Logs.Detach();
        server.StateChanged -= OnStateChanged;
        server.PropertyChanged -= OnServerPropertyChanged;
        Transfer.Cancel(); // Package 75: a transfer stops and cleans up
    }

    // ---- Server logo (Package 30) ----

    public byte[]? IconPng => server.IconPng;
    public bool HasIcon => server.IconHash is not null;
    public string ServerInitial => server.ServerName;
    [ObservableProperty] string? iconError;
    /// <summary>Package 83: why the group would be refused, null while it is fine.</summary>
    [ObservableProperty] string? groupError;

    void OnServerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ServerViewModel.IconPng)) return;
        OnPropertyChanged(nameof(IconPng));
        OnPropertyChanged(nameof(HasIcon));
    }

    /// <summary>Takes an already checked and scaled PNG (IconImport) or the reason it was refused.</summary>
    public Task UploadIconAsync(byte[]? png, string? error)
    {
        IconError = error;
        if (png is not null) SendIcon(Convert.ToBase64String(png));
        return Task.CompletedTask;
    }

    [RelayCommand]
    void RemoveIcon()
    {
        IconError = null;
        SendIcon(null);
    }

    /// <summary>Package 98: the logo spins until the server's new settings carry it.</summary>
    void SendIcon(string? pngBase64)
    {
        if (!IconUpload.IsRunning) server.SendConfirmed(new SetServerIcon(pngBase64), m => m is ServerSettingsChanged, IconUpload, notify: false);
    }

    public async Task RequestListsAsync()
    {
        await UsersAndBans.RefreshAsync();
        await BackupCard.RequestAsync();
        if (ShowLogs) await Logs.RequestAsync();
    }

    void OnStateChanged()
    {
        OnPropertyChanged(nameof(HasIcon));
        RebuildGroups();
        UsersAndBans.OnStateChanged();
        Links.Rebuild();
        ServerSettings.OnStateChanged();
        OnPropertyChanged(nameof(ShowLinks));
        OnPropertyChanged(nameof(ShowGroups));
        OnPropertyChanged(nameof(ShowUsers));
        OnPropertyChanged(nameof(ShowBans));
        OnPropertyChanged(nameof(ShowServer));
        OnPropertyChanged(nameof(ShowServerSettings));
        OnPropertyChanged(nameof(ShowBackups));
        OnPropertyChanged(nameof(ShowLogs));
        BackupCard.OnStateChanged();
        Logs.RightsChanged(); // Package 82
        NewGroupCommand.NotifyCanExecuteChanged();
        MoveGroupUpCommand.NotifyCanExecuteChanged();
        MoveGroupDownCommand.NotifyCanExecuteChanged();
    }

    void RebuildGroups()
    {
        var selected = SelectedGroup?.Id;
        var unsaved = Groups.Live().Where(g => g.Id is null).ToList();
        // Package 98: a moved group stays in its new place until the server confirms or refuses it
        var live = Groups.Live().ToList();
        var groups = server.Mirror.Groups.OrderBy(g => pendingGroupOrder?.IndexOf(g.Id) is >= 0 and var i ? i : int.MaxValue)
            .Select(g => Rebuilt(live.FirstOrDefault(row => row.Id == g.Id), g))
            .Concat(unsaved).ToList();
        CollectionSync.Sync(Groups, groups, server.Leave); // Package 102: a deleted group folds away
        SelectGroup(groups.FirstOrDefault(g => g.Id == selected && selected is not null) ?? SelectedGroup switch
        {
            { Id: null } s => s,
            _ => groups.FirstOrDefault(),
        });
    }

    // ---- Package 114: unsaved changes ----

    /// <summary>
    /// A rebuilt row would replace the shown one (same key) and drop its edits. An edited row stays (the text box keeps
    /// its focus) and follows the server in what the user did not touch; built for other rights or flags, a new row
    /// takes over only the edits. An untouched row is built anew.
    /// </summary>
    GroupEditViewModel Rebuilt(GroupEditViewModel? old, GroupInfo server)
    {
        var fresh = new GroupEditViewModel(server.Id, server.Name, server.Permissions, Actor);
        if (old is not { HasChanges: true }) return fresh;
        if (old.BuiltFor != Actor || old.IsReadOnly != fresh.IsReadOnly || old.CanDelete != fresh.CanDelete) return fresh.CarryEdits(old);
        old.Follow(server.Name, server.Permissions);
        return old;
    }

    /// <summary>True while the selection changes from code (a rebuild, a new group), not by the user.</summary>
    public bool IsSelectingInCode { get; private set; }

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

    // Package 114: the places that need a manual save; each tab of the view carries its own
    internal AdminPlace GroupsPlace { get; }
    internal AdminPlace LinksPlace { get; }
    internal AdminPlace ServerPlace { get; }

    /// <summary>Every place in saving order (the edited group comes first).</summary>
    internal IReadOnlyList<AdminPlace> Places { get; }

    public bool HasAnyChanges => Places.Any(p => p.HasChanges());

    void DiscardGroups()
    {
        foreach (var g in Groups.Live().Where(g => g.Id is not null)) g.Revert();
        var drafts = Groups.Live().Where(g => g.Id is null).ToList();
        if (drafts.Count == 0) return;
        if (drafts.Contains(SelectedGroup!)) SelectGroup(null);
        CollectionSync.Sync(Groups, Groups.Live().Except(drafts).ToList(), server.Leave);
        if (SelectedGroup is null) SelectGroup(Groups.Live().FirstOrDefault());
    }

    /// <summary>Asks before leaving the place (another tab, another group); false = stay.</summary>
    internal async Task<bool> ConfirmLeaveAsync(AdminPlace place)
    {
        if (!place.HasChanges()) return true;
        switch (server.Dialogs.AskLeave is { } ask ? await ask() : LeaveChoice.Discard)
        {
            case LeaveChoice.Stay:
                return false;
            case LeaveChoice.Save:
                return await place.SaveAsync();
            default:
                place.Discard();
                return true;
        }
    }

    /// <summary>Packages 75 and 82: backups and log files to and from this PC, one transfer at a time; the backup card and the logs tab share it.</summary>
    internal AdminTransfer Transfer { get; }

    bool CanNewGroup => Actor.Has(Permission.GroupsCreate);

    [RelayCommand(CanExecute = nameof(CanNewGroup))]
    async Task NewGroup()
    {
        if (!await ConfirmLeaveAsync(GroupsPlace)) return; // Package 114: the edited group comes first
        var group = new GroupEditViewModel(null, Strings.Group_New, Permission.Speak, Actor);
        Groups.Add(group);
        SelectGroup(group);
    }

    bool CanSaveGroup => SelectedGroup is { IsReadOnly: false };

    [RelayCommand(CanExecute = nameof(CanSaveGroup))]
    Task SaveGroup()
    {
        SendGroup();
        return Task.CompletedTask;
    }

    /// <summary>The selected group to the server; null when there is nothing to send or the name is invalid.</summary>
    PendingViewModel? SendGroup()
    {
        if (SelectedGroup is not { IsReadOnly: false } g) return null;
        GroupError = TextRules.NormalizeName(g.Name, ProtocolLimits.MaxGroupNameLength) is null ? Strings.Dlg_NameInvalid : null; // Package 83
        if (GroupError is not null || GroupSave.IsRunning) return null;
        // Package 98: busy until the server's groups arrive; a refused group stays for correcting
        var sent = server.SendConfirmed(g.Id is { } id ? new UpdateGroup(id, g.Name, g.Permissions) : new CreateGroup(g.Name, g.Permissions),
            m => m is GroupsChanged, GroupSave, notify: false);
        if (g.Id is null) _ = ReplaceDraftAsync(g, sent);
        return sent;
    }

    /// <summary>The draft stays until the server has the group, then gives way to it.</summary>
    async Task ReplaceDraftAsync(GroupEditViewModel draft, PendingViewModel sent)
    {
        if (await sent.Completion is not null) return;
        bool selected = SelectedGroup == draft;
        Groups.Remove(draft);
        if (selected) SelectGroup(Groups.Live().LastOrDefault(g => g.Id is not null && g.Name == draft.Name) ?? Groups.Live().FirstOrDefault());
    }

    // ---- Package 37: order ----

    List<GroupEditViewModel> SavedGroups => Groups.Live().Where(g => g.Id is not null).ToList();

    bool CanMoveGroupUp => Actor.Has(Permission.GroupsManage) && SelectedGroup is { Id: not null } g && SavedGroups.IndexOf(g) > 0;
    bool CanMoveGroupDown => Actor.Has(Permission.GroupsManage) && SelectedGroup is { Id: not null } g &&
                             SavedGroups.IndexOf(g) is var i && i >= 0 && i < SavedGroups.Count - 1;

    [RelayCommand(CanExecute = nameof(CanMoveGroupUp))]
    Task MoveGroupUp() => SelectedGroup is { } g ? MoveGroupAsync(g, SavedGroups[SavedGroups.IndexOf(g) - 1], after: false) : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanMoveGroupDown))]
    Task MoveGroupDown() => SelectedGroup is { } g ? MoveGroupAsync(g, SavedGroups[SavedGroups.IndexOf(g) + 1], after: true) : Task.CompletedTask;

    /// <summary>The group order sent last, shown until the server confirms or refuses it.</summary>
    List<Guid>? pendingGroupOrder;

    /// <summary>
    /// Puts source right before or after target and sends the complete order of the saved groups. Package 98: the new
    /// order shows at once, busy until the server's groups come; refused or unanswered, it goes back.
    /// </summary>
    public Task MoveGroupAsync(GroupEditViewModel source, GroupEditViewModel target, bool after)
    {
        var saved = SavedGroups;
        if (!Actor.Has(Permission.GroupsManage) || source == target || source.Id is null || target.Id is null) return Task.CompletedTask;
        if (!saved.Contains(source) || !saved.Contains(target)) return Task.CompletedTask; // Package 102: folding away
        var order = saved.Where(g => g != source).ToList();
        order.Insert(order.IndexOf(target) + (after ? 1 : 0), source);
        if (order.SequenceEqual(saved)) return Task.CompletedTask;
        var ids = order.Select(g => g.Id!.Value).ToList();
        pendingGroupOrder = ids;
        RebuildGroups();
        _ = EndGroupReorderAsync(server.SendConfirmed(new ReorderGroups(ids), m => m is GroupsChanged, GroupReorder, notify: false), ids);
        return Task.CompletedTask;
    }

    async Task EndGroupReorderAsync(PendingViewModel sent, List<Guid> ids)
    {
        await sent.Completion;
        if (pendingGroupOrder != ids) return; // a newer order is on its way
        pendingGroupOrder = null;
        RebuildGroups();
    }

    bool CanDeleteGroup => SelectedGroup is { CanDelete: true };

    [RelayCommand(CanExecute = nameof(CanDeleteGroup))]
    async Task DeleteGroup()
    {
        if (SelectedGroup is not { CanDelete: true } g) return;
        if (g.Id is { } id) await server.SendAsync(new DeleteGroup(id));
        else
        {
            // Package 109: a new group dropped before it was saved folds away like a deleted one
            SelectGroup(null);
            CollectionSync.Sync(Groups, Groups.Live().Where(other => other != g).ToList(), server.Leave);
        }
    }

    [RelayCommand]
    Task Refresh() => RequestListsAsync();
}
