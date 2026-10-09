using OVS.Client.Dialogs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OVS.Client.Models;
using OVS.Client.Services;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol.Messages.ServerInfo;

namespace OVS.Client.ViewModels;

/// <summary>
/// The administration page: it composes the groups tab, the users and bans tabs, the backup and settings cards of the server tab,
/// the links and logs tabs. Every tab only shows with the matching right.
/// </summary>
public sealed partial class AdminViewModel : ObservableObject
{
    readonly ServerViewModel server;

    public AdminViewModel(ServerViewModel server)
    {
        this.server = server;
        UsersAndBans = new UsersAndBansViewModel(server);
        IconUpload = server.NewPending();
        server.StateChanged += OnStateChanged;
        server.PropertyChanged += OnServerPropertyChanged;
        ServerSettings = new ServerSettingsViewModel(server);
        Links = new LinkMatrixViewModel(server);
        Transfer = new AdminTransfer((request, route) => server.SendAsync(request, route: route), server.Routes.RemoveAll, server.ShowNotice, server.Time);
        BackupCard = new BackupCardViewModel(server, Transfer);
        Logs = new LogsViewModel(server, Transfer);
        GroupList = new GroupListViewModel(server, ConfirmLeaveAsync);
        Places = [GroupList.Place, Links.Place, ServerSettings.Place];
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
    /// <summary>The groups tab.</summary>
    internal GroupListViewModel GroupList { get; }
    /// <summary>Package 98: the server logo, until the server's update shows it.</summary>
    public PendingViewModel IconUpload { get; }

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
        GroupList.OnStateChanged();
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
    }

    // Package 114: the places that need a manual save; each section builds its own and its tab of the view carries it
    /// <summary>Every place in saving order (the edited group comes first).</summary>
    internal IReadOnlyList<AdminPlace> Places { get; }

    public bool HasAnyChanges => Places.Any(p => p.HasChanges());

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
