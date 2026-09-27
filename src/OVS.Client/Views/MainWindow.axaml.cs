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

    void OnChannelDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ChannelViewModel channel) channel.JoinCommand.Execute(null);
    }
}
