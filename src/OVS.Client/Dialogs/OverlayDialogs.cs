using OVS.Client.Controls;
using OVS.Client.Views;
using OVS.Shared.Protocol.Messages.Groups;

namespace OVS.Client.Dialogs;

/// <summary>The dialogs of the main window's overlay, as the view models ask for them (App, and the UI tests).</summary>
internal static class OverlayDialogs
{
    /// <param name="groups">Package 93: the server's groups for the channel dialog's group lock.</param>
    /// <param name="overlay">The overlay the dialogs show in.</param>
    internal static DialogCallbacks Create(OverlayHost overlay, Func<IReadOnlyList<GroupInfo>?> groups) => new()
    {
        AskPassword = name => ServerPasswordDialog.ShowAsync(overlay, name),
        BookmarkList = CreateBookmarkListDialogs(overlay),
        PageFlow = CreatePageFlowDialogs(overlay),
        Server = CreateServerDialogs(overlay, groups),
        Admin = CreateAdminDialogs(overlay),
        OfferUpdate = offer => UpdateOfferDialog.ShowAsync(overlay, offer),
        ConnectionDetails = stats => ConnectionDetailsDialog.ShowAsync(overlay, stats), // Package 133
    };

    /// <summary>The dialogs of the sidebar's saved servers.</summary>
    internal static BookmarkListDialogs CreateBookmarkListDialogs(OverlayHost overlay) => new()
    {
        EditBookmark = bookmark => BookmarkDialog.ShowAsync(overlay, bookmark),
        Confirm = text => ConfirmDialog.ShowAsync(overlay, text),
    };

    /// <summary>The dialogs of the page flow and the settings page's key binding editor.</summary>
    internal static PageFlowDialogs CreatePageFlowDialogs(OverlayHost overlay) => new()
    {
        AskLeave = () => LeaveDialog.ShowAsync(overlay), // Package 114
        EditKeyBinding = (binding, capture) => KeyBindingDialog.ShowAsync(overlay, binding, capture),
    };

    /// <param name="groups">Package 93: the server's groups for the channel dialog's group lock.</param>
    /// <param name="overlay">The overlay the dialogs show in.</param>
    internal static ServerDialogs CreateServerDialogs(OverlayHost overlay, Func<IReadOnlyList<GroupInfo>?> groups) => new()
    {
        EditChannel = (current, mode, submit) => ChannelDialogView.ShowAsync(overlay, current, mode, groups(), submit),
        PickChannel = (title, channels, submit) => ChannelPickerDialog.ShowAsync(overlay, title, channels, submit),
        AskText = (title, prompt, submit) => TextDialog.ShowAsync(overlay, title, prompt, submit),
        Ban = (nickname, ipKnown, submit) => BanDialog.ShowAsync(overlay, nickname, ipKnown, submit),
        Confirm = text => ConfirmDialog.ShowAsync(overlay, text),
        AskChannelPassword = name => ChannelPasswordDialog.ShowAsync(overlay, name), // Package 94
    };

    internal static AdminDialogs CreateAdminDialogs(OverlayHost overlay) => new()
    {
        AskLeave = () => LeaveDialog.ShowAsync(overlay), // Package 114
        UsersAndBans = CreateUsersAndBansDialogs(overlay),
        BackupCard = CreateBackupCardDialogs(overlay),
    };

    internal static UsersAndBansDialogs CreateUsersAndBansDialogs(OverlayHost overlay) => new()
    {
        Ban = (nickname, ipKnown, submit) => BanDialog.ShowAsync(overlay, nickname, ipKnown, submit),
        ConfirmDeleteUser = nickname => DeleteUserDialog.ShowAsync(overlay, nickname),
    };

    internal static BackupCardDialogs CreateBackupCardDialogs(OverlayHost overlay) => new()
    {
        ConfirmBackupDownload = () => BackupDownloadDialog.ShowAsync(overlay),
        Confirm = text => ConfirmDialog.ShowAsync(overlay, text),
        ConfirmRestore = title => RestoreDialog.ShowAsync(overlay, title),
    };
}
