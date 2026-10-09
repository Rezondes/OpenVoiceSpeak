using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OVS.Shared.Protocol.Messages.ServerInfo;

namespace OVS.Client.ViewModels;

/// <summary>
/// The icon part of the settings card on the server tab: the server's icon as the server shows it, setting a new one
/// (already checked and scaled by <c>IconImport</c>) and removing it. It follows the server's icon itself and lets go of
/// the server when the page closes.
/// </summary>
internal sealed partial class ServerIconViewModel : ObservableObject
{
    readonly ServerViewModel server;

    internal ServerIconViewModel(ServerViewModel server)
    {
        this.server = server;
        IconUpload = server.NewPending();
        server.PropertyChanged += OnServerPropertyChanged;
    }

    /// <summary>Package 98: the server icon, until the server's update shows it.</summary>
    internal PendingViewModel IconUpload { get; }

    internal byte[]? IconPng => server.IconPng;
    internal bool HasIcon => server.IconHash is not null;
    [ObservableProperty] string? iconError;

    /// <summary>The server's state changed: the icon may have been set or removed.</summary>
    internal void FollowServer() => OnPropertyChanged(nameof(HasIcon));

    /// <summary>The page closed: the server no longer reaches this section.</summary>
    internal void Detach() => server.PropertyChanged -= OnServerPropertyChanged;

    void OnServerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(ServerViewModel.IconPng)) return;
        OnPropertyChanged(nameof(IconPng));
        OnPropertyChanged(nameof(HasIcon));
    }

    /// <summary>Takes an already checked and scaled PNG (IconImport) or the reason it was refused.</summary>
    internal Task UploadIconAsync(byte[]? png, string? error)
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

    /// <summary>Package 98: the icon spins until the server's new settings carry it.</summary>
    void SendIcon(string? pngBase64)
    {
        if (!IconUpload.IsRunning) server.SendConfirmed(new SetServerIcon(pngBase64), message => message is ServerSettingsChanged, IconUpload, notify: false);
    }
}
