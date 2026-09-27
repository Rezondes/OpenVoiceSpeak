using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NAudio.CoreAudioApi;
using OVS.Client.Audio;
using OVS.Client.ViewModels;

namespace OVS.Client.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    MainViewModel Vm => (MainViewModel)DataContext!;

    async void OnConnectClick(object? sender, RoutedEventArgs e)
    {
        if (await SimpleDialogs.Connect(this, Vm.Settings) is { } choice) await Vm.ConnectAsync(choice);
    }

    async void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        var vm = new SettingsViewModel(Vm.Settings, AudioDevices.List(DataFlow.Capture), AudioDevices.List(DataFlow.Render), Vm.Keys);
        void OnLevel(float db) => Dispatcher.UIThread.Post(() => vm.InputLevelDb = db);
        Vm.Audio.InputLevel += OnLevel;
        try
        {
            if (await new SettingsDialog { DataContext = vm }.ShowDialog<bool>(this))
                Vm.ApplySettings(vm.ToSettings(Vm.Settings));
        }
        finally
        {
            Vm.Audio.InputLevel -= OnLevel;
        }
    }

    async void OnAdminClick(object? sender, RoutedEventArgs e)
    {
        if (Vm.Server is not { } server) return;
        var vm = new AdminViewModel(server);
        await vm.RequestListsAsync();
        try
        {
            await new AdminDialog { DataContext = vm }.ShowDialog(this);
        }
        finally
        {
            vm.Detach();
        }
    }

    void OnChannelDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ChannelViewModel channel) channel.JoinCommand.Execute(null);
    }
}
