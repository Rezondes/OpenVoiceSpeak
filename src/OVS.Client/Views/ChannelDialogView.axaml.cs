using Avalonia.Controls;
using OVS.Client.Controls;
using OVS.Client.Dialogs;
using OVS.Client.ViewModels;
using OVS.Shared.Protocol.Messages.Groups;

namespace OVS.Client.Views;

/// <summary>Package 54: one dialog for creating and editing a channel, shown in the overlay of the main window (A20).</summary>
public partial class ChannelDialogView : UserControl
{
    public ChannelDialogView() => InitializeComponent();

    /// <param name="groups">Package 93: the server's groups for the group lock.</param>
    /// <param name="submit">Package 98: sends the channel; the card waits for the server's confirmation.</param>
    public static Task<ChannelEdit?> ShowAsync(OverlayHost overlay, ChannelEdit current, ChannelDialogMode mode, IReadOnlyList<GroupInfo>? groups = null,
        Func<ChannelEdit, PendingActionViewModel>? submit = null)
    {
        var vm = new ChannelDialogViewModel(current, mode, groups);
        return DialogFrame.ShowAsync(overlay, vm.Title, "Speaker", new ChannelDialogView { DataContext = vm }, vm.Result, new(vm.ConfirmText, Submit: submit));
    }
}
