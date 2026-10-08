using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using OVS.Client.Controls;
using OVS.Client.Localization;
using OVS.Client.Settings;
using OVS.Shared.Protocol;
using static OVS.Client.Dialogs.DialogControls;

namespace OVS.Client.Dialogs;

/// <summary>Address, nickname and password of a server, filled in from a bookmark or typed; optionally saved as a bookmark.</summary>
internal static class ConnectDialog
{
    internal static Task<ConnectChoice?> ShowAsync(OverlayHost overlay, ClientSettings settings, Bookmark? preselect = null)
    {
        var host = new TextBox { Watermark = Strings.Dlg_HostExample };
        var port = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = ProtocolDefaults.Port, FormatString = "0", Increment = 1, Width = 130 };
        var nickname = new TextBox();
        var password = new TextBox { PasswordChar = '•', RevealPassword = false };
        var reveal = new ToggleButton { Content = CreateIcon("Eye", "muted"), Width = 36, Margin = new Thickness(6, 0, 0, 0) };
        ToolTip.SetTip(reveal, Strings.Dlg_ShowPassword);
        reveal.IsCheckedChanged += (_, _) => password.RevealPassword = reveal.IsChecked == true;
        var save = new CheckBox { Content = Strings.Dlg_SaveBookmark, IsChecked = true };
        var savePassword = new CheckBox { Content = Strings.Dlg_SavePassword, IsChecked = false };
        void UpdateSavePassword()
        {
            // Package 39: only with a password and only together with the bookmark.
            savePassword.IsEnabled = save.IsChecked == true && !string.IsNullOrEmpty(password.Text);
            if (!savePassword.IsEnabled) savePassword.IsChecked = false;
        }
        save.IsCheckedChanged += (_, _) => UpdateSavePassword();
        password.TextChanged += (_, _) => UpdateSavePassword();
        var error = CreateText("", "danger");
        error.IsVisible = false;
        var bookmarks = new ComboBox
        {
            ItemsSource = settings.Bookmarks.Select(b => $"{b.Name} ({b.Nickname})").ToList(),
            PlaceholderText = Strings.Dlg_ChooseBookmark,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        bookmarks.SelectionChanged += (_, _) =>
        {
            if (bookmarks.SelectedIndex < 0) return;
            var b = settings.Bookmarks[bookmarks.SelectedIndex];
            host.Text = b.Host;
            port.Value = b.Port;
            nickname.Text = b.Nickname;
            password.Text = b.SavedPassword() ?? "";
            UpdateSavePassword();
            savePassword.IsChecked = b.HasSavedPassword;
        };
        int index = preselect is null ? 0 : settings.Bookmarks.IndexOf(preselect);
        if (settings.Bookmarks.Count > 0) bookmarks.SelectedIndex = Math.Max(0, index);

        var address = new DockPanel { Children = { DockAt(CreateField("Port", port), Dock.Right), CreateField(Strings.Dlg_Address, host) } };
        ((Control)address.Children[0]).Margin = new Thickness(12, 0, 0, 0);
        var passwordRow = new DockPanel { Children = { DockAt(reveal, Dock.Right), password } };

        UpdateSavePassword();
        var body = CreateStack(address, CreateField("Nickname", nickname), CreateField(Strings.Dlg_ServerPassword, passwordRow, Strings.Dlg_PasswordOptional), save, savePassword, error);
        if (settings.Bookmarks.Count > 0) body.Children.Insert(0, CreateField(Strings.Dlg_Bookmark, bookmarks));

        return DialogFrame.ShowAsync(overlay, Strings.Dlg_ConnectTitle, "PlugConnected", body, () =>
            {
                string? problem = string.IsNullOrWhiteSpace(host.Text) ? Strings.Dlg_AddressMissing
                    : string.IsNullOrWhiteSpace(nickname.Text) ? Strings.Dlg_NicknameMissing
                    : TextRules.NormalizeName(nickname.Text, ProtocolLimits.MaxNicknameLength) is null ? Strings.Dlg_NicknameInvalid // Package 83
                    : null;
                error.Text = problem ?? "";
                error.IsVisible = problem is not null;
                return problem is not null
                    ? null
                    : new ConnectChoice(host.Text!.Trim(), (int)(port.Value ?? ProtocolDefaults.Port), nickname.Text!.Trim(), password.Text, save.IsChecked == true,
                        savePassword.IsChecked == true);
            },
            new(Strings.Dlg_Connect));
    }
}
