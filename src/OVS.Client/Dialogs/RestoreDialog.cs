using OVS.Client.Controls;
using OVS.Client.Localization;
using static OVS.Client.Dialogs.DialogControls;

namespace OVS.Client.Dialogs;

/// <summary>Package 74: red, because everyone is disconnected and the current state goes (a safety backup stays).</summary>
internal static class RestoreDialog
{
    internal static async Task<bool> ShowAsync(OverlayHost overlay, string backupTitle) =>
        await DialogFrame.ShowAsync(overlay, string.Format(Strings.Backup_RestoreTitle, backupTitle), "ArrowSync", CreateText(Strings.Backup_ConfirmRestore), () => "ok",
            new(Strings.Ui_Restore, OkIsDefault: false, Kind: DialogKind.Danger)) is not null;
}
