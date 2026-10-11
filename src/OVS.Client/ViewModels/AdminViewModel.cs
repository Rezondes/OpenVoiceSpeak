using OVS.Client.Dialogs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OVS.Client.Models;
using OVS.Client.Services;
using OVS.Shared.Permissions;

namespace OVS.Client.ViewModels;

/// <summary>
/// The administration page: it composes the groups tab, the users and bans tabs, the icon, backup and settings cards of the server tab,
/// the links and logs tabs. Every tab only shows with the matching right. It lists the sections once (<see cref="AdminSection"/>)
/// and asks them to refresh, follow the server and let go of their routes.
/// </summary>
public sealed partial class AdminViewModel : ObservableObject, IPageViewModel
{
    readonly ServerViewModel server;

    public AdminViewModel(ServerViewModel server)
    {
        this.server = server;
        UsersAndBans = new UsersAndBansViewModel(server);
        server.StateChanged += OnServerStateChanged;
        ServerIcon = new ServerIconViewModel(server);
        ServerSettings = new ServerSettingsViewModel(server);
        Links = new LinkMatrixViewModel(server);
        Transfer = server.NewAdminTransfer();
        BackupCard = new BackupCardViewModel(server, Transfer);
        Logs = new LogsViewModel(server, Transfer);
        GroupList = new GroupListViewModel(server, ConfirmLeaveAsync);
        Places = [GroupList.Place, Links.Place, ServerSettings.Place];
        // The lists are asked for in this order (a section without a refresh, like the icon first, is listed only to follow
        // the server and to let go of it); each section owns the right its own code checks (users and bans, backups,
        // settings, logs), the page keeps those of the groups and links tabs, which have no refresh.
        sections =
        [
            new(FollowServer: ServerIcon.FollowServer, Detach: ServerIcon.Detach),
            new(FollowServer: GroupList.FollowServer),
            new(UsersAndBans.RefreshAsync, UsersAndBans.FollowServer, UsersAndBans.Detach),
            new(FollowServer: Links.Rebuild),
            new(FollowServer: ServerSettings.FollowServer),
            new(BackupCard.RefreshAsync, BackupCard.FollowServer, BackupCard.Detach),
            new(Logs.RefreshAsync, Logs.FollowServer, Logs.Detach),
        ];
    }

    readonly IReadOnlyList<AdminSection> sections;

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
    public bool ShowLogs => Logs.ShowLogs; // Package 81
    public LinkMatrixViewModel Links { get; }
    public LogsViewModel Logs { get; }
    /// <summary>The users and bans tabs.</summary>
    internal UsersAndBansViewModel UsersAndBans { get; }
    /// <summary>The backup card of the server tab.</summary>
    internal BackupCardViewModel BackupCard { get; }
    /// <summary>The settings card of the server tab.</summary>
    internal ServerSettingsViewModel ServerSettings { get; }
    /// <summary>The groups tab.</summary>
    internal GroupListViewModel GroupList { get; }
    /// <summary>The server icon in the settings card of the server tab.</summary>
    internal ServerIconViewModel ServerIcon { get; }

    /// <summary>The page asks to be closed (close button or Esc).</summary>
    public event Action? LeaveRequested;

    [RelayCommand]
    void Close() => LeaveRequested?.Invoke();

    public void Detach()
    {
        foreach (var section in sections) section.Detach?.Invoke();
        server.StateChanged -= OnServerStateChanged;
        Transfer.Cancel(); // Package 75: a transfer stops and cleans up
    }

    public async Task RequestListsAsync()
    {
        foreach (var section in sections)
        {
            if (section.Refresh is { } refresh) await refresh();
        }
    }

    void OnServerStateChanged()
    {
        foreach (var section in sections) section.FollowServer?.Invoke();
        OnPropertyChanged(nameof(ShowLinks));
        OnPropertyChanged(nameof(ShowGroups));
        OnPropertyChanged(nameof(ShowUsers));
        OnPropertyChanged(nameof(ShowBans));
        OnPropertyChanged(nameof(ShowServer));
        OnPropertyChanged(nameof(ShowServerSettings));
        OnPropertyChanged(nameof(ShowBackups));
        OnPropertyChanged(nameof(ShowLogs));
    }

    // Package 114: the places that need a manual save; each section builds its own and its tab of the view carries it
    /// <summary>Every place in saving order (the edited group comes first).</summary>
    internal IReadOnlyList<AdminPlace> Places { get; }

    public bool HasChanges => Places.Any(p => p.HasChanges());

    /// <summary>Package 114: saves every place with changes in saving order; false at the first one that fails.</summary>
    public async Task<bool> SaveAsync()
    {
        foreach (var place in Places.Where(candidate => candidate.HasChanges()))
            if (!await place.SaveAsync()) return false; // its error shows on the page, which stays
        return true;
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

    [RelayCommand]
    Task Refresh() => RequestListsAsync();
}
