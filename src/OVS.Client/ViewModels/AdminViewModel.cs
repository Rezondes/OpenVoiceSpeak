using OVS.Client.Localization;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;

namespace OVS.Client.ViewModels;

/// <summary>Groups, user assignments, bans and server settings. Every tab only shows with the matching right.</summary>
public sealed partial class AdminViewModel : ObservableObject
{
    readonly ServerViewModel server;
    IReadOnlyList<KnownUserInfo> knownUsers = [];
    // Package 87: the lists come in pages, collected until complete
    readonly ListPages<KnownUserInfo> userPages = new();
    readonly ListPages<BanInfo> banPages = new();
    readonly ListPages<BackupInfo> backupPages = new();
    // every refresh of the user and ban list goes through these, so quick actions never exceed the server's list limit
    readonly ListRefresh userRefresh, banRefresh;

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
    // Package 71: search, filters and order of the user overview survive every refresh
    [ObservableProperty] string searchText = "";
    [ObservableProperty] Choice<UserStatusFilter> selectedStatusFilter;
    [ObservableProperty] Choice<Guid?>? selectedGroupFilter;
    [ObservableProperty] Choice<UserSortOrder> selectedSortOrder;
    [ObservableProperty] string userCountText = "";
    // Package 80: the same for the ban overview; active bans by default
    IReadOnlyList<BanInfo> allBans = [];
    [ObservableProperty] string banSearchText = "";
    [ObservableProperty] Choice<BanStatusFilter> selectedBanStatusFilter;
    [ObservableProperty] Choice<BanTypeFilter> selectedBanTypeFilter;
    [ObservableProperty] Choice<BanSortOrder> selectedBanSortOrder;
    [ObservableProperty] string banCountText = "";
    HashSet<string> onlineFingerprints;
    // Package 92: the rights the user list's rank flags were judged by; the list is asked for again when they change
    Permission judgedActor;
    List<GroupInfo> judgedGroups;
    bool detached;

    public AdminViewModel(ServerViewModel server)
    {
        this.server = server;
        UsersLoad = server.NewPending();
        BansLoad = server.NewPending();
        BackupsLoad = server.NewPending();
        BackupCreate = server.NewPending();
        Restoring = server.NewPending();
        GroupSave = server.NewPending();
        GroupReorder = server.NewPending();
        IconUpload = server.NewPending();
        userRefresh = new("u", () => !detached && ShowUsers, id => server.SendAsync(new ListUsers(), id, UsersLoad), server.Time);
        banRefresh = new("b", () => !detached && ShowBans, id => server.SendAsync(new ListBans(), id, BansLoad), server.Time);
        // Package 97: a list that never came no longer blocks the next request ("Aktualisieren" retries)
        UsersLoad.TimedOut += userRefresh.Completed;
        BansLoad.TimedOut += banRefresh.Completed;
        StatusFilters =
        [
            new(UserStatusFilter.All, Strings.Ui_StatusAll), new(UserStatusFilter.Online, Strings.Ui_StatusOnline),
            new(UserStatusFilter.Offline, Strings.Ui_StatusOffline), new(UserStatusFilter.Banned, Strings.Ui_StatusBanned),
        ];
        SortOrders =
        [
            new(UserSortOrder.Name, Strings.Ui_SortName), new(UserSortOrder.LastLogin, Strings.Ui_SortLastLogin),
            new(UserSortOrder.OnlineTime, Strings.Ui_SortOnlineTime),
        ];
        selectedStatusFilter = StatusFilters[0];
        selectedSortOrder = SortOrders[0];
        BanStatusFilters =
        [
            new(BanStatusFilter.Active, Strings.Ui_BanStatusActive), new(BanStatusFilter.Expired, Strings.Ui_BanStatusExpired),
            new(BanStatusFilter.Lifted, Strings.Ui_BanStatusLifted), new(BanStatusFilter.All, Strings.Ui_BanStatusAll),
        ];
        BanTypeFilters =
        [
            new(BanTypeFilter.All, Strings.Ui_BanTypeAll), new(BanTypeFilter.Permanent, Strings.Ui_BanTypePermanent),
            new(BanTypeFilter.Temporary, Strings.Ui_BanTypeTemporary), new(BanTypeFilter.WithIp, Strings.Ui_BanTypeWithIp),
        ];
        BanSortOrders =
        [
            new(BanSortOrder.Newest, Strings.Ui_SortNewest), new(BanSortOrder.EndingSoonest, Strings.Ui_SortEndingSoonest),
            new(BanSortOrder.MostAttempts, Strings.Ui_SortMostAttempts), new(BanSortOrder.Name, Strings.Ui_SortName),
        ];
        selectedBanStatusFilter = BanStatusFilters[0];
        selectedBanTypeFilter = BanTypeFilters[0];
        selectedBanSortOrder = BanSortOrders[0];
        onlineFingerprints = OnlineFingerprints();
        judgedActor = Actor;
        judgedGroups = server.Mirror.Groups;
        server.AdminMessage += OnAdminMessage;
        server.StateChanged += OnStateChanged;
        server.PropertyChanged += OnServerPropertyChanged;
        serverName = loadedName = server.Mirror.Settings.Name;
        welcomeText = loadedWelcome = server.Mirror.Settings.WelcomeText;
        seenSettings = server.Mirror.Settings;
        hasPassword = server.Mirror.Settings.HasPassword;
        LoadLimits();
        Links = new LinkMatrixViewModel(server);
        Logs = new LogsViewModel(server, this);
        RebuildGroups();
        RebuildGroupFilters();
        RebuildUsers();
    }

    Permission Actor => server.SelfPermissions;
    // Package 76: seeing a tab and acting in it are separate rights
    public bool ShowGroups => Actor.Has(Permission.GroupsView);
    public bool ShowUsers => Actor.Has(Permission.UsersView);
    public bool ShowBans => Actor.Has(Permission.BansView);
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
    /// <summary>Package 97 (A113): the lists' loading states, and the backup actions that wait for the server.</summary>
    public Pending UsersLoad { get; }
    public Pending BansLoad { get; }
    public Pending BackupsLoad { get; }
    public Pending BackupCreate { get; }
    public Pending Restoring { get; }
    /// <summary>Package 98: group save and order, and the server logo, until the server's update shows them.</summary>
    public Pending GroupSave { get; }
    public Pending GroupReorder { get; }
    public Pending IconUpload { get; }

    public ObservableCollection<GroupEditViewModel> Groups { get; } = [];
    /// <summary>Package 71: the known users that pass search and filters, in the chosen order.</summary>
    public ObservableCollection<KnownUserViewModel> Users { get; } = [];
    public IReadOnlyList<Choice<UserStatusFilter>> StatusFilters { get; }
    public ObservableCollection<Choice<Guid?>> GroupFilters { get; } = [];
    public IReadOnlyList<Choice<UserSortOrder>> SortOrders { get; }
    /// <summary>Package 80: the bans that pass search and filters, in the chosen order.</summary>
    public ObservableCollection<BanViewModel> Bans { get; } = [];
    public IReadOnlyList<Choice<BanStatusFilter>> BanStatusFilters { get; }
    public IReadOnlyList<Choice<BanTypeFilter>> BanTypeFilters { get; }
    public IReadOnlyList<Choice<BanSortOrder>> BanSortOrders { get; }
    /// <summary>No ban stored at all (none ever, or all aged out); otherwise an empty list means nothing matches.</summary>
    public bool HasNoBans => allBans.Count == 0;
    public bool HasNoBanMatches => allBans.Count > 0 && !Bans.Live().Any();
    /// <summary>Package 74: the backups on the server, newest first.</summary>
    public ObservableCollection<BackupViewModel> Backups { get; } = [];
    public bool HasNoBackups => !Backups.Live().Any();
    /// <summary>Package 102: the filter leaves nobody (rows folding away do not count).</summary>
    public bool HasNoUsersShown => !Users.Live().Any();

    /// <summary>The page asks to be closed (close button or Esc).</summary>
    public event Action? CloseRequested;

    [RelayCommand]
    void Close() => CloseRequested?.Invoke();

    public void Detach()
    {
        detached = true;
        server.AdminMessage -= OnAdminMessage;
        server.StateChanged -= OnStateChanged;
        server.PropertyChanged -= OnServerPropertyChanged;
        foreach (var reply in replies.Values) reply.TrySetCanceled(); // Package 75: a transfer stops and cleans up
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
        await RefreshUsersAndBansAsync();
        if (ShowBackups) await server.SendAsync(new ListBackups(), pending: BackupsLoad);
        if (ShowLogs) await Logs.RequestAsync();
    }

    void OnStateChanged()
    {
        OnPropertyChanged(nameof(HasIcon));
        RebuildGroups();
        RebuildGroupFilters();
        // Package 71: online status at once from the live state, the stored data follows with a new list
        var online = OnlineFingerprints();
        // Package 92: new own rights or group rights change on whom one may act; the server judges that anew
        bool rejudge = Actor != judgedActor || server.Mirror.Groups != judgedGroups;
        if (!online.SetEquals(onlineFingerprints) || rejudge) _ = userRefresh.RequestAsync();
        onlineFingerprints = online;
        (judgedActor, judgedGroups) = (Actor, server.Mirror.Groups);
        RebuildUsers();
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

    void OnAdminMessage(Message message)
    {
        switch (message)
        {
            case Error e:
                userRefresh.Failed(e.RequestId);
                banRefresh.Failed(e.RequestId);
                if (pendingGroupChanges.RemoveAll(p => p.RequestId == e.RequestId) > 0) UpdateOwnGroupChangePending();
                if (IsTransferring && e.RequestId is { } failed) Reply(failed).TrySetResult(e);
                break;
            case BackupChunk { RequestId: { } id }:
                Reply(id).TrySetResult(message);
                break;
            case UploadBackupAck { RequestId: { } id }:
                Reply(id).TrySetResult(message);
                break;
            case BackupUploaded { RequestId: { } id }:
                Reply(id).TrySetResult(message);
                break;
            case LogDownloadReady { RequestId: { } id }: // Package 82
                Reply(id).TrySetResult(message);
                break;
            case LogChunk { RequestId: { } id }:
                Reply(id).TrySetResult(message);
                break;
            case UserList list:
                if (userPages.Add(list.RequestId, list.Offset, list.Total, list.Users, (id, o) => _ = server.SendAsync(new ListUsers(o), id)) is not { } users) break;
                userRefresh.Completed();
                UsersLoad.Done();
                ConfirmCards(userRefresh.RoundOf(list.RequestId));
                ConfirmGroupChanges(users, userRefresh.RoundOf(list.RequestId));
                knownUsers = users;
                RebuildUsers();
                break;
            case BackupList list:
                if (backupPages.Add(list.RequestId, list.Offset, list.Total, list.Backups, (id, o) => _ = server.SendAsync(new ListBackups(o), id)) is not { } backups) break;
                backupInfos = backups;
                BackupsLoad.Done();
                BackupCreate.Done(); // the new backup is in the list (Restoring waits for the disconnect)
                RebuildBackups();
                break;
            case BanList list:
                if (banPages.Add(list.RequestId, list.Offset, list.Total, list.Bans, (id, o) => _ = server.SendAsync(new ListBans(o), id)) is not { } bans) break;
                banRefresh.Completed();
                BansLoad.Done();
                foreach (var busy in banBusy.Values) busy.Done(); // Package 98: an unban is answered with the new list
                allBans = bans;
                RebuildBans();
                break;
            case LogList or LogPage or LogSearchResult: // Package 81
                Logs.Apply(message);
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

    /// <summary>The places of this page that need a manual save (A122).</summary>
    public enum Place { Groups, Links, Server }

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

    public bool HasChanges(Place place) => place switch
    {
        Place.Groups => Groups.Live().Any(g => g.HasChanges),
        Place.Links => Links.HasPending,
        _ => ServerSettingsChanged,
    };

    public bool HasAnyChanges => Enum.GetValues<Place>().Any(HasChanges);

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

    /// <summary>Saves the place and waits for the server; false when it refused, did not answer or the input is invalid.</summary>
    public async Task<bool> SaveAsync(Place place)
    {
        // a save already on its way is waited for first, it may be the one the user means
        if (place == Place.Groups && GroupSave.IsRunning) await GroupSave.Completion;
        if (place == Place.Links && Links.Applying.IsRunning) await Links.Applying.Completion;
        if (!HasChanges(place)) return true;
        Pending? sent = place switch
        {
            Place.Groups => SelectedGroup is { HasChanges: true } ? SendGroup() : null,
            Place.Links => Links.SendPending(),
            _ => SendServerSettings(),
        };
        return sent is not null && await sent.Completion is null;
    }

    public void Discard(Place place)
    {
        switch (place)
        {
            case Place.Groups:
                foreach (var g in Groups.Live().Where(g => g.Id is not null)) g.Revert();
                var drafts = Groups.Live().Where(g => g.Id is null).ToList();
                if (drafts.Count == 0) break;
                if (drafts.Contains(SelectedGroup!)) SelectGroup(null);
                CollectionSync.Sync(Groups, Groups.Live().Except(drafts).ToList(), server.Leave);
                if (SelectedGroup is null) SelectGroup(Groups.Live().FirstOrDefault());
                break;
            case Place.Links:
                Links.DiscardCommand.Execute(null);
                break;
            default:
                ReloadServerSettings();
                break;
        }
    }

    /// <summary>Asks before leaving the place (another tab, another group); false = stay.</summary>
    public async Task<bool> ConfirmLeaveAsync(Place place)
    {
        if (!HasChanges(place)) return true;
        switch (server.Dialogs.AskLeave is { } ask ? await ask() : LeaveChoice.Discard)
        {
            case LeaveChoice.Stay:
                return false;
            case LeaveChoice.Save:
                return await SaveAsync(place);
            default:
                Discard(place);
                return true;
        }
    }

    // ---- Package 71: user overview ----

    partial void OnSearchTextChanged(string value) => RebuildUsers();
    partial void OnSelectedStatusFilterChanged(Choice<UserStatusFilter> value) => RebuildUsers();
    partial void OnSelectedGroupFilterChanged(Choice<Guid?>? value) => RebuildUsers();
    partial void OnSelectedSortOrderChanged(Choice<UserSortOrder> value) => RebuildUsers();

    HashSet<string> OnlineFingerprints() => server.Mirror.Users.Values.Select(u => u.Fingerprint).ToHashSet();

    async Task RefreshUsersAndBansAsync()
    {
        var users = userRefresh.RequestAsync();
        await banRefresh.RequestAsync();
        await users;
    }

    /// <summary>Keeps the chosen group when the groups change; a deleted group falls back to "all".</summary>
    void RebuildGroupFilters()
    {
        var wanted = new List<Choice<Guid?>> { new(null, Strings.Ui_AllGroups) };
        wanted.AddRange(server.Mirror.Groups.Select(g => new Choice<Guid?>(g.Id, g.Name)));
        if (wanted.SequenceEqual(GroupFilters)) return;
        var selected = SelectedGroupFilter?.Value;
        GroupFilters.Clear();
        foreach (var choice in wanted) GroupFilters.Add(choice);
        SelectedGroupFilter = GroupFilters.FirstOrDefault(c => c.Value == selected) ?? GroupFilters[0];
    }

    void RebuildUsers()
    {
        var now = server.Time.GetUtcNow();
        var search = SearchText.Trim();
        var status = SelectedStatusFilter?.Value ?? UserStatusFilter.All;
        var group = SelectedGroupFilter?.Value;
        var admins = knownUsers.Count(u => u.GroupIds.Contains(WellKnownGroups.Admin));
        var all = knownUsers.Select(user =>
        {
            // Package 72, 84 (A102): the server checks the same; never oneself, only strictly weaker users, never the last admin.
            // Package 92: the rank comes judged with the list, the client knows no rights of others.
            var weaker = user.CanBeModeratedByMe;
            var lastAdmin = admins == 1 && user.GroupIds.Contains(WellKnownGroups.Admin);
            var toggles = server.Mirror.Groups.Select(g => new GroupToggle(g.Id, g.Name, IsInGroup(user, g.Id),
                g.AssignableByMe && weaker, // Package 92: judged by the server, group rights only reach GroupsView holders
                t => ToggleUserGroupAsync(user.Fingerprint, t))).ToList();
            var bans = (user.Bans ?? []).Where(b => b.LiftedAt is null && (b.ExpiresAt is null || b.ExpiresAt > now)).ToList();
            return new KnownUserViewModel(user, onlineFingerprints.Contains(user.Fingerprint), toggles, bans, this, CardBusy(user.Fingerprint),
                canBan: Actor.Has(Permission.UserBan) && weaker && !lastAdmin && bans.Count == 0,
                canUnban: Actor.Has(Permission.UserBan) && weaker && bans.Count > 0,
                canDelete: Actor.Has(Permission.UserDelete) && weaker && !lastAdmin,
                canLiftMute: Actor.Has(Permission.UserMute) && weaker && user.ServerMuted); // Package 85
        }).ToList();
        var visible = all.Where(u => u.Matches(search) && (group is null || u.Info.GroupIds.Contains(group.Value)) && status switch
        {
            UserStatusFilter.Online => u.IsOnline,
            UserStatusFilter.Offline => !u.IsOnline,
            UserStatusFilter.Banned => u.IsBanned,
            _ => true,
        });
        var byName = StringComparer.CurrentCultureIgnoreCase;
        visible = (SelectedSortOrder?.Value ?? UserSortOrder.Name) switch
        {
            UserSortOrder.LastLogin => visible.OrderByDescending(u => u.Info.LastLogin ?? DateTimeOffset.MinValue).ThenBy(u => u.Nickname, byName),
            UserSortOrder.OnlineTime => visible.OrderByDescending(u => u.Info.OnlineTime).ThenBy(u => u.Nickname, byName),
            _ => visible.OrderBy(u => u.Nickname, byName),
        };
        var shown = visible.ToList();
        CollectionSync.Sync(Users, shown, server.Leave);
        UserCountText = string.Format(Strings.Ui_UserCount, shown.Count, all.Count);
        OnPropertyChanged(nameof(HasNoUsersShown));
    }

    // ---- Package 80: ban overview ----

    partial void OnBanSearchTextChanged(string value) => RebuildBans();
    partial void OnSelectedBanStatusFilterChanged(Choice<BanStatusFilter> value) => RebuildBans();
    partial void OnSelectedBanTypeFilterChanged(Choice<BanTypeFilter> value) => RebuildBans();
    partial void OnSelectedBanSortOrderChanged(Choice<BanSortOrder> value) => RebuildBans();

    void RebuildBans()
    {
        var now = server.Time.GetUtcNow();
        var search = BanSearchText.Trim();
        var status = SelectedBanStatusFilter?.Value ?? BanStatusFilter.All;
        var type = SelectedBanTypeFilter?.Value ?? BanTypeFilter.All;
        var all = allBans.Select(ban => new BanViewModel(ban, now, Actor.Has(Permission.UserBan) && MayActOnBanned(ban.Fingerprint), async () =>
        {
            await server.SendAsync(new Unban(ban.Id), pending: BanBusy(ban.Id)); // answered with the new ban list
            await userRefresh.RequestAsync(); // Package 72: the user cards follow
        }, BanBusy(ban.Id))).ToList();
        var visible = all.Where(b => b.Matches(search) && status switch
        {
            BanStatusFilter.Active => b.Status == BanStatus.Active,
            BanStatusFilter.Expired => b.Status == BanStatus.Expired,
            BanStatusFilter.Lifted => b.Status == BanStatus.Lifted,
            _ => true,
        } && type switch
        {
            BanTypeFilter.Permanent => b.Ban.ExpiresAt is null,
            BanTypeFilter.Temporary => b.Ban.ExpiresAt is not null,
            BanTypeFilter.WithIp => b.HasIp,
            _ => true,
        });
        var byName = StringComparer.CurrentCultureIgnoreCase;
        visible = (SelectedBanSortOrder?.Value ?? BanSortOrder.Newest) switch
        {
            BanSortOrder.EndingSoonest => visible.OrderBy(b => b.Ban.ExpiresAt ?? DateTimeOffset.MaxValue).ThenBy(b => b.Nickname, byName),
            BanSortOrder.MostAttempts => visible.OrderByDescending(b => b.Ban.BlockedAttempts).ThenBy(b => b.Nickname, byName),
            BanSortOrder.Name => visible.OrderBy(b => b.Nickname, byName),
            _ => visible.OrderByDescending(b => b.Ban.CreatedAt ?? DateTimeOffset.MinValue).ThenBy(b => b.Nickname, byName),
        };
        var shown = visible.ToList();
        CollectionSync.Sync(Bans, shown, server.Leave);
        BanCountText = string.Format(Strings.Ui_BanCount, shown.Count, all.Count);
        OnPropertyChanged(nameof(HasNoBans));
        OnPropertyChanged(nameof(HasNoBanMatches));
    }

    /// <summary>
    /// Package 84 (A102), 92: the server's judgement from the user list or the live state; anyone but oneself when the user
    /// is unknown here (without UsersView the list is missing, then the server has the last word).
    /// </summary>
    bool MayActOnBanned(string fingerprint) =>
        knownUsers.FirstOrDefault(u => u.Fingerprint == fingerprint)?.CanBeModeratedByMe
        ?? server.Mirror.Users.Values.FirstOrDefault(u => u.Fingerprint == fingerprint)?.CanBeModeratedByMe
        ?? fingerprint != server.Mirror.Self?.Fingerprint;

    // ---- Package 72: ban, unban and delete from a user card, online or offline ----

    /// <summary>Package 98: the ban dialog stays open until a user list shows the ban.</summary>
    internal Task BanUserAsync(KnownUserViewModel user)
    {
        var ipKnown = user.Info.LastIp is not null;
        return ServerViewModel.Ask<BanChoice>(server.Dialogs.Ban is { } ban ? submit => ban(user.Nickname, ipKnown, submit) : null,
            choice => CardAction(user.Fingerprint, new BanUser(user.Fingerprint, choice.Reason, choice.DurationMinutes, choice.IncludeIp && ipKnown), bans: true));
    }

    internal Task UnbanUserAsync(KnownUserViewModel user)
    {
        foreach (var ban in user.Bans) CardAction(user.Fingerprint, new Unban(ban.Id)); // each answered with the new ban list
        return Task.CompletedTask;
    }

    /// <summary>Package 85: works offline too; the card follows with the next user list.</summary>
    internal Task LiftMuteAsync(KnownUserViewModel user)
    {
        CardAction(user.Fingerprint, new SetStoredServerMute(user.Fingerprint, false));
        return Task.CompletedTask;
    }

    internal async Task DeleteUserAsync(KnownUserViewModel user)
    {
        if (server.Dialogs.ConfirmDeleteUser is not { } confirm || !await confirm(user.Nickname)) return;
        CardAction(user.Fingerprint, new DeleteUser(user.Fingerprint), bans: true);
    }

    // ---- Package 98 (A113): a card's actions spin until a user list asked for after them shows the result ----

    readonly Dictionary<string, Pending> cardBusy = [];
    /// <summary>Per card: the user list requests sent before its last action; a list of a later request settles it.</summary>
    readonly Dictionary<string, int> cardAskedBefore = [];
    readonly Dictionary<Guid, Pending> banBusy = [];

    Pending CardBusy(string fingerprint) => cardBusy.TryGetValue(fingerprint, out var busy) ? busy : cardBusy[fingerprint] = server.NewPending();
    Pending BanBusy(Guid banId) => banBusy.TryGetValue(banId, out var busy) ? busy : banBusy[banId] = server.NewPending();

    /// <summary>Sends under the card's mark, then asks for the list (and with bans the ban list) that shows the result.</summary>
    Pending CardAction(string fingerprint, Request request, bool bans = false)
    {
        var busy = CardBusy(fingerprint);
        cardAskedBefore[fingerprint] = userRefresh.Sent;
        _ = SendAsync();
        return busy;

        async Task SendAsync()
        {
            await server.SendAsync(request, pending: busy);
            if (bans) await RefreshUsersAndBansAsync();
            else await userRefresh.RequestAsync();
        }
    }

    /// <param name="round">The number of the user list request a complete list answers, 0 when unknown.</param>
    void ConfirmCards(int round)
    {
        foreach (var (fingerprint, asked) in cardAskedBefore.ToList())
        {
            if (round <= asked) continue;
            cardAskedBefore.Remove(fingerprint);
            if (cardBusy.GetValueOrDefault(fingerprint) is { IsRunning: true } busy) busy.Done();
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
    Task NewBackup() => BackupCreate.IsRunning ? Task.CompletedTask : server.SendAsync(new CreateBackup(), pending: BackupCreate);

    async Task DeleteBackupAsync(BackupViewModel backup)
    {
        if (server.Dialogs.Confirm is not { } confirm || !await confirm(string.Format(Strings.Backup_ConfirmDelete, backup.Title))) return;
        await server.SendAsync(new DeleteBackup(backup.Info.FileName));
    }

    /// <summary>Everyone, this client too, is disconnected with Restoring once the server accepted the backup.</summary>
    async Task RestoreBackupAsync(BackupViewModel backup)
    {
        if (!CanUploadRestore) return;
        if (server.Dialogs.ConfirmRestore is not { } confirm || !await confirm(backup.Title)) return;
        await server.SendAsync(new RestoreBackup(backup.Info.FileName), pending: Restoring); // Package 97: until the disconnect
    }

    // ---- Package 75: download to and upload from this PC, chunk by chunk (A91) ----

    static readonly TimeSpan TransferTimeout = TimeSpan.FromSeconds(30);
    [ObservableProperty] bool isTransferring;
    [ObservableProperty] double transferPercent;
    [ObservableProperty] string transferText = "";
    /// <summary>Answers by request id; an answer can come before SendAsync returns, so whoever comes first adds the entry.</summary>
    readonly Dictionary<string, TaskCompletionSource<Message>> replies = [];

    TaskCompletionSource<Message> Reply(string requestId)
    {
        if (!replies.TryGetValue(requestId, out var reply))
            replies[requestId] = reply = new TaskCompletionSource<Message>(TaskCreationOptions.RunContinuationsAsynchronously);
        return reply;
    }

    /// <summary>The server's answer to one request, or null (error, timeout, page closed). The server shows its own errors as a notice.</summary>
    async Task<Message?> RequestAsync(Request request, string? failedText = null)
    {
        var id = await server.SendAsync(request);
        try
        {
            var answer = await Reply(id).Task.WaitAsync(TransferTimeout, server.Time);
            return answer is Error ? null : answer;
        }
        catch (TimeoutException)
        {
            server.ShowNotice(failedText ?? Strings.Backup_TransferFailed);
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            replies.Remove(id);
        }
    }

    void Progress(string format, long done, long total)
    {
        TransferPercent = total <= 0 ? 100 : done * 100d / total;
        TransferText = string.Format(format, (int)Math.Round(TransferPercent));
    }

    /// <summary>Writes to a temporary file next to target and moves it into place only once the last chunk arrived.</summary>
    public async Task DownloadBackupAsync(BackupViewModel backup, string target)
    {
        if (IsTransferring) return;
        IsTransferring = true;
        Progress(Strings.Backup_Downloading, 0, 1);
        try
        {
            await ChunkedDownload.RunAsync(target,
                async offset => await RequestAsync(new DownloadBackup(backup.Info.FileName, offset)) is BackupChunk c ? new(c.Offset, c.TotalSize, c.DataBase64, c.IsLast) : null,
                (done, total) => Progress(Strings.Backup_Downloading, done, total));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
        {
            server.ShowNotice(Strings.Backup_TransferFailed);
        }
        finally
        {
            replies.Clear(); // answers to other failed requests that came in meanwhile
            IsTransferring = false;
        }
    }

    /// <summary>
    /// Package 82 (A99): the server copies the chosen log files (one .log or a zip), then they come chunk by chunk like a backup,
    /// with the same progress; one transfer at a time.
    /// </summary>
    internal async Task DownloadLogsAsync(IReadOnlyList<string> fileIds, string target)
    {
        if (IsTransferring || fileIds.Count == 0) return;
        IsTransferring = true;
        Progress(Strings.Logs_Downloading, 0, 1);
        try
        {
            if (await RequestAsync(new PrepareLogDownload(fileIds), Strings.Logs_TransferFailed) is not LogDownloadReady ready) return;
            await ChunkedDownload.RunAsync(target,
                async offset => await RequestAsync(new DownloadLogChunk(ready.DownloadId, offset), Strings.Logs_TransferFailed) is LogChunk c
                    ? new(c.Offset, c.TotalSize, c.DataBase64, c.IsLast) : null,
                (done, total) => Progress(Strings.Logs_Downloading, done, total));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
        {
            server.ShowNotice(Strings.Logs_TransferFailed);
        }
        finally
        {
            replies.Clear(); // answers to other failed requests that came in meanwhile
            IsTransferring = false;
        }
    }

    /// <summary>Sends the file in chunks, each after the server's answer; with restore, asks the red question of Package 74 once it is stored.</summary>
    public async Task UploadBackupAsync(string path, bool restore)
    {
        if (IsTransferring || !CanUploadRestore) return;
        IsTransferring = true;
        Progress(Strings.Backup_Uploading, 0, 1);
        BackupInfo? uploaded = null;
        try
        {
            await using var file = File.OpenRead(path);
            if (file.Length > ProtocolInfo.MaxBackupUploadBytes)
            {
                server.ShowNotice(ErrorTexts.For(Codes.BackupTooLarge));
                return;
            }
            var uploadId = Guid.NewGuid().ToString("N");
            var buffer = new byte[ProtocolInfo.BackupChunkBytes];
            for (long offset = 0; ;)
            {
                int read = await file.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false);
                bool last = offset + read >= file.Length;
                var answer = await RequestAsync(new UploadBackupChunk(uploadId, offset, Convert.ToBase64String(buffer, 0, read), last));
                offset += read;
                Progress(Strings.Backup_Uploading, offset, file.Length);
                if (answer is BackupUploaded u) uploaded = u.Backup;
                if (last || answer is not UploadBackupAck) break;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            server.ShowNotice(Strings.Backup_TransferFailed);
        }
        finally
        {
            replies.Clear(); // answers to other failed requests that came in meanwhile
            IsTransferring = false;
        }
        if (!restore || uploaded is null) return;
        if (server.Dialogs.ConfirmRestore is not { } confirm || !await confirm(BackupViewModel.TitleOf(uploaded))) return;
        await server.SendAsync(new RestoreBackup(uploaded.FileName), pending: Restoring);
    }

    /// <summary>
    /// Package 73: group changes sent but not yet seen in a user list; the server sends no confirmation. AskedBefore is the
    /// number of user list requests sent before the change: a list of a later request settles it either way.
    /// </summary>
    readonly List<(string RequestId, string Fingerprint, Guid GroupId, bool Assigned, int AskedBefore)> pendingGroupChanges = [];

    /// <summary>A box just ticked keeps its state until a list asked for after the tick arrives; an older list would flip it back.</summary>
    bool IsInGroup(KnownUserInfo user, Guid groupId) =>
        pendingGroupChanges.LastOrDefault(p => p.Fingerprint == user.Fingerprint && p.GroupId == groupId) is { RequestId: not null } pending
            ? pending.Assigned
            : user.GroupIds.Contains(groupId);

    async Task ToggleUserGroupAsync(string fingerprint, GroupToggle toggle)
    {
        bool own = fingerprint == server.Mirror.Self?.Fingerprint;
        if (own) server.OwnGroupChangePending = true; // set before sending: the server's update can come at once
        cardAskedBefore[fingerprint] = userRefresh.Sent; // Package 98: the card spins until a later list shows it
        var id = await server.SendAsync(toggle.IsChecked ? new AssignGroup(fingerprint, toggle.GroupId) : new UnassignGroup(fingerprint, toggle.GroupId),
            pending: CardBusy(fingerprint));
        pendingGroupChanges.Add((id, fingerprint, toggle.GroupId, toggle.IsChecked, userRefresh.Sent));
        UpdateOwnGroupChangePending();
        await userRefresh.RequestAsync();
    }

    void UpdateOwnGroupChangePending()
    {
        var self = server.Mirror.Self?.Fingerprint;
        server.OwnGroupChangePending = pendingGroupChanges.Any(p => p.Fingerprint == self);
    }

    /// <summary>Package 73: a tone once a user list shows a pending change; also works for users who are offline.</summary>
    /// <param name="round">The number of the user list request this list answers, 0 when unknown.</param>
    void ConfirmGroupChanges(IReadOnlyList<KnownUserInfo> users, int round)
    {
        int done = pendingGroupChanges.RemoveAll(p => users.Any(u => u.Fingerprint == p.Fingerprint && u.GroupIds.Contains(p.GroupId) == p.Assigned));
        pendingGroupChanges.RemoveAll(p => round > p.AskedBefore); // asked for after the change and still not showing it: the list wins
        UpdateOwnGroupChangePending();
        if (done > 0) server.RequestSound(Audio.SoundEvent.GroupChangedByMe);
    }

    bool CanNewGroup => Actor.Has(Permission.GroupsCreate);

    [RelayCommand(CanExecute = nameof(CanNewGroup))]
    async Task NewGroup()
    {
        if (!await ConfirmLeaveAsync(Place.Groups)) return; // Package 114: the edited group comes first
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
    Pending? SendGroup()
    {
        if (SelectedGroup is not { IsReadOnly: false } g) return null;
        GroupError = TextRules.Name(g.Name, ProtocolInfo.MaxGroupNameLength) is null ? Strings.Dlg_NameInvalid : null; // Package 83
        if (GroupError is not null || GroupSave.IsRunning) return null;
        // Package 98: busy until the server's groups arrive; a refused group stays for correcting
        var sent = server.SendConfirmed(g.Id is { } id ? new UpdateGroup(id, g.Name, g.Permissions) : new CreateGroup(g.Name, g.Permissions),
            m => m is GroupsChanged, GroupSave, notify: false);
        if (g.Id is null) _ = ReplaceDraftAsync(g, sent);
        return sent;
    }

    /// <summary>The draft stays until the server has the group, then gives way to it.</summary>
    async Task ReplaceDraftAsync(GroupEditViewModel draft, Pending sent)
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

    async Task EndGroupReorderAsync(Pending sent, List<Guid> ids)
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
    Pending? SendServerSettings()
    {
        // Package 83: the server's rules, checked before anything is sent or cleared
        SettingsError = TextRules.Name(ServerName, ProtocolInfo.MaxNameLength) is null ? Strings.Dlg_NameInvalid
            : TextRules.Text(WelcomeText, ProtocolInfo.MaxTextLength) is null ? Strings.Dlg_TextInvalid
            : !RemovePassword && NewPassword.Length > ProtocolInfo.MaxPasswordLength ? Strings.Ui_PasswordTooLong
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

public sealed partial class GroupEditViewModel : ObservableObject, IMotionKey
{
    /// <summary>A new group not saved yet counts by itself.</summary>
    public object MotionKey => Id ?? (object)this;
    [ObservableProperty] string name;

    public GroupEditViewModel(Guid? id, string name, Permission permissions, Permission actor)
    {
        Id = id;
        this.name = name;
        serverName = name;
        serverPermissions = permissions;
        BuiltFor = actor;
        // Admin is fixed; a group stronger than the actor could only be edited by escalating.
        // Package 76: saved groups need GroupsManage to edit and GroupsDelete to delete; an unsaved one is the actor's own draft.
        ReadOnlyReason = id is null ? null
            : id == WellKnownGroups.Admin ? Strings.Ui_AdminGroupFixed
            : !permissions.IsSubsetOf(actor) ? Strings.Ui_GroupStronger
            : !actor.Has(Permission.GroupsManage) ? Strings.Ui_GroupViewOnly
            : null;
        IsReadOnly = ReadOnlyReason is not null;
        CanDelete = id is null || (id != WellKnownGroups.Admin && id != WellKnownGroups.Guest && permissions.IsSubsetOf(actor) && actor.Has(Permission.GroupsDelete));
        Toggles = PermissionLabels.All
            .Select(p => new PermissionToggle(p.Permission, p.Label, permissions.Has(p.Permission), !IsReadOnly && actor.Has(p.Permission)))
            .ToList();
    }

    public Guid? Id { get; }
    public bool IsReadOnly { get; }
    public string? ReadOnlyReason { get; }
    public bool IsEditable => !IsReadOnly;
    public bool CanDelete { get; }
    public IReadOnlyList<PermissionToggle> Toggles { get; }
    public Permission Permissions => Toggles.Where(t => t.IsChecked).Aggregate(Permission.None, (acc, t) => acc | t.Permission);

    // ---- Package 114: what the server has, to tell an edit ----
    string serverName = "";
    Permission serverPermissions;

    /// <summary>A new group is unsaved by itself; a saved one when its name or rights differ from the server's.</summary>
    public bool HasChanges => Id is null || (!IsReadOnly && (Name != serverName || Permissions != serverPermissions));

    internal void SetServer(string name, Permission permissions) => (serverName, serverPermissions) = (name, permissions);

    /// <summary>The own rights this row was built for (what may be edited and deleted).</summary>
    public Permission BuiltFor { get; }

    /// <summary>Only what the user changed on the old row (against its server state), as far as this row allows it.</summary>
    internal GroupEditViewModel CarryEdits(GroupEditViewModel old)
    {
        if (IsReadOnly) return this;
        if (old.Name != old.serverName) Name = old.Name;
        foreach (var t in Toggles.Where(t => t.IsEnabled))
            if (old.Toggles.FirstOrDefault(o => o.Permission == t.Permission) is { } edited && edited.IsChecked != old.serverPermissions.Has(t.Permission))
                t.IsChecked = edited.IsChecked;
        return this;
    }

    /// <summary>New server state for an edited row: what the user did not touch follows it, the edits stay.</summary>
    internal void Follow(string name, Permission permissions)
    {
        if (Name == serverName) Name = name;
        foreach (var t in Toggles)
            if (t.IsChecked == serverPermissions.Has(t.Permission)) t.IsChecked = permissions.Has(t.Permission);
        SetServer(name, permissions);
    }

    /// <summary>Back to the server's name and rights.</summary>
    internal void Revert()
    {
        Name = serverName;
        foreach (var t in Toggles) t.IsChecked = serverPermissions.Has(t.Permission);
    }
    public string DisplayName => Id is null ? string.Format(Strings.Group_Unsaved, Name) : Name;
}

public sealed partial class PermissionToggle(Permission permission, string label, bool isChecked, bool isEnabled) : ObservableObject
{
    [ObservableProperty] bool isChecked = isChecked;

    public Permission Permission { get; } = permission;
    public string Label { get; } = label;
    public bool IsEnabled { get; } = isEnabled;
}

/// <summary>Packages 75 and 82: pulls a file chunk by chunk into target.part and moves it into place only after the last chunk,
/// so a failed transfer leaves no half file and the file there before untouched.</summary>
static class ChunkedDownload
{
    public sealed record Chunk(long Offset, long Total, string DataBase64, bool IsLast);

    /// <param name="next">Asks for the chunk at an offset; null stops (error, timeout, page closed).</param>
    /// <returns>True once the file is in place. IO and base64 errors throw, after removing the temporary file.</returns>
    public static async Task<bool> RunAsync(string target, Func<long, Task<Chunk?>> next, Action<long, long> progress)
    {
        var temp = target + ".part";
        bool done = false;
        try
        {
            await using (var file = File.Create(temp))
            {
                while (true)
                {
                    if (await next(file.Length) is not { } chunk || chunk.Offset != file.Length) return false;
                    var bytes = Convert.FromBase64String(chunk.DataBase64);
                    await file.WriteAsync(bytes);
                    progress(file.Length, chunk.Total);
                    if (chunk.IsLast) break;
                    if (bytes.Length == 0) return false;
                }
            }
            File.Move(temp, target, overwrite: true);
            done = true;
            return true;
        }
        finally
        {
            if (!done) DeleteQuietly(temp);
        }
    }

    static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}

public enum UserStatusFilter { All, Online, Offline, Banned }
public enum UserSortOrder { Name, LastLogin, OnlineTime }
public enum BanStatusFilter { Active, Expired, Lifted, All }
public enum BanTypeFilter { All, Permanent, Temporary, WithIp }
public enum BanSortOrder { Newest, EndingSoonest, MostAttempts, Name }
public enum BanStatus { Active, Expired, Lifted }

/// <summary>An entry of a combo box: the value and its text.</summary>
public sealed record Choice<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Package 71: one card of the user overview with everything the server stores about the user (A86).</summary>
/// <param name="busy">Package 98: the card's actions, running until a user list shows their result.</param>
public sealed partial class KnownUserViewModel(KnownUserInfo info, bool isOnline, IReadOnlyList<GroupToggle> toggles, IReadOnlyList<BanInfo> bans,
    AdminViewModel? owner = null, Pending? busy = null, bool canBan = false, bool canUnban = false, bool canDelete = false, bool canLiftMute = false) : IMotionKey
{
    public KnownUserInfo Info { get; } = info;
    public object MotionKey => Info.Fingerprint;
    public Pending Busy { get; } = busy ?? new Pending(TimeProvider.System);
    public string Fingerprint => Info.Fingerprint;
    public string Nickname => Info.LastNickname;
    public string ShortFingerprint => Fingerprint.Length > 16 ? Fingerprint[..16] : Fingerprint;
    public IReadOnlyList<GroupToggle> Toggles { get; } = toggles;
    public bool IsOnline { get; } = isOnline;
    public string StatusText => IsOnline ? Strings.Ui_StatusOnline : Strings.Ui_StatusOffline;
    public IReadOnlyList<BanInfo> Bans { get; } = bans;
    public bool IsBanned => Bans.Count > 0;

    // Package 72: only the actions the viewer may take show on the card
    public bool CanBan { get; } = canBan;
    public bool CanUnban { get; } = canUnban;
    public bool CanDelete { get; } = canDelete;
    /// <summary>Package 85: the stored server mute, applied again on every login.</summary>
    public bool IsServerMuted => Info.ServerMuted;
    public bool CanLiftMute { get; } = canLiftMute;
    public bool HasActions => CanBan || CanUnban || CanDelete || CanLiftMute;

    [RelayCommand(CanExecute = nameof(CanLiftMute))]
    Task LiftMute() => owner?.LiftMuteAsync(this) ?? Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanBan))]
    Task Ban() => owner?.BanUserAsync(this) ?? Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanUnban))]
    Task Unban() => owner?.UnbanUserAsync(this) ?? Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanDelete))]
    Task Delete() => owner?.DeleteUserAsync(this) ?? Task.CompletedTask;

    /// <summary>The longest running ban.</summary>
    public string BanText => Bans.OrderBy(b => b.ExpiresAt ?? DateTimeOffset.MaxValue).LastOrDefault() is not { } ban ? ""
        : string.Format(Strings.Ui_BanDetail, ban.ExpiresAt is { } until ? string.Format(Strings.Ban_Until, until.ToLocalTime()) : Strings.Ban_Forever)
          + (ban.Reason.Length > 0 ? ". " + string.Format(Strings.Ui_BanReason, ban.Reason) : "");

    /// <summary>Users saved before Package 70 have no statistics yet (LoginCount 0): shown as unknown.</summary>
    bool HasStats => Info.LoginCount > 0;
    static string Unknown => Strings.Ui_Unknown;
    public string FirstSeenText => Info.FirstSeen == default ? Unknown : Info.FirstSeen.ToLocalTime().ToString("g");
    public string LastLoginText => Info.LastLogin is { } at ? at.ToLocalTime().ToString("g") : Unknown;
    public string LoginCountText => HasStats ? Info.LoginCount.ToString() : Unknown;
    public string OnlineTimeText => HasStats ? Duration(Info.OnlineTime) : Unknown;
    public string SpeechTimeText => HasStats ? Duration(Info.SpeechTime) : Unknown;
    public string ChatMessagesText => HasStats ? Info.ChatMessages.ToString() : Unknown;
    public string LastIpText => Info.LastIp ?? Unknown;
    public string PreviousNicknamesText => Info.PreviousNicknames is { Count: > 0 } names ? string.Join(", ", names) : HasStats ? Strings.Ui_None : Unknown;

    /// <summary>Case-insensitive, in nickname, previous nicknames, fingerprint and IP.</summary>
    public bool Matches(string search) =>
        search.Length == 0
        || new[] { Nickname, Fingerprint, Info.LastIp ?? "" }.Concat(Info.PreviousNicknames ?? []).Any(v => v.Contains(search, StringComparison.OrdinalIgnoreCase));

    /// <summary>"45 s", "1 min 15 s", "1 h 30 min", "2 T 3 h".</summary>
    public static string Duration(TimeSpan t) => t switch
    {
        { TotalMinutes: < 1 } => $"{t.Seconds} s",
        { TotalHours: < 1 } => t.Seconds == 0 ? $"{t.Minutes} min" : $"{t.Minutes} min {t.Seconds} s",
        { TotalDays: < 1 } => t.Minutes == 0 ? $"{t.Hours} h" : $"{t.Hours} h {t.Minutes} min",
        _ => string.Format(Strings.Ui_DurationDays, (int)t.TotalDays, t.Hours),
    };
}

public sealed partial class GroupToggle(Guid groupId, string name, bool isChecked, bool isEnabled, Func<GroupToggle, Task> onToggle)
    : ObservableObject
{
    [ObservableProperty] bool isChecked = isChecked;

    public Guid GroupId { get; } = groupId;
    public string Name { get; } = name;
    public bool IsEnabled { get; } = isEnabled;

    /// <summary>Bound to the checkbox's Command: IsChecked already holds the new value.</summary>
    [RelayCommand]
    Task Toggle() => onToggle(this);
}

/// <summary>Package 74: one backup on the server: date as title, size and server version below.</summary>
/// <param name="canRestore">Package 89: only members of the Admin group see the restore button.</param>
public sealed partial class BackupViewModel(BackupInfo info, Func<BackupViewModel, Task> delete, Func<BackupViewModel, Task> restore, bool canRestore = false)
    : ObservableObject, IMotionKey
{
    public object MotionKey => Info.FileName;
    public BackupInfo Info { get; } = info;
    public bool CanRestore { get; } = canRestore;
    public string Title => TitleOf(Info);
    public static string TitleOf(BackupInfo info) => info.CreatedAt.ToLocalTime().ToString("G") +
                                                     (info.FileName.StartsWith(SafetyPrefix, StringComparison.Ordinal) ? " " + Strings.Backup_Safety : "");
    public string Details => string.Format(Strings.Backup_Details, Size(Info.Size), Info.ServerVersion);

    /// <summary>The prefix the server gives the backup it takes before a restore.</summary>
    const string SafetyPrefix = "vor-wiederherstellung_";

    internal static string Size(long bytes) =>
        bytes >= 1024 * 1024 ? $"{bytes / (1024d * 1024):0.0} MB" : $"{Math.Max(1, (bytes + 1023) / 1024)} KB";

    [RelayCommand]
    Task Delete() => delete(this);

    [RelayCommand]
    Task Restore() => restore(this);
}

/// <summary>Package 80: one card of the ban overview with everything the server stores about the ban (A97).</summary>
/// <param name="now">The client's time when the list was built: status and remaining time as of then.</param>
/// <param name="busy">Package 98: the unban, running until the new ban list comes.</param>
public sealed partial class BanViewModel(BanInfo ban, DateTimeOffset now, bool canUnban, Func<Task> unban, Pending? busy = null) : ObservableObject, IMotionKey
{
    public object MotionKey => Ban.Id;
    public BanInfo Ban { get; } = ban;
    public Pending Busy { get; } = busy ?? new Pending(TimeProvider.System);
    public string Nickname => Ban.Nickname;
    public string Fingerprint => Ban.Fingerprint;
    public string ShortFingerprint => Fingerprint.Length > 16 ? Fingerprint[..16] : Fingerprint;
    public bool HasIp => Ban.Ip is not null;
    public string Ip => Ban.Ip ?? "";
    public string ReasonText => Ban.Reason.Length > 0 ? Ban.Reason : Strings.Ui_BanNoReason;
    public string CreatedBy => Ban.CreatedBy;

    public BanStatus Status { get; } = StatusOf(ban, now);
    static BanStatus StatusOf(BanInfo b, DateTimeOffset now) =>
        b.LiftedAt is not null ? BanStatus.Lifted : b.ExpiresAt is { } end && end <= now ? BanStatus.Expired : BanStatus.Active;
    public string StatusText => Status switch
    {
        BanStatus.Lifted => string.Format(Strings.Ui_BanLifted, Ban.LiftedBy, Local(Ban.LiftedAt)),
        BanStatus.Expired => Strings.Ui_BanExpired,
        _ => Strings.Ui_BanActive,
    };
    public bool IsActive => Status == BanStatus.Active;

    // Bans saved before Package 80 have no creation time, creator fingerprint or duration: shown as unknown
    static string Unknown => Strings.Ui_Unknown;
    static string Local(DateTimeOffset? at) => at?.ToLocalTime().ToString("g") ?? Unknown;
    public string CreatedAtText => Local(Ban.CreatedAt);
    public string CreatedByFingerprintText => Ban.CreatedByFingerprint ?? Unknown;
    public string DurationText => Ban.ExpiresAt is null ? Strings.Ban_Forever
        : Ban.DurationMinutes is { } minutes ? KnownUserViewModel.Duration(TimeSpan.FromMinutes(minutes)) : Unknown;
    public string EndText => Ban.ExpiresAt is null ? Strings.Ban_Forever : Local(Ban.ExpiresAt);
    public bool HasRemaining => IsActive && Ban.ExpiresAt is not null;
    public string RemainingText => HasRemaining ? KnownUserViewModel.Duration(Ban.ExpiresAt!.Value - now) : "";
    public string AttemptsText => Ban.BlockedAttempts == 0 ? Strings.Ui_None
        : string.Format(Strings.Ui_BanAttemptsFmt, Ban.BlockedAttempts, Local(Ban.LastAttempt), Ban.LastAttemptIp ?? Unknown);

    /// <summary>Case-insensitive, in nickname, fingerprint, IP, reason, creator and lifter.</summary>
    public bool Matches(string search) =>
        search.Length == 0
        || new[] { Nickname, Fingerprint, Ban.Ip, Ban.Reason, Ban.CreatedBy, Ban.LiftedBy }.Any(v => v?.Contains(search, StringComparison.OrdinalIgnoreCase) == true);

    /// <summary>Package 76: the list is readable with BansView, lifting a ban needs UserBan; Package 80: only active ones.</summary>
    public bool CanUnban { get; } = canUnban && StatusOf(ban, now) == BanStatus.Active;

    [RelayCommand(CanExecute = nameof(CanUnban))]
    Task Unban() => unban();
}
