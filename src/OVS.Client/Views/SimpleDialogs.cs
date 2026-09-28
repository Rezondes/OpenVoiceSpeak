using OVS.Client.Localization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Automation;
using OVS.Client.Input;
using KeyBinding = OVS.Client.Input.KeyBinding;
using OVS.Client.Net;
using OVS.Client.Settings;
using OVS.Client.ViewModels;

namespace OVS.Client.Views;

/// <summary>
/// Small dialogs built in code, shown as a card on the main window's overlay, never as a window of their own (A20).
/// Each returns null when cancelled. All share one frame: icon and title, body, button bar.
/// </summary>
public static class SimpleDialogs
{
    internal enum Kind { Normal, Danger }

    internal static async Task<T?> Show<T>(OverlayHost host, string title, string icon, Control body, Func<T?> accept,
        string okText = "OK", bool okIsDefault = true, Kind kind = Kind.Normal, string? cancelText = null) where T : class
    {
        T? result = null;
        void Accept()
        {
            result = accept();
            if (result is not null) host.Close();
        }

        var ok = new Button { Content = okText, MinWidth = 96, HorizontalContentAlignment = HorizontalAlignment.Center };
        ok.Classes.Add(kind == Kind.Danger ? "danger" : "accent");
        var cancel = new Button { Content = cancelText ?? Strings.Dlg_Cancel, MinWidth = 96, HorizontalContentAlignment = HorizontalAlignment.Center };
        ok.Click += (_, _) => Accept();
        cancel.Click += (_, _) => host.Close();

        var badge = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(10), Child = Icon(icon, kind == Kind.Danger ? "danger" : "accent") };
        badge.Bind(Border.BackgroundProperty, badge.GetResourceObservable(kind == Kind.Danger ? "Ovs.DangerSurface" : "Ovs.AccentSurface"));
        var heading = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 400 };
        heading.Classes.Add("h2");
        heading.FontSize = 17;

        var buttonBar = new Border
        {
            Padding = new Thickness(24, 12),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, ok } },
        };
        buttonBar.Bind(Border.BackgroundProperty, buttonBar.GetResourceObservable("Ovs.Sidebar"));
        buttonBar.Bind(Border.BorderBrushProperty, buttonBar.GetResourceObservable("Ovs.Border"));

        var frame = new DockPanel
        {
            Children =
            {
                Dock(buttonBar, Avalonia.Controls.Dock.Bottom),
                new StackPanel
                {
                    Margin = new Thickness(24, 20, 24, 20),
                    Spacing = 16,
                    Children =
                    {
                        new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { badge, heading } },
                        body,
                    },
                },
            },
        };
        // The default button decides what Enter does; a risky choice (changed certificate) defaults to cancel.
        var firstInput = body.GetLogicalDescendants().Prepend(body).OfType<InputElement>()
            .FirstOrDefault(c => c is TextBox or ComboBox or ListBox or NumericUpDown);
        await host.ShowAsync(frame, okIsDefault ? Accept : host.Close, (Control?)firstInput ?? (okIsDefault ? ok : cancel));
        return result;
    }

    static T Dock<T>(T control, Dock dock) where T : Control
    {
        DockPanel.SetDock(control, dock);
        return control;
    }

    static PathIcon Icon(string key, string style)
    {
        var data = Application.Current!.FindResource("Icon." + key) as Geometry ?? throw new KeyNotFoundException($"Icon '{key}' fehlt in Styles/Icons.axaml");
        var icon = new PathIcon { Data = data, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        icon.Classes.Add(style);
        return icon;
    }

    static TextBlock Text(string text, string? style = null)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 440 };
        block.Classes.Add(style ?? "body");
        return block;
    }

    /// <summary>A visible label above its field, never a placeholder alone.</summary>
    static StackPanel Field(string label, Control input, string? hint = null)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(Text(label, "label"));
        panel.Children.Add(input);
        if (hint is not null) panel.Children.Add(Text(hint, "caption"));
        return panel;
    }

    static StackPanel Stack(params Control[] children)
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.AddRange(children);
        return panel;
    }

    public static Task<string?> AskText(OverlayHost overlay, string title, string prompt)
    {
        var box = new TextBox();
        return Show(overlay, title, title == Strings.Dialog_RedeemToken ? "Key" : "Edit", Field(prompt, box), () => box.Text ?? "");
    }

    public static Task<ChannelViewModel?> PickChannel(OverlayHost overlay, string title, IReadOnlyList<ChannelViewModel> channels)
    {
        if (channels.Count == 0) return Show<ChannelViewModel>(overlay, title, "Speaker", Text(Strings.Dlg_NoChannel, "muted"), () => null);
        var list = new ListBox { ItemsSource = channels.Select(c => c.Name).ToList(), MaxHeight = 320, SelectedIndex = 0, CornerRadius = new CornerRadius(8) };
        return Show(overlay, title, "Speaker", list, () => list.SelectedIndex >= 0 ? channels[list.SelectedIndex] : null, Strings.Dlg_Select);
    }

    public static Task<BanChoice?> Ban(OverlayHost overlay, string nickname)
    {
        var durations = BanChoice.Durations;
        var reason = new TextBox();
        var duration = new ComboBox { ItemsSource = durations.Select(d => d.Label).ToList(), SelectedIndex = 1, HorizontalAlignment = HorizontalAlignment.Stretch };
        var includeIp = new CheckBox { Content = Strings.Dlg_BanIp };
        return Show(overlay, string.Format(Strings.Dlg_BanTitle, nickname), "Prohibited", Stack(Field(Strings.Dlg_Reason, reason), Field(Strings.Dlg_Duration, duration), includeIp),
            () => new BanChoice(reason.Text ?? "", durations[Math.Max(0, duration.SelectedIndex)].Minutes, includeIp.IsChecked == true),
            Strings.Dlg_Ban, kind: Kind.Danger);
    }

    /// <summary>Only used for destructive actions, hence the red button.</summary>
    static CheckBox SavePasswordBox(TextBox password, bool isChecked)
    {
        var box = new CheckBox { Content = Strings.Dlg_SavePassword, IsChecked = isChecked };
        void Update()
        {
            box.IsEnabled = !string.IsNullOrEmpty(password.Text);
            if (!box.IsEnabled) box.IsChecked = false;
        }
        password.TextChanged += (_, _) => Update();
        Update();
        return box;
    }

    /// <summary>Package 40: asked when a bookmark connects and the server wants a (different) password.</summary>
    public static Task<PasswordAnswer?> AskPassword(OverlayHost overlay, string serverName)
    {
        var password = new TextBox { PasswordChar = '•' };
        var save = SavePasswordBox(password, false);
        var body = Stack(Text(Strings.Dlg_PasswordNeeded, "muted"), Field(Strings.Dlg_ServerPassword, password), save);
        return Show(overlay, string.Format(Strings.Dlg_PasswordFor, serverName), "LockClosed", body,
            () => string.IsNullOrEmpty(password.Text) ? null : new PasswordAnswer(password.Text, save.IsChecked == true), Strings.Dlg_Connect);
    }

    public static Task<BookmarkEdit?> EditBookmark(OverlayHost overlay, Bookmark bookmark)
    {
        var name = new TextBox { Text = bookmark.Name };
        var host = new TextBox { Text = bookmark.Host };
        var port = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = bookmark.Port, FormatString = "0", Increment = 1, Width = 130 };
        var nickname = new TextBox { Text = bookmark.Nickname };
        var password = new TextBox { PasswordChar = '•', Text = bookmark.SavedPassword() ?? "" };
        var save = SavePasswordBox(password, bookmark.HasSavedPassword);
        var address = new DockPanel { Children = { Dock(Field("Port", port), Avalonia.Controls.Dock.Right), Field(Strings.Dlg_Address, host) } };
        ((Control)address.Children[0]).Margin = new Thickness(12, 0, 0, 0);
        var body = Stack(Field("Name", name), address, Field("Nickname", nickname),
            Field(Strings.Dlg_ServerPassword, password, Strings.Dlg_PasswordEditHint), save);
        return Show(overlay, Strings.Dlg_EditBookmark, "Edit", body, () =>
            string.IsNullOrWhiteSpace(name.Text) || string.IsNullOrWhiteSpace(host.Text) || string.IsNullOrWhiteSpace(nickname.Text)
                ? null
                : new BookmarkEdit(name.Text.Trim(), host.Text.Trim(), (int)(port.Value ?? 7000), nickname.Text.Trim(), password.Text, save.IsChecked == true),
            Strings.Dlg_Save);
    }

    /// <summary>Package 41: pick the action, then press the key or combination.</summary>
    public static Task<KeyBinding?> EditKeyBinding(OverlayHost overlay, KeyBinding? current, Func<KeyAction, Task<KeyChord?>> capture)
    {
        var action = new ComboBox
        {
            ItemsSource = KeyActions.All.Select(KeyActions.Label).ToList(),
            SelectedIndex = current is null ? -1 : KeyActions.All.ToList().IndexOf(current.Action),
            PlaceholderText = Strings.Dlg_ChooseAction,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        KeyChord? chord = current?.Chord is { Key: > 0 } known ? known : null; // Package 59: key 0 = only the action is chosen
        var keyText = new TextBlock { Text = chord?.Name ?? Strings.Dlg_NoKeyYet, VerticalAlignment = VerticalAlignment.Center };
        var keycap = new Border { Child = keyText, VerticalAlignment = VerticalAlignment.Center };
        keycap.Classes.Add("keycap");
        var assign = new Button { Content = Strings.Dlg_SetKeyButton, Margin = new Thickness(12, 0, 0, 0) };
        AutomationProperties.SetName(assign, Strings.Dlg_SetKey);
        var hint = Text("", "accent");
        hint.IsVisible = false;
        var error = Text("", "danger");
        error.IsVisible = false;
        assign.Click += async (_, _) =>
        {
            assign.IsEnabled = false;
            hint.Text = Strings.Dlg_PressKey;
            hint.IsVisible = true;
            try
            {
                if (await capture(action.SelectedIndex >= 0 ? KeyActions.All[action.SelectedIndex] : KeyAction.PushToTalk) is { } captured)
                {
                    chord = captured;
                    keyText.Text = captured.Name;
                }
            }
            finally
            {
                assign.IsEnabled = true;
                hint.IsVisible = false;
            }
        };
        var keyRow = new StackPanel { Orientation = Orientation.Horizontal, Children = { keycap, assign } };
        var body = Stack(Field(Strings.Dlg_Action, action), Field(Strings.Dlg_Key, keyRow, Strings.Dlg_KeyHint), hint, error);
        return Show(overlay, current is null ? Strings.Dlg_AddKeyAction : Strings.Dlg_ChangeKeyAction, "Keyboard", body, () =>
        {
            string? problem = action.SelectedIndex < 0 ? Strings.Dlg_ActionMissing : chord is null ? Strings.Dlg_KeyMissing : null;
            error.Text = problem ?? "";
            error.IsVisible = problem is not null;
            return problem is null ? new KeyBinding(KeyActions.All[action.SelectedIndex], chord!) : null;
        }, Strings.Dlg_Save);
    }

    /// <summary>Package 43: a newer release is out. "Später" asks again at the next start.</summary>
    public static async Task<bool> OfferUpdate(OverlayHost overlay, UpdateOffer offer)
    {
        var notes = new TextBlock { Text = offer.Notes.Length > 0 ? offer.Notes : Strings.Dlg_NoNotes, TextWrapping = TextWrapping.Wrap, MaxWidth = 440 };
        notes.Classes.Add("caption");
        var body = Stack(
            Text(string.Format(Strings.Dlg_UpdateQuestion, offer.Version)),
            new Border { MaxHeight = 200, Child = new ScrollViewer { Content = notes } },
            Text(Strings.Dlg_UpdateHint, "caption"));
        return await Show(overlay, Strings.Dlg_UpdateTitle, "ArrowSync", body, () => "ok", Strings.Dlg_InstallNow, cancelText: Strings.Dlg_Later) is not null;
    }

    public static async Task<bool> Confirm(OverlayHost overlay, string text) =>
        await Show(overlay, Strings.Dlg_Confirm, "Delete", Text(text), () => "ok", Strings.Dlg_YesDelete, kind: Kind.Danger) is not null;

    public static async Task<bool> Tofu(OverlayHost overlay, TofuPrompt prompt)
    {
        var fingerprint = string.Join(" ", Enumerable.Range(0, prompt.Fingerprint.Length / 8).Select(i => prompt.Fingerprint.Substring(i * 8, 8)));
        bool mismatch = prompt.Result == TofuResult.Mismatch;
        var text = mismatch
            ? string.Format(Strings.Tofu_Changed, prompt.Host, prompt.Port)
            : string.Format(Strings.Tofu_First, prompt.Host, prompt.Port);
        var code = new Border
        {
            Padding = new Thickness(12, 10),
            CornerRadius = new CornerRadius(8),
            Child = new SelectableTextBlock { Text = fingerprint, FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace"), TextWrapping = TextWrapping.Wrap, MaxWidth = 420 },
        };
        code.Bind(Border.BackgroundProperty, code.GetResourceObservable("Ovs.Surface"));
        Control message = Text(text);
        if (mismatch)
        {
            var icon = Icon("Warning", "warning");
            icon.Margin = new Thickness(0, 1, 10, 0);
            icon.VerticalAlignment = VerticalAlignment.Top;
            var banner = new Border { Child = new DockPanel { Children = { Dock(icon, Avalonia.Controls.Dock.Left), Text(text, "warning") } } };
            banner.Classes.Add("banner");
            banner.Classes.Add("warning");
            message = banner;
        }
        return await Show(overlay, Strings.Tofu_Title, mismatch ? "Warning" : "LockClosed", Stack(message, Field("Fingerprint", code)), () => "ok",
            mismatch ? Strings.Tofu_TrustAnyway : Strings.Tofu_Trust, prompt.AcceptIsDefault, mismatch ? Kind.Danger : Kind.Normal) is not null;
    }

    public static Task<ConnectChoice?> Connect(OverlayHost overlay, ClientSettings settings, Bookmark? preselect = null)
    {
        var host = new TextBox { Watermark = Strings.Dlg_HostExample };
        var port = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = 7000, FormatString = "0", Increment = 1, Width = 130 };
        var nickname = new TextBox();
        var password = new TextBox { PasswordChar = '•', RevealPassword = false };
        var reveal = new ToggleButton { Content = Icon("Eye", "muted"), Width = 36, Margin = new Thickness(6, 0, 0, 0) };
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
        var error = Text("", "danger");
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

        var address = new DockPanel { Children = { Dock(Field("Port", port), Avalonia.Controls.Dock.Right), Field(Strings.Dlg_Address, host) } };
        ((Control)address.Children[0]).Margin = new Thickness(12, 0, 0, 0);
        var passwordRow = new DockPanel { Children = { Dock(reveal, Avalonia.Controls.Dock.Right), password } };

        UpdateSavePassword();
        var body = Stack(address, Field("Nickname", nickname), Field(Strings.Dlg_ServerPassword, passwordRow, Strings.Dlg_PasswordOptional), save, savePassword, error);
        if (settings.Bookmarks.Count > 0) body.Children.Insert(0, Field(Strings.Dlg_Bookmark, bookmarks));

        return Show(overlay, Strings.Dlg_ConnectTitle, "PlugConnected", body, () =>
            {
                string? problem = string.IsNullOrWhiteSpace(host.Text) ? Strings.Dlg_AddressMissing
                    : string.IsNullOrWhiteSpace(nickname.Text) ? Strings.Dlg_NicknameMissing
                    : null;
                error.Text = problem ?? "";
                error.IsVisible = problem is not null;
                return problem is not null
                    ? null
                    : new ConnectChoice(host.Text!.Trim(), (int)(port.Value ?? 7000), nickname.Text!.Trim(), password.Text, save.IsChecked == true,
                        savePassword.IsChecked == true);
            },
            Strings.Dlg_Connect);
    }
}
