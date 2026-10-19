using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using OVS.Client.Converters;
using OVS.Client.Controls;
using OVS.Client.Localization;
using OVS.Client.Net;
using OVS.Client.Settings;
using OVS.Shared.Net;
using OVS.Shared.Protocol;
using static OVS.Client.Dialogs.DialogControls;

namespace OVS.Client.Dialogs;

/// <summary>Address, nickname and password of a server, filled in from a bookmark or typed; optionally saved as a bookmark.</summary>
internal static class ConnectDialog
{
    /// <param name="link">
    /// Package 170: a server link: the bookmark of that address when there is one, otherwise host and port of the link and
    /// the nickname of the first bookmark; the link brings no nickname or password.
    /// </param>
    /// <param name="overlay">Where the dialog shows.</param>
    /// <param name="savedBookmarks">The bookmarks to choose from.</param>
    /// <param name="preselect">The bookmark filled in first.</param>
    internal static Task<ConnectChoice?> ShowAsync(OverlayHost overlay, IReadOnlyList<Bookmark> savedBookmarks, Bookmark? preselect = null, JoinLink? link = null)
    {
        if (link is not null)
            preselect = savedBookmarks.FirstOrDefault(bookmark => bookmark.Port == link.Port && JoinLink.For(bookmark.Host, bookmark.Port)?.Host.Equals(link.Host, StringComparison.OrdinalIgnoreCase) == true);
        var host = new TextBox { Watermark = Strings.Dlg_HostExample, PasswordChar = PrivacyConverters.MaskOf(overlay) }; // Package 160
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
            ItemsSource = savedBookmarks.Select(bookmark => $"{bookmark.Name} ({bookmark.Nickname})").ToList(),
            PlaceholderText = Strings.Dlg_ChooseBookmark,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        bookmarks.SelectionChanged += (_, _) =>
        {
            if (bookmarks.SelectedIndex < 0) return;
            var chosen = savedBookmarks[bookmarks.SelectedIndex];
            host.Text = chosen.Host;
            port.Value = chosen.Port;
            nickname.Text = chosen.Nickname;
            password.Text = chosen.SavedPassword() ?? "";
            UpdateSavePassword();
            savePassword.IsChecked = chosen.HasSavedPassword;
        };
        int index = 0;
        for (int position = 0; preselect is not null && position < savedBookmarks.Count; position++)
        {
            if (!savedBookmarks[position].Equals(preselect)) continue;
            index = position;
            break;
        }
        if (link is not null && preselect is null)
        {
            host.Text = link.Host;
            port.Value = link.Port;
            nickname.Text = savedBookmarks.Count > 0 ? savedBookmarks[0].Nickname : "";
        }
        else if (savedBookmarks.Count > 0) bookmarks.SelectedIndex = Math.Max(0, index);

        var address = new DockPanel { Children = { DockAt(CreateField("Port", port), Dock.Right), CreateField(Strings.Dlg_Address, host) } };
        ((Control)address.Children[0]).Margin = new Thickness(12, 0, 0, 0);
        var passwordRow = new DockPanel { Children = { DockAt(reveal, Dock.Right), password } };

        UpdateSavePassword();
        var body = CreateStack(address, CreateField("Nickname", nickname), CreateField(Strings.Dlg_ServerPassword, passwordRow, Strings.Dlg_PasswordOptional), save, savePassword, error);
        if (savedBookmarks.Count > 0) body.Children.Insert(0, CreateField(Strings.Dlg_Bookmark, bookmarks));

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
