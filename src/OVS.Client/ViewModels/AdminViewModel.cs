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

    public AdminViewModel(ServerViewModel server)
    {
        this.server = server;
        server.AdminMessage += OnAdminMessage;
        server.StateChanged += OnStateChanged;
        server.PropertyChanged += OnServerPropertyChanged;
        serverName = server.Mirror.Settings.Name;
        welcomeText = server.Mirror.Settings.WelcomeText;
        hasPassword = server.Mirror.Settings.HasPassword;
        Links = new LinkMatrixViewModel(server);
        RebuildGroups();
    }

    Permission Actor => server.SelfPermissions;
    public bool ShowGroups => Actor.Has(Permission.GroupsManage);
    public bool ShowUsers => Actor.Has(Permission.GroupsAssign);
    public bool ShowBans => Actor.Has(Permission.UserBan);
    public bool ShowServer => Actor.Has(Permission.ServerConfig);
    public bool ShowLinks => Actor.Has(Permission.ChannelLink);
    public LinkMatrixViewModel Links { get; }

    public ObservableCollection<GroupEditViewModel> Groups { get; } = [];
    public ObservableCollection<KnownUserViewModel> Users { get; } = [];
    public ObservableCollection<BanViewModel> Bans { get; } = [];

    /// <summary>The page asks to be closed (close button or Esc).</summary>
    public event Action? CloseRequested;

    [RelayCommand]
    void Close() => CloseRequested?.Invoke();

    public void Detach()
    {
        server.AdminMessage -= OnAdminMessage;
        server.StateChanged -= OnStateChanged;
        server.PropertyChanged -= OnServerPropertyChanged;
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
    }

    void OnStateChanged()
    {
        OnPropertyChanged(nameof(HasIcon));
        RebuildGroups();
        RebuildUsers();
        Links.Rebuild();
        HasPassword = server.Mirror.Settings.HasPassword;
        OnPropertyChanged(nameof(ShowLinks));
        OnPropertyChanged(nameof(ShowGroups));
        OnPropertyChanged(nameof(ShowUsers));
        OnPropertyChanged(nameof(ShowBans));
        OnPropertyChanged(nameof(ShowServer));
    }

    void OnAdminMessage(Message message)
    {
        switch (message)
        {
            case UserList list:
                knownUsers = list.Users;
                RebuildUsers();
                break;
            case BanList list:
                Bans.Clear();
                foreach (var ban in list.Bans) Bans.Add(new BanViewModel(ban, () => server.SendAsync(new Unban(ban.Id))));
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

    void RebuildUsers()
    {
        Users.Clear();
        foreach (var user in knownUsers.OrderBy(u => u.LastNickname, StringComparer.CurrentCultureIgnoreCase))
        {
            var toggles = server.Mirror.Groups.Select(g => new GroupToggle(g.Id, g.Name, user.GroupIds.Contains(g.Id),
                CanAssign(g), t => ToggleUserGroupAsync(user.Fingerprint, t))).ToList();
            Users.Add(new KnownUserViewModel(user.Fingerprint, user.LastNickname, toggles));
        }
    }

    bool CanAssign(GroupInfo g) =>
        Actor.Has(Permission.GroupsAssign) && (g.Id == WellKnownGroups.Admin ? Permission.All : g.Permissions).IsSubsetOf(Actor);

    async Task ToggleUserGroupAsync(string fingerprint, GroupToggle toggle)
    {
        await server.SendAsync(toggle.IsChecked ? new AssignGroup(fingerprint, toggle.GroupId) : new UnassignGroup(fingerprint, toggle.GroupId));
        await server.SendAsync(new ListUsers());
    }

    [RelayCommand]
    void NewGroup()
    {
        var group = new GroupEditViewModel(null, "Neue Gruppe", Permission.Speak, Actor);
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

    bool CanMoveGroupUp => SelectedGroup is { Id: not null } g && SavedGroups.IndexOf(g) > 0;
    bool CanMoveGroupDown => SelectedGroup is { Id: not null } g && SavedGroups.IndexOf(g) is var i && i >= 0 && i < SavedGroups.Count - 1;

    [RelayCommand(CanExecute = nameof(CanMoveGroupUp))]
    Task MoveGroupUp() => SelectedGroup is { } g ? MoveGroupAsync(g, SavedGroups[SavedGroups.IndexOf(g) - 1], after: false) : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanMoveGroupDown))]
    Task MoveGroupDown() => SelectedGroup is { } g ? MoveGroupAsync(g, SavedGroups[SavedGroups.IndexOf(g) + 1], after: true) : Task.CompletedTask;

    /// <summary>Puts source right before or after target and sends the complete order of the saved groups.</summary>
    public Task MoveGroupAsync(GroupEditViewModel source, GroupEditViewModel target, bool after)
    {
        var saved = SavedGroups;
        if (source == target || source.Id is null || target.Id is null) return Task.CompletedTask;
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

    [RelayCommand]
    Task SaveServerSettings()
    {
        string? password = RemovePassword ? "" : NewPassword.Length > 0 ? NewPassword : null;
        NewPassword = "";
        RemovePassword = false;
        return server.SendAsync(new UpdateServerSettings(ServerName, WelcomeText, password));
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
        IsReadOnly = id == WellKnownGroups.Admin || (id is not null && !permissions.IsSubsetOf(actor));
        CanDelete = id != WellKnownGroups.Admin && id != WellKnownGroups.Guest && permissions.IsSubsetOf(actor);
        Toggles = PermissionLabels.All
            .Select(p => new PermissionToggle(p.Permission, p.Label, permissions.Has(p.Permission), !IsReadOnly && actor.Has(p.Permission)))
            .ToList();
    }

    public Guid? Id { get; }
    public bool IsReadOnly { get; }
    public bool IsEditable => !IsReadOnly;
    public bool CanDelete { get; }
    public IReadOnlyList<PermissionToggle> Toggles { get; }
    public Permission Permissions => Toggles.Where(t => t.IsChecked).Aggregate(Permission.None, (acc, t) => acc | t.Permission);
    public string DisplayName => Id is null ? Name + " (neu)" : Name;
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

public sealed record KnownUserViewModel(string Fingerprint, string Nickname, IReadOnlyList<GroupToggle> Toggles)
{
    public string ShortFingerprint => Fingerprint.Length > 16 ? Fingerprint[..16] : Fingerprint;
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

public sealed partial class BanViewModel(BanInfo ban, Func<Task> unban) : ObservableObject
{
    public BanInfo Ban { get; } = ban;
    public string Text => $"{Ban.Nickname} ({Ban.Fingerprint[..Math.Min(12, Ban.Fingerprint.Length)]})" +
                          (Ban.Ip is null ? "" : $", IP {Ban.Ip}") +
                          $": {Ban.Reason}, von {Ban.CreatedBy}, " +
                          (Ban.ExpiresAt is { } until ? $"bis {until.ToLocalTime():dd.MM.yyyy HH:mm}" : "dauerhaft");

    [RelayCommand]
    Task Unban() => unban();
}
