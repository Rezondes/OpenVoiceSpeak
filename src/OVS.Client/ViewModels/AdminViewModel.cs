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
    DateTimeOffset lastUserRequest = DateTimeOffset.MinValue;
    bool userRequestPending, detached;

    public AdminViewModel(ServerViewModel server)
    {
        this.server = server;
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
        server.AdminMessage += OnAdminMessage;
        server.StateChanged += OnStateChanged;
        server.PropertyChanged += OnServerPropertyChanged;
        serverName = server.Mirror.Settings.Name;
        welcomeText = server.Mirror.Settings.WelcomeText;
        hasPassword = server.Mirror.Settings.HasPassword;
        LoadLimits();
        Links = new LinkMatrixViewModel(server);
        RebuildGroups();
        RebuildGroupFilters();
        RebuildUsers();
    }

    Permission Actor => server.SelfPermissions;
    // Package 76: seeing a tab and acting in it are separate rights
    public bool ShowGroups => Actor.Has(Permission.GroupsView);
    public bool ShowUsers => Actor.Has(Permission.UsersView);
    public bool ShowBans => Actor.Has(Permission.BansView);
    public bool ShowServer => Actor.Has(Permission.ServerConfig);
    public bool ShowLinks => Actor.Has(Permission.ChannelLink);
    public LinkMatrixViewModel Links { get; }

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
    public bool HasNoBanMatches => allBans.Count > 0 && Bans.Count == 0;
    /// <summary>Package 74: the backups on the server, newest first.</summary>
    public ObservableCollection<BackupViewModel> Backups { get; } = [];
    public bool HasNoBackups => Backups.Count == 0;

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
        return png is null ? Task.CompletedTask : server.SendAsync(new SetServerIcon(Convert.ToBase64String(png)));
    }

    [RelayCommand]
    Task RemoveIcon()
    {
        IconError = null;
        return server.SendAsync(new SetServerIcon(null));
    }

    public async Task RequestListsAsync()
    {
        if (ShowUsers) await server.SendAsync(new ListUsers());
        if (ShowBans) await server.SendAsync(new ListBans());
        if (ShowServer) await server.SendAsync(new ListBackups());
    }

    void OnStateChanged()
    {
        OnPropertyChanged(nameof(HasIcon));
        RebuildGroups();
        RebuildGroupFilters();
        // Package 71: online status at once from the live state, the stored data follows with a new list
        var online = OnlineFingerprints();
        if (!online.SetEquals(onlineFingerprints)) _ = RequestUsersSoonAsync();
        onlineFingerprints = online;
        RebuildUsers();
        Links.Rebuild();
        HasPassword = server.Mirror.Settings.HasPassword;
        if (loadedLimits is null) LoadLimits(); // e.g. arriving with the ServerConfig right; later ones never overwrite edits
        OnPropertyChanged(nameof(ShowLinks));
        OnPropertyChanged(nameof(ShowGroups));
        OnPropertyChanged(nameof(ShowUsers));
        OnPropertyChanged(nameof(ShowBans));
        OnPropertyChanged(nameof(ShowServer));
        NewGroupCommand.NotifyCanExecuteChanged();
        MoveGroupUpCommand.NotifyCanExecuteChanged();
        MoveGroupDownCommand.NotifyCanExecuteChanged();
    }

    void OnAdminMessage(Message message)
    {
        switch (message)
        {
            case Error e:
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
            case UserList list:
                ConfirmGroupChanges(list.Users);
                knownUsers = list.Users;
                RebuildUsers();
                break;
            case BackupList list:
                Backups.Clear();
                foreach (var backup in list.Backups) Backups.Add(new BackupViewModel(backup, DeleteBackupAsync, RestoreBackupAsync));
                OnPropertyChanged(nameof(HasNoBackups));
                break;
            case BanList list:
                allBans = list.Bans;
                RebuildBans();
                break;
        }
    }

    void RebuildGroups()
    {
        var selected = SelectedGroup?.Id;
        var unsaved = Groups.Where(g => g.Id is null).ToList();
        Groups.Clear();
        foreach (var g in server.Mirror.Groups) Groups.Add(new GroupEditViewModel(g.Id, g.Name, g.Permissions, Actor));
        foreach (var g in unsaved) Groups.Add(g);
        SelectedGroup = Groups.FirstOrDefault(g => g.Id == selected && selected is not null) ?? SelectedGroup switch
        {
            { Id: null } s => s,
            _ => Groups.FirstOrDefault(),
        };
    }

    // ---- Package 71: user overview ----

    partial void OnSearchTextChanged(string value) => RebuildUsers();
    partial void OnSelectedStatusFilterChanged(Choice<UserStatusFilter> value) => RebuildUsers();
    partial void OnSelectedGroupFilterChanged(Choice<Guid?>? value) => RebuildUsers();
    partial void OnSelectedSortOrderChanged(Choice<UserSortOrder> value) => RebuildUsers();

    HashSet<string> OnlineFingerprints() => server.Mirror.Users.Values.Select(u => u.Fingerprint).ToHashSet();

    /// <summary>Someone came or went: asks for the stored data again, at most once per second.</summary>
    async Task RequestUsersSoonAsync()
    {
        if (!ShowUsers || userRequestPending) return;
        var wait = lastUserRequest + TimeSpan.FromSeconds(1) - server.Time.GetUtcNow();
        if (wait > TimeSpan.Zero)
        {
            userRequestPending = true;
            await Task.Delay(wait, server.Time);
            userRequestPending = false;
        }
        if (detached || !ShowUsers) return;
        lastUserRequest = server.Time.GetUtcNow();
        await server.SendAsync(new ListUsers());
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
        var self = server.Mirror.Self?.Fingerprint;
        var admins = knownUsers.Count(u => u.GroupIds.Contains(WellKnownGroups.Admin));
        var all = knownUsers.Select(user =>
        {
            var toggles = server.Mirror.Groups.Select(g => new GroupToggle(g.Id, g.Name, user.GroupIds.Contains(g.Id),
                CanAssign(g), t => ToggleUserGroupAsync(user.Fingerprint, t))).ToList();
            var bans = (user.Bans ?? []).Where(b => b.LiftedAt is null && (b.ExpiresAt is null || b.ExpiresAt > now)).ToList();
            // Package 72: the server checks the same; never oneself, only users without more rights, never the last admin
            var weaker = user.Fingerprint != self && RightsOf(user.GroupIds).IsSubsetOf(Actor);
            var lastAdmin = admins == 1 && user.GroupIds.Contains(WellKnownGroups.Admin);
            return new KnownUserViewModel(user, onlineFingerprints.Contains(user.Fingerprint), toggles, bans, this,
                canBan: Actor.Has(Permission.UserBan) && weaker && bans.Count == 0,
                canUnban: Actor.Has(Permission.UserBan) && bans.Count > 0,
                canDelete: Actor.Has(Permission.UserDelete) && weaker && !lastAdmin);
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
        Users.Clear();
        foreach (var user in visible) Users.Add(user);
        UserCountText = string.Format(Strings.Ui_UserCount, Users.Count, all.Count);
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
        var all = allBans.Select(ban => new BanViewModel(ban, now, Actor.Has(Permission.UserBan), async () =>
        {
            await server.SendAsync(new Unban(ban.Id)); // answered with the new ban list
            if (ShowUsers) await server.SendAsync(new ListUsers()); // Package 72: the user cards follow
        })).ToList();
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
        Bans.Clear();
        foreach (var ban in visible) Bans.Add(ban);
        BanCountText = string.Format(Strings.Ui_BanCount, Bans.Count, all.Count);
        OnPropertyChanged(nameof(HasNoBans));
        OnPropertyChanged(nameof(HasNoBanMatches));
    }

    /// <summary>What the stored groups give; the Admin group always everything (like PermissionRules.Effective).</summary>
    Permission RightsOf(IReadOnlyList<Guid> groupIds) => groupIds.Contains(WellKnownGroups.Admin)
        ? Permission.All
        : server.Mirror.Groups.Where(g => groupIds.Contains(g.Id)).Aggregate(Permission.None, (acc, g) => acc | g.Permissions);

    // ---- Package 72: ban, unban and delete from a user card, online or offline ----

    internal async Task BanUserAsync(KnownUserViewModel user)
    {
        var ipKnown = user.Info.LastIp is not null;
        if (server.Dialogs.Ban is not { } ban || await ban(user.Nickname, ipKnown) is not { } choice) return;
        await server.SendAsync(new BanUser(user.Fingerprint, choice.Reason, choice.DurationMinutes, choice.IncludeIp && ipKnown));
        await RequestListsAsync();
    }

    internal async Task UnbanUserAsync(KnownUserViewModel user)
    {
        foreach (var ban in user.Bans) await server.SendAsync(new Unban(ban.Id));
        await RequestListsAsync();
    }

    internal async Task DeleteUserAsync(KnownUserViewModel user)
    {
        if (server.Dialogs.ConfirmDeleteUser is not { } confirm || !await confirm(user.Nickname)) return;
        await server.SendAsync(new DeleteUser(user.Fingerprint));
        await RequestListsAsync();
    }

    // ---- Package 74: backups ----

    /// <summary>Answered with the new list.</summary>
    [RelayCommand]
    Task NewBackup() => server.SendAsync(new CreateBackup());

    async Task DeleteBackupAsync(BackupViewModel backup)
    {
        if (server.Dialogs.Confirm is not { } confirm || !await confirm(string.Format(Strings.Backup_ConfirmDelete, backup.Title))) return;
        await server.SendAsync(new DeleteBackup(backup.Info.FileName));
    }

    /// <summary>Everyone, this client too, is disconnected with Restoring once the server accepted the backup.</summary>
    async Task RestoreBackupAsync(BackupViewModel backup)
    {
        if (server.Dialogs.ConfirmRestore is not { } confirm || !await confirm(backup.Title)) return;
        await server.SendAsync(new RestoreBackup(backup.Info.FileName));
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
    async Task<Message?> RequestAsync(Request request)
    {
        var id = await server.SendAsync(request);
        try
        {
            var answer = await Reply(id).Task.WaitAsync(TransferTimeout, server.Time);
            return answer is Error ? null : answer;
        }
        catch (TimeoutException)
        {
            server.ShowNotice(Strings.Backup_TransferFailed);
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
        var temp = target + ".part";
        bool done = false;
        try
        {
            await using (var file = File.Create(temp))
            {
                while (true)
                {
                    if (await RequestAsync(new DownloadBackup(backup.Info.FileName, file.Length)) is not BackupChunk chunk || chunk.Offset != file.Length) return;
                    var bytes = Convert.FromBase64String(chunk.DataBase64);
                    await file.WriteAsync(bytes);
                    Progress(Strings.Backup_Downloading, file.Length, chunk.TotalSize);
                    if (chunk.IsLast) break;
                    if (bytes.Length == 0) return;
                }
            }
            File.Move(temp, target, overwrite: true);
            done = true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
        {
            server.ShowNotice(Strings.Backup_TransferFailed);
        }
        finally
        {
            if (!done) DeleteQuietly(temp);
            replies.Clear(); // answers to other failed requests that came in meanwhile
            IsTransferring = false;
        }
    }

    /// <summary>Sends the file in chunks, each after the server's answer; with restore, asks the red question of Package 74 once it is stored.</summary>
    public async Task UploadBackupAsync(string path, bool restore)
    {
        if (IsTransferring) return;
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
        await server.SendAsync(new RestoreBackup(uploaded.FileName));
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

    bool CanAssign(GroupInfo g) =>
        Actor.Has(Permission.GroupsAssign) && (g.Id == WellKnownGroups.Admin ? Permission.All : g.Permissions).IsSubsetOf(Actor);

    /// <summary>Package 73: group changes sent but not yet seen in a user list; the server sends no confirmation.</summary>
    readonly List<(string RequestId, string Fingerprint, Guid GroupId, bool Assigned)> pendingGroupChanges = [];

    async Task ToggleUserGroupAsync(string fingerprint, GroupToggle toggle)
    {
        bool own = fingerprint == server.Mirror.Self?.Fingerprint;
        if (own) server.OwnGroupChangePending = true; // set before sending: the server's update can come at once
        var id = await server.SendAsync(toggle.IsChecked ? new AssignGroup(fingerprint, toggle.GroupId) : new UnassignGroup(fingerprint, toggle.GroupId));
        pendingGroupChanges.Add((id, fingerprint, toggle.GroupId, toggle.IsChecked));
        UpdateOwnGroupChangePending();
        await server.SendAsync(new ListUsers());
    }

    void UpdateOwnGroupChangePending()
    {
        var self = server.Mirror.Self?.Fingerprint;
        server.OwnGroupChangePending = pendingGroupChanges.Any(p => p.Fingerprint == self);
    }

    /// <summary>Package 73: a tone once a user list shows a pending change; also works for users who are offline.</summary>
    void ConfirmGroupChanges(IReadOnlyList<KnownUserInfo> users)
    {
        int done = pendingGroupChanges.RemoveAll(p => users.Any(u => u.Fingerprint == p.Fingerprint && u.GroupIds.Contains(p.GroupId) == p.Assigned));
        UpdateOwnGroupChangePending();
        if (done > 0) server.RequestSound(Audio.SoundEvent.GroupChangedByMe);
    }

    bool CanNewGroup => Actor.Has(Permission.GroupsCreate);

    [RelayCommand(CanExecute = nameof(CanNewGroup))]
    void NewGroup()
    {
        var group = new GroupEditViewModel(null, Strings.Group_New, Permission.Speak, Actor);
        Groups.Add(group);
        SelectedGroup = group;
    }

    bool CanSaveGroup => SelectedGroup is { IsReadOnly: false };

    [RelayCommand(CanExecute = nameof(CanSaveGroup))]
    Task SaveGroup()
    {
        if (SelectedGroup is not { IsReadOnly: false } g) return Task.CompletedTask;
        return g.Id is { } id
            ? server.SendAsync(new UpdateGroup(id, g.Name, g.Permissions))
            : SaveNewGroupAsync(g);
    }

    async Task SaveNewGroupAsync(GroupEditViewModel g)
    {
        Groups.Remove(g); // the server's GroupsChanged brings it back with an id
        await server.SendAsync(new CreateGroup(g.Name, g.Permissions));
    }

    // ---- Package 37: order ----

    List<GroupEditViewModel> SavedGroups => Groups.Where(g => g.Id is not null).ToList();

    bool CanMoveGroupUp => Actor.Has(Permission.GroupsManage) && SelectedGroup is { Id: not null } g && SavedGroups.IndexOf(g) > 0;
    bool CanMoveGroupDown => Actor.Has(Permission.GroupsManage) && SelectedGroup is { Id: not null } g &&
                             SavedGroups.IndexOf(g) is var i && i >= 0 && i < SavedGroups.Count - 1;

    [RelayCommand(CanExecute = nameof(CanMoveGroupUp))]
    Task MoveGroupUp() => SelectedGroup is { } g ? MoveGroupAsync(g, SavedGroups[SavedGroups.IndexOf(g) - 1], after: false) : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanMoveGroupDown))]
    Task MoveGroupDown() => SelectedGroup is { } g ? MoveGroupAsync(g, SavedGroups[SavedGroups.IndexOf(g) + 1], after: true) : Task.CompletedTask;

    /// <summary>Puts source right before or after target and sends the complete order of the saved groups.</summary>
    public Task MoveGroupAsync(GroupEditViewModel source, GroupEditViewModel target, bool after)
    {
        var saved = SavedGroups;
        if (!Actor.Has(Permission.GroupsManage) || source == target || source.Id is null || target.Id is null) return Task.CompletedTask;
        var order = saved.Where(g => g != source).ToList();
        order.Insert(order.IndexOf(target) + (after ? 1 : 0), source);
        return order.SequenceEqual(saved) ? Task.CompletedTask : server.SendAsync(new ReorderGroups(order.Select(g => g.Id!.Value).ToList()));
    }

    bool CanDeleteGroup => SelectedGroup is { CanDelete: true };

    [RelayCommand(CanExecute = nameof(CanDeleteGroup))]
    async Task DeleteGroup()
    {
        if (SelectedGroup is not { CanDelete: true } g) return;
        if (g.Id is { } id) await server.SendAsync(new DeleteGroup(id));
        else Groups.Remove(g);
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
        string? password = RemovePassword ? "" : NewPassword.Length > 0 ? NewPassword : null;
        NewPassword = "";
        RemovePassword = false;
        // the server checks the ranges again; an emptied number field keeps the last known value
        var limits = loadedLimits is { } l
            ? new ServerLimits((int)(MaxUsers ?? l.MaxUsers), (int)(LogDays ?? l.LogDays), LogRotateDaily, AutoRestart,
                AutoRestartTime is { } t ? TimeOnly.FromTimeSpan(t) : l.AutoRestartTime)
            : null;
        return server.SendAsync(new UpdateServerSettings(ServerName, WelcomeText, password, limits));
    }

    [RelayCommand]
    Task Refresh() => RequestListsAsync();
}

public sealed partial class GroupEditViewModel : ObservableObject
{
    [ObservableProperty] string name;

    public GroupEditViewModel(Guid? id, string name, Permission permissions, Permission actor)
    {
        Id = id;
        this.name = name;
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
    public string DisplayName => Id is null ? string.Format(Strings.Group_Unsaved, Name) : Name;
    /// <summary>Package 37: where a dragged group would land.</summary>
    [ObservableProperty] bool isDropAbove;
    [ObservableProperty] bool isDropBelow;
}

public sealed partial class PermissionToggle(Permission permission, string label, bool isChecked, bool isEnabled) : ObservableObject
{
    [ObservableProperty] bool isChecked = isChecked;

    public Permission Permission { get; } = permission;
    public string Label { get; } = label;
    public bool IsEnabled { get; } = isEnabled;
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
public sealed partial class KnownUserViewModel(KnownUserInfo info, bool isOnline, IReadOnlyList<GroupToggle> toggles, IReadOnlyList<BanInfo> bans,
    AdminViewModel? owner = null, bool canBan = false, bool canUnban = false, bool canDelete = false)
{
    public KnownUserInfo Info { get; } = info;
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
    public bool HasActions => CanBan || CanUnban || CanDelete;

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
public sealed partial class BackupViewModel(BackupInfo info, Func<BackupViewModel, Task> delete, Func<BackupViewModel, Task> restore)
    : ObservableObject
{
    public BackupInfo Info { get; } = info;
    public string Title => TitleOf(Info);
    public static string TitleOf(BackupInfo info) => info.CreatedAt.ToLocalTime().ToString("G") +
                                                     (info.FileName.StartsWith(SafetyPrefix, StringComparison.Ordinal) ? " " + Strings.Backup_Safety : "");
    public string Details => string.Format(Strings.Backup_Details, Size(Info.Size), Info.ServerVersion);

    /// <summary>The prefix the server gives the backup it takes before a restore.</summary>
    const string SafetyPrefix = "vor-wiederherstellung_";

    static string Size(long bytes) =>
        bytes >= 1024 * 1024 ? $"{bytes / (1024d * 1024):0.0} MB" : $"{Math.Max(1, (bytes + 1023) / 1024)} KB";

    [RelayCommand]
    Task Delete() => delete(this);

    [RelayCommand]
    Task Restore() => restore(this);
}

/// <summary>Package 80: one card of the ban overview with everything the server stores about the ban (A97).</summary>
/// <param name="now">The client's time when the list was built: status and remaining time as of then.</param>
public sealed partial class BanViewModel(BanInfo ban, DateTimeOffset now, bool canUnban, Func<Task> unban) : ObservableObject
{
    public BanInfo Ban { get; } = ban;
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
