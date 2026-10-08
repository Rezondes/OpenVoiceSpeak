using OVS.Client.Controls;
using OVS.Client.Localization;
using static OVS.Client.Dialogs.DialogControls;

namespace OVS.Client.Dialogs;

/// <summary>Package 89 (A101): before every backup download, since the certificate stays in the archive for a server move.</summary>
internal static class BackupDownloadDialog
{
    internal static async Task<bool> ShowAsync(OverlayHost overlay) =>
        await DialogFrame.ShowAsync(overlay, Strings.Backup_DownloadTitle, "LockClosed", CreateText(Strings.Backup_DownloadWarning), () => "ok",
            new(Strings.Ui_Download)) is not null;
}
