using Avalonia.Controls;
using OVS.Client.ViewModels;

namespace OVS.Client.Views;

/// <summary>Package 54: one dialog for creating and editing a channel, shown in the overlay of the main window (A20).</summary>
public partial class ChannelDialog : UserControl
{
    public ChannelDialog() => InitializeComponent();

    public static Task<ChannelEdit?> ShowAsync(OverlayHost overlay, ChannelEdit current, ChannelDialogMode mode)
    {
        var vm = new ChannelDialogViewModel(current, mode);
        return SimpleDialogs.Show(overlay, vm.Title, "Speaker", new ChannelDialog { DataContext = vm }, vm.Result, vm.ConfirmText);
    }
}
