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
using OVS.Shared.Protocol.Messages.ServerBackups;
using OVS.Shared.Protocol.Messages.ServerInfo;

namespace OVS.Client.ViewModels;

/// <summary>
/// The administration page: it composes the users and bans, links and logs tabs and holds the groups, the backups and the
/// server settings itself. Every tab only shows with the matching right.
/// </summary>
public sealed partial class AdminViewModel : ObservableObject
{
    readonly ServerViewModel server;
    // Package 87: the backup list comes in pages, collected until complete
    readonly ListPages<BackupInfo> backupPages = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveGroupCommand), nameof(DeleteGroupCommand), nameof(MoveGroupUpCommand), nameof(MoveGroupDownCommand))]
    GroupEditViewModel? selectedGroup;
    [ObservableProperty] string serverName = "";
    [ObservableProperty] string welcomeText = "";
    [ObservableProperty] string newPassword = "";
    [ObservableProperty] bool removePassword;
    [ObservableProperty] bool hasPassword;
    // Package 69: limits, logs and restart; null limits (not sent yet) leave them unchanged on save
    ServerLimits? loadedLimits;
    [ObservableProperty] decimal? maxUsers;
    [ObservableProperty] decimal? logDays;
    [ObservableProperty] bool logRotateDaily;
    [ObservableProperty] bool autoRestart;
    [ObservableProperty] TimeSpan? autoRestartTime;
    public AdminViewModel(ServerViewModel server)
    {
        this.server = server;
        UsersAndBans = new UsersAndBansViewModel(server);
        BackupsLoad = server.NewPending();
        BackupCreate = server.NewPending();
        Restoring = server.NewPending();
        GroupSave = server.NewPending();
        GroupReorder = server.NewPending();
        IconUpload = server.NewPending();
        server.StateChanged += OnStateChanged;
        server.PropertyChanged += OnServerPropertyChanged;
        serverName = loadedName = server.Mirror.Settings.Name;
        welcomeText = loadedWelcome = server.Mirror.Settings.WelcomeText;
        seenSettings = server.Mirror.Settings;
        hasPassword = server.Mirror.Settings.HasPassword;
        LoadLimits();
        Links = new LinkMatrixViewModel(server);
        Transfer = new AdminTransfer((request, route) => server.SendAsync(request, route: route), server.Routes.RemoveAll, server.ShowNotice, server.Time);
        Logs = new LogsViewModel(server, Transfer);
        GroupsPlace = AdminPlace.Create(() => Groups.Live().Any(g => g.HasChanges), GroupSave,
            () => SelectedGroup is { HasChanges: true } ? SendGroup() : null, DiscardGroups);
        LinksPlace = AdminPlace.Create(() => Links.HasPending, Links.Applying, Links.SendPending, () => Links.DiscardCommand.Execute(null));
        ServerPlace = AdminPlace.Create(() => ServerSettingsChanged, null, SendServerSettings, ReloadServerSettings);
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
    public bool ShowServerSettings => Actor.Has(Permission.ServerConfig);
    public bool ShowBackups => Actor.Has(Permission.BackupsManage);
    /// <summary>Package 89 (A101): members of the Admin group only, as on the server; all rights alone are not enough.</summary>
    public bool CanUploadRestore => server.IsAdmin;
    public bool ShowLinks => Actor.Has(Permission.ChannelLink);
    public bool ShowLogs => Actor.Has(Permission.LogsView); // Package 81
    public LinkMatrixViewModel Links { get; }
    public LogsViewModel Logs { get; }
    /// <summary>The users and bans tabs.</summary>
    internal UsersAndBansViewModel UsersAndBans { get; }
    /// <summary>Package 97 (A113): the backups' loading state, and the backup actions that wait for the server.</summary>
    public PendingViewModel BackupsLoad { get; }
    public PendingViewModel BackupCreate { get; }
    public PendingViewModel Restoring { get; }
    /// <summary>Package 98: group save and order, and the server logo, until the server's update shows them.</summary>
    public PendingViewModel GroupSave { get; }
    public PendingViewModel GroupReorder { get; }
    public PendingViewModel IconUpload { get; }

    public ObservableCollection<GroupEditViewModel> Groups { get; } = [];
    /// <summary>Package 74: the backups on the server, newest first.</summary>
    public ObservableCollection<BackupViewModel> Backups { get; } = [];
    public bool HasNoBackups => !Backups.Live().Any();
    /// <summary>The page asks to be closed (close button or Esc).</summary>
    public event Action? CloseRequested;

    [RelayCommand]
    void Close() => CloseRequested?.Invoke();

    public void Detach()
    {
        // ponytail: every request the page sent keeps its route until here; see UsersAndBansViewModel.Detach for the lists
        server.Routes.RemoveAll(Apply); // no answer reaches the closed page
        UsersAndBans.Detach();
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
    /// <summary>Package 83: why the server settings or the group would be refused, null while they are fine.</summary>
    [ObservableProperty] string? settingsError;
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
        if (ShowBackups) await SendRoutedAsync(new ListBackups(), pending: BackupsLoad);
        if (ShowLogs) await Logs.RequestAsync();
    }

    void OnStateChanged()
    {
        OnPropertyChanged(nameof(HasIcon));
        RebuildGroups();
        UsersAndBans.OnStateChanged();
        Links.Rebuild();
        HasPassword = server.Mirror.Settings.HasPassword;
        if (loadedLimits is null) LoadLimits(); // e.g. arriving with the ServerConfig right; later ones never overwrite edits
        if (!ReferenceEquals(server.Mirror.Settings, seenSettings)) // Package 114
        {
            seenSettings = server.Mirror.Settings;
            SettingsArrived();
        }
        OnPropertyChanged(nameof(ShowLinks));
        OnPropertyChanged(nameof(ShowGroups));
        OnPropertyChanged(nameof(ShowUsers));
        OnPropertyChanged(nameof(ShowBans));
        OnPropertyChanged(nameof(ShowServer));
        OnPropertyChanged(nameof(ShowServerSettings));
        OnPropertyChanged(nameof(ShowBackups));
        OnPropertyChanged(nameof(ShowLogs));
        if (CanUploadRestore != backupsForAdmin) RebuildBackups(); // Package 89: restore buttons come and go with the Admin group
        Logs.RightsChanged(); // Package 82
        NewGroupCommand.NotifyCanExecuteChanged();
        MoveGroupUpCommand.NotifyCanExecuteChanged();
        MoveGroupDownCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Sends a request whose answers, and the server's error for it, come back to <see cref="Apply"/>.</summary>
    Task<string> SendRoutedAsync(Request request, string? id = null, PendingViewModel? pending = null) => server.SendAsync(request, id, pending, Apply);

    /// <summary>The server's answers to the page's own requests (sent with this as their route) and its errors for them.</summary>
    internal void Apply(Message answer)
    {
        switch (answer)
        {
            case BackupList list:
                if (backupPages.Add(list.RequestId, list.Offset, list.Total, list.Backups, (id, o) => _ = SendRoutedAsync(new ListBackups(o), id)) is not { } backups) break;
                backupInfos = backups;
                BackupsLoad.Done();
                BackupCreate.Done(); // the new backup is in the list (Restoring waits for the disconnect)
                RebuildBackups();
                break;
        }
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

    /// <summary>What the server tab's fields were loaded with (the server's settings then).</summary>
    string loadedName = "", loadedWelcome = "";
    ServerSettingsInfo? seenSettings;

    /// <summary>The server's normal form: a trimmed name, "\r\n" as "\n" (TextRules), so a saved edit is no change.</summary>
    static string NormName(string? name) => name?.Trim() ?? "";
    static string NormText(string? text) => (text ?? "").Replace("\r\n", "\n");

    ServerLimits EditedLimits(ServerLimits l) =>
        new((int)(MaxUsers ?? l.MaxUsers), (int)(LogDays ?? l.LogDays), LogRotateDaily, AutoRestart, AutoRestartTime is { } t ? TimeOnly.FromTimeSpan(t) : l.AutoRestartTime);

    /// <summary>The server tab differs from what its fields were loaded with.</summary>
    bool ServerSettingsChanged =>
        ShowServerSettings && (NormName(ServerName) != NormName(loadedName) || NormText(WelcomeText) != NormText(loadedWelcome)
                               || NewPassword.Length > 0 || RemovePassword || (loadedLimits is { } l && EditedLimits(l) != l));

    /// <summary>
    /// New settings from the server, field by field: an untouched field shows them (a change by another admin), and so
    /// does an edited one that is now what the server has (the own save landed); an edit that still differs stays, and
    /// so do the password fields (only a save empties them).
    /// </summary>
    void SettingsArrived()
    {
        var s = server.Mirror.Settings;
        if (NormName(ServerName) == NormName(loadedName) || NormName(ServerName) == NormName(s.Name)) ServerName = loadedName = s.Name;
        if (NormText(WelcomeText) == NormText(loadedWelcome) || NormText(WelcomeText) == NormText(s.WelcomeText)) WelcomeText = loadedWelcome = s.WelcomeText;
        if (loadedLimits is { } l && s.Limits is { } now && now != l && (EditedLimits(l) == l || EditedLimits(l) == now))
        {
            loadedLimits = null;
            LoadLimits();
        }
    }

    void ReloadServerSettings()
    {
        ServerName = loadedName = server.Mirror.Settings.Name;
        WelcomeText = loadedWelcome = server.Mirror.Settings.WelcomeText;
        NewPassword = "";
        RemovePassword = false;
        SettingsError = null;
        loadedLimits = null;
        LoadLimits();
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

    // ---- Package 74: backups ----

    IReadOnlyList<BackupInfo> backupInfos = [];
    bool backupsForAdmin;

    void RebuildBackups()
    {
        backupsForAdmin = CanUploadRestore;
        OnPropertyChanged(nameof(CanUploadRestore));
        CollectionSync.Sync(Backups, backupInfos.Select(b => new BackupViewModel(b, DeleteBackupAsync, RestoreBackupAsync, backupsForAdmin)).ToList(), server.Leave);
        OnPropertyChanged(nameof(HasNoBackups));
    }

    /// <summary>Package 89 (A101): asked before the file picker; the archive holds the certificate with its private key and all user data.</summary>
    public async Task<bool> ConfirmDownloadAsync() => server.Dialogs.ConfirmBackupDownload is { } warn && await warn();

    /// <summary>Answered with the new list; Package 97: busy until then, a second click meanwhile is ignored.</summary>
    [RelayCommand]
    Task NewBackup() => BackupCreate.IsRunning ? Task.CompletedTask : SendRoutedAsync(new CreateBackup(), pending: BackupCreate);

    async Task DeleteBackupAsync(BackupViewModel backup)
    {
        if (server.Dialogs.Confirm is not { } confirm || !await confirm(string.Format(Strings.Backup_ConfirmDelete, backup.Title))) return;
        await SendRoutedAsync(new DeleteBackup(backup.Info.FileName)); // answered with the new list
    }

    /// <summary>Everyone, this client too, is disconnected with Restoring once the server accepted the backup.</summary>
    async Task RestoreBackupAsync(BackupViewModel backup)
    {
        if (!CanUploadRestore) return;
        if (server.Dialogs.ConfirmRestore is not { } confirm || !await confirm(backup.Title)) return;
        await server.SendAsync(new RestoreBackup(backup.Info.FileName), pending: Restoring); // Package 97: until the disconnect
    }

    // ---- Package 75: download to and upload from this PC, chunk by chunk (A91) ----

    /// <summary>Packages 75 and 82: backups and log files to and from this PC, one transfer at a time; the logs tab shares it.</summary>
    internal AdminTransfer Transfer { get; }

    /// <summary>Saves the backup to target (chosen in the save dialog).</summary>
    internal Task DownloadBackupAsync(BackupViewModel backup, string target) => Transfer.DownloadBackupAsync(backup.Info.FileName, target);

    /// <summary>Uploads the file; with restore, asks the red question of Package 74 once it is stored.</summary>
    internal async Task UploadBackupAsync(string path, bool restore)
    {
        if (!CanUploadRestore || await Transfer.UploadBackupAsync(path, Apply) is not { } uploaded || !restore) return;
        if (server.Dialogs.ConfirmRestore is not { } confirm || !await confirm(BackupViewModel.TitleOf(uploaded))) return;
        await server.SendAsync(new RestoreBackup(uploaded.FileName), pending: Restoring);
    }

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

    public bool HasLimits => loadedLimits is not null;

    void LoadLimits()
    {
        if (server.Mirror.Settings.Limits is not { } l) return;
        loadedLimits = l;
        MaxUsers = l.MaxUsers;
        LogDays = l.LogDays;
        LogRotateDaily = l.LogRotateDaily;
        AutoRestart = l.AutoRestart;
        AutoRestartTime = l.AutoRestartTime.ToTimeSpan();
        OnPropertyChanged(nameof(HasLimits));
    }

    [RelayCommand]
    Task SaveServerSettings()
    {
        SendServerSettings();
        return Task.CompletedTask;
    }

    /// <summary>Package 114: waits for the server's new settings; null when the input breaks the server's rules.</summary>
    PendingViewModel? SendServerSettings()
    {
        // Package 83: the server's rules, checked before anything is sent or cleared
        SettingsError = TextRules.NormalizeName(ServerName, ProtocolLimits.MaxNameLength) is null ? Strings.Dlg_NameInvalid
            : TextRules.NormalizeText(WelcomeText, ProtocolLimits.MaxTextLength) is null ? Strings.Dlg_TextInvalid
            : !RemovePassword && NewPassword.Length > ProtocolLimits.MaxPasswordLength ? Strings.Ui_PasswordTooLong
            : null;
        if (SettingsError is not null) return null;
        string? password = RemovePassword ? "" : NewPassword.Length > 0 ? NewPassword : null;
        NewPassword = "";
        RemovePassword = false;
        // the server checks the ranges again; an emptied number field keeps the last known value
        var limits = loadedLimits is { } l
            ? new ServerLimits((int)(MaxUsers ?? l.MaxUsers), (int)(LogDays ?? l.LogDays), LogRotateDaily, AutoRestart,
                AutoRestartTime is { } t ? TimeOnly.FromTimeSpan(t) : l.AutoRestartTime)
            : null;
        return server.SendConfirmed(new UpdateServerSettings(ServerName, WelcomeText, password, limits), m => m is ServerSettingsChanged);
    }

    [RelayCommand]
    Task Refresh() => RequestListsAsync();
}
