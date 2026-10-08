using Avalonia.Controls;
using OVS.Client.Controls;
using OVS.Client.Localization;
using OVS.Client.ViewModels;
using static OVS.Client.Dialogs.DialogControls;

namespace OVS.Client.Dialogs;

/// <summary>One line of text, such as the admin token to redeem.</summary>
internal static class TextDialog
{
    internal static Task<string?> ShowAsync(OverlayHost overlay, string title, string prompt, Func<string, PendingViewModel>? submit = null)
    {
        var box = new TextBox();
        return DialogFrame.ShowAsync(overlay, title, title == Strings.Dialog_RedeemToken ? "Key" : "Edit", CreateField(prompt, box), () => box.Text ?? "",
            new(Submit: submit));
    }
}
