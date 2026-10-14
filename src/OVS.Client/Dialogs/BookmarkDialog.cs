using Avalonia;
using Avalonia.Controls;
using OVS.Client.Converters;
using OVS.Client.Controls;
using OVS.Client.Localization;
using OVS.Client.Settings;
using OVS.Shared.Protocol;
using static OVS.Client.Dialogs.DialogControls;

namespace OVS.Client.Dialogs;

/// <summary>Edits a bookmark: name, address, nickname and the saved password.</summary>
internal static class BookmarkDialog
{
    internal static Task<BookmarkEdit?> ShowAsync(OverlayHost overlay, Bookmark bookmark)
    {
        var name = new TextBox { Text = bookmark.Name };
        var host = new TextBox { Text = bookmark.Host, PasswordChar = PrivacyConverters.MaskOf(overlay) }; // Package 160
        var port = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = bookmark.Port, FormatString = "0", Increment = 1, Width = 130 };
        var nickname = new TextBox { Text = bookmark.Nickname };
        var password = new TextBox { PasswordChar = '•', Text = bookmark.SavedPassword() ?? "" };
        var save = CreateSavePasswordBox(password, bookmark.HasSavedPassword);
        var address = new DockPanel { Children = { DockAt(CreateField("Port", port), Dock.Right), CreateField(Strings.Dlg_Address, host) } };
        ((Control)address.Children[0]).Margin = new Thickness(12, 0, 0, 0);
        var body = CreateStack(CreateField("Name", name), address, CreateField("Nickname", nickname),
            CreateField(Strings.Dlg_ServerPassword, password, Strings.Dlg_PasswordEditHint), save);
        return DialogFrame.ShowAsync(overlay, Strings.Dlg_EditBookmark, "Edit", body, () =>
            string.IsNullOrWhiteSpace(name.Text) || string.IsNullOrWhiteSpace(host.Text)
            || TextRules.NormalizeName(nickname.Text, ProtocolLimits.MaxNicknameLength) is null // Package 83: the server's nickname rule
                ? null
                : new BookmarkEdit(name.Text.Trim(), host.Text.Trim(), (int)(port.Value ?? ProtocolDefaults.Port), nickname.Text.Trim(), password.Text, save.IsChecked == true),
            new(Strings.Dlg_Save));
    }
}
