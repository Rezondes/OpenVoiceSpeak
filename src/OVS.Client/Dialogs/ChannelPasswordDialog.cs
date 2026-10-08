using Avalonia.Automation;
using Avalonia.Controls;
using OVS.Client.Controls;
using OVS.Client.Localization;
using static OVS.Client.Dialogs.DialogControls;

namespace OVS.Client.Dialogs;

/// <summary>Package 94: asked when joining a password-locked channel; the answer is only kept in memory.</summary>
internal static class ChannelPasswordDialog
{
    internal static Task<string?> ShowAsync(OverlayHost overlay, string channelName)
    {
        var password = new TextBox { PasswordChar = '•' };
        AutomationProperties.SetName(password, Strings.Dlg_ChannelPassword);
        var body = CreateStack(CreateText(Strings.Dlg_ChannelPasswordNeeded, "muted"), CreateField(Strings.Dlg_ChannelPassword, password));
        return DialogFrame.ShowAsync(overlay, string.Format(Strings.Dlg_ChannelPasswordFor, channelName), "LockClosed", body,
            () => string.IsNullOrEmpty(password.Text) ? null : password.Text, new(Strings.Ui_Join));
    }
}
