using OVS.Client.Controls;
using OVS.Client.Views;
using OVS.Shared.Protocol.Messages.Groups;

namespace OVS.Client.Dialogs;

/// <summary>The dialogs of the main window's overlay, as the view models ask for them (App, and the UI tests).</summary>
internal static class OverlayDialogs
{
    /// <param name="groups">Package 93: the server's groups for the channel dialog's group lock.</param>
    internal static DialogCallbacks Create(OverlayHost overlay, Func<IReadOnlyList<GroupInfo>?> groups) => new()
    {
        EditChannel = (current, mode, submit) => ChannelDialogView.ShowAsync(overlay, current, mode, groups(), submit),
        PickChannel = (title, channels, submit) => ChannelPickerDialog.ShowAsync(overlay, title, channels, submit),
        AskText = (title, prompt, submit) => TextDialog.ShowAsync(overlay, title, prompt, submit),
        Ban = (nickname, ipKnown, submit) => BanDialog.ShowAsync(overlay, nickname, ipKnown, submit),
        Confirm = text => ConfirmDialog.ShowAsync(overlay, text),
        ConfirmDeleteUser = nickname => DeleteUserDialog.ShowAsync(overlay, nickname),
        ConfirmRestore = title => RestoreDialog.ShowAsync(overlay, title),
        ConfirmBackupDownload = () => BackupDownloadDialog.ShowAsync(overlay),
        AskPassword = name => ServerPasswordDialog.ShowAsync(overlay, name),
        AskChannelPassword = name => ChannelPasswordDialog.ShowAsync(overlay, name), // Package 94
        EditBookmark = bookmark => BookmarkDialog.ShowAsync(overlay, bookmark),
        EditKeyBinding = (binding, capture) => KeyBindingDialog.ShowAsync(overlay, binding, capture),
        OfferUpdate = offer => UpdateOfferDialog.ShowAsync(overlay, offer),
        AskLeave = () => LeaveDialog.ShowAsync(overlay), // Package 114
        ConnectionDetails = stats => ConnectionDetailsDialog.ShowAsync(overlay, stats), // Package 133
    };
}
