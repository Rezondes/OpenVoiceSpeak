using Avalonia.Controls;
using OVS.Client.Controls;
using OVS.Client.Dialogs;
using OVS.Client.ViewModels;
using OVS.Shared.Protocol.Messages.Groups;

namespace OVS.Client.Views;

/// <summary>Package 54: one dialog for creating and editing a channel, shown in the overlay of the main window (A20).</summary>
internal partial class ChannelDialogView : UserControl
{
    /// <summary>The channel dialog's card.</summary>
    public ChannelDialogView() => InitializeComponent();

    /// <param name="groups">Package 93: the server's groups for the group lock.</param>
    /// <param name="submit">Package 98: sends the channel; the card waits for the server's confirmation.</param>
    /// <param name="overlay">The overlay the dialog shows in.</param>
    /// <param name="current">The channel's values to start from.</param>
    /// <param name="mode">Create, edit or edit the default channel.</param>
    public static Task<ChannelEdit?> ShowAsync(OverlayHost overlay, ChannelEdit current, ChannelDialogMode mode, IReadOnlyList<GroupInfo>? groups = null,
        Func<ChannelEdit, PendingActionViewModel>? submit = null)
    {
        var channelDialog = new ChannelDialogViewModel(current, mode, groups);
        return DialogFrame.ShowAsync(overlay, channelDialog.Title, "Speaker", new ChannelDialogView { DataContext = channelDialog }, channelDialog.Result, new(channelDialog.ConfirmText, Submit: submit));
    }
}
