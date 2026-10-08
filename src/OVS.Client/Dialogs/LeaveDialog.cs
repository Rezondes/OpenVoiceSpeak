using OVS.Client.Controls;
using OVS.Client.Localization;
using static OVS.Client.Dialogs.DialogControls;

namespace OVS.Client.Dialogs;

/// <summary>Package 114: leaving would lose unsaved changes. Enter saves, Esc keeps editing.</summary>
internal static class LeaveDialog
{
    internal static async Task<LeaveChoice> ShowAsync(OverlayHost overlay) =>
        await DialogFrame.ShowAsync(overlay, Strings.Dlg_UnsavedTitle, "Warning", CreateText(Strings.Dlg_UnsavedText), () => "save",
            new(Strings.Dlg_SaveAndLeave, CancelText: Strings.Dlg_KeepEditing, ThirdAnswer: (Strings.Dlg_LeaveWithoutSaving, "discard"))) switch
        {
            "save" => LeaveChoice.Save,
            "discard" => LeaveChoice.Discard,
            _ => LeaveChoice.Stay,
        };
}
