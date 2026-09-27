using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using OVS.Client.Settings;
using OVS.Client.ViewModels;

namespace OVS.Client.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown);
    }

    MainViewModel Vm => (MainViewModel)DataContext!;

    /// <summary>The modal layer for all dialogs (A20: no second window).</summary>
    public OverlayHost Overlay => OverlayLayer;

    async void OnConnectClick(object? sender, RoutedEventArgs e) => await ConnectAsync(null);

    /// <summary>A bookmark opens the connect dialog prefilled, so a password can still be entered.</summary>
    async void OnBookmarkClick(object? sender, RoutedEventArgs e) => await ConnectAsync((sender as Control)?.DataContext as Bookmark);

    async Task ConnectAsync(Bookmark? preselect)
    {
        if (Vm.IsConnecting) return;
        if (await SimpleDialogs.Connect(Overlay, Vm.Settings, preselect) is { } choice) await Vm.ConnectAsync(choice);
    }

    /// <summary>Esc leaves settings (without saving) or the administration. An open dialog handles Esc itself first.</summary>
    void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.Escape || Overlay.IsOpen || Vm.IsHomePage) return;
        Vm.ClosePage();
        e.Handled = true;
    }

    void OnChannelDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ChannelViewModel channel) channel.JoinCommand.Execute(null);
    }
}
