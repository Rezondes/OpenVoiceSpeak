using Avalonia.Controls;
using OVS.Client.ViewModels;
using OVS.Shared.Protocol;

namespace OVS.Client.Views;

/// <summary>Package 54: one dialog for creating and editing a channel, shown in the overlay of the main window (A20).</summary>
public partial class ChannelDialog : UserControl
{
    public ChannelDialog() => InitializeComponent();

    /// <param name="groups">Package 93: the server's groups for the group lock.</param>
    /// <param name="submit">Package 98: sends the channel; the card waits for the server's confirmation.</param>
    public static Task<ChannelEdit?> ShowAsync(OverlayHost overlay, ChannelEdit current, ChannelDialogMode mode, IReadOnlyList<GroupInfo>? groups = null,
        Func<ChannelEdit, Pending>? submit = null)
    {
        var vm = new ChannelDialogViewModel(current, mode, groups);
        return SimpleDialogs.Show(overlay, vm.Title, "Speaker", new ChannelDialog { DataContext = vm }, vm.Result, vm.ConfirmText, submit: submit);
    }
}
