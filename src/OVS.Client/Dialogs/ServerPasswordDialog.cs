using System.Globalization;
using Avalonia.Controls;
using OVS.Client.Controls;
using OVS.Client.Localization;
using static OVS.Client.Dialogs.DialogControls;

namespace OVS.Client.Dialogs;

/// <summary>Package 40: asked when a bookmark connects and the server wants a (different) password.</summary>
internal static class ServerPasswordDialog
{
    internal static Task<PasswordAnswer?> ShowAsync(OverlayHost overlay, string serverName)
    {
        var password = new TextBox { PasswordChar = '•' };
        var save = CreateSavePasswordBox(password, false);
        var body = CreateStack(CreateText(Strings.Dlg_PasswordNeeded, "muted"), CreateField(Strings.Dlg_ServerPassword, password), save);
        return DialogFrame.ShowAsync(overlay, TextFormat.Of(Strings.Dlg_PasswordFor, serverName), "LockClosed", body,
            () => string.IsNullOrEmpty(password.Text) ? null : new PasswordAnswer(password.Text, save.IsChecked == true), new(Strings.Dlg_Connect));
    }
}
