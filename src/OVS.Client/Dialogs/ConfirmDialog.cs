using OVS.Client.Controls;
using OVS.Client.Localization;
using static OVS.Client.Dialogs.DialogControls;

namespace OVS.Client.Dialogs;

/// <summary>Only used for destructive actions, hence the red button.</summary>
internal static class ConfirmDialog
{
    internal static async Task<bool> ShowAsync(OverlayHost overlay, string text) =>
        await DialogFrame.ShowAsync(overlay, Strings.Dlg_Confirm, "Delete", CreateText(text), () => "ok", new(Strings.Dlg_YesDelete, Kind: DialogKind.Danger)) is not null;
}
