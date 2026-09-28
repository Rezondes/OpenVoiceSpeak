using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
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
    enum Kind { Normal, Danger }

    static async Task<T?> Show<T>(OverlayHost host, string title, string icon, Control body, Func<T?> accept,
        string okText = "OK", bool okIsDefault = true, Kind kind = Kind.Normal) where T : class
    {
        T? result = null;
        void Accept()
        {
            result = accept();
            if (result is not null) host.Close();
        }

        var ok = new Button { Content = okText, MinWidth = 96, HorizontalContentAlignment = HorizontalAlignment.Center };
        ok.Classes.Add(kind == Kind.Danger ? "danger" : "accent");
        var cancel = new Button { Content = "Abbrechen", MinWidth = 96, HorizontalContentAlignment = HorizontalAlignment.Center };
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
        return Show(overlay, title, title.StartsWith("Admin-Token") ? "Key" : "Edit", Field(prompt, box), () => box.Text ?? "");
    }

    public static Task<ChannelEdit?> EditChannel(OverlayHost overlay, string title, ChannelEdit current, ChannelDialogMode mode)
    {
        var nameBox = new TextBox { Text = current.Name };
        var descriptionBox = new TextBox { Text = current.Description, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 76 };
        var muted = new CheckBox { Content = "Stummer Channel: niemand wird gehört", IsChecked = current.IsMuted };
        var maxUsers = new NumericUpDown
        {
            Minimum = 0, Maximum = Shared.Protocol.ProtocolInfo.MaxChannelUsers, Increment = 1, FormatString = "0",
            Value = current.MaxUsers, Width = 140, HorizontalAlignment = HorizontalAlignment.Left,
            IsEnabled = mode != ChannelDialogMode.EditDefault,
        };
        var body = Stack(Field("Name", nameBox), Field("Beschreibung", descriptionBox, "Optional, erscheint als Tooltip und im Kopf des Channels."));
        if (mode != ChannelDialogMode.Create)
        {
            body.Children.Add(Field("Optionen", muted, "Auch Link-PTT aus diesem Channel wird nicht übertragen. Sprache aus verlinkten Channels ist hörbar."));
            body.Children.Add(Field("Maximale Nutzer (0 = unbegrenzt)", maxUsers, mode == ChannelDialogMode.EditDefault
                ? "Der Standard-Channel bleibt unbegrenzt: dort landet jeder beim Verbinden."
                : "Wer drin ist, bleibt auch bei einem kleineren Limit. Das Recht \"Volle Channel betreten\" umgeht es."));
        }
        return Show(overlay, title, "Speaker", body,
            () => string.IsNullOrWhiteSpace(nameBox.Text) ? null
                : new ChannelEdit(nameBox.Text.Trim(), descriptionBox.Text ?? "", muted.IsChecked == true, (int)(maxUsers.Value ?? 0)),
            "Speichern");
    }

    public static Task<ChannelViewModel?> PickChannel(OverlayHost overlay, string title, IReadOnlyList<ChannelViewModel> channels)
    {
        if (channels.Count == 0) return Show<ChannelViewModel>(overlay, title, "Speaker", Text("Kein passender Channel vorhanden.", "muted"), () => null);
        var list = new ListBox { ItemsSource = channels.Select(c => c.Name).ToList(), MaxHeight = 320, SelectedIndex = 0, CornerRadius = new CornerRadius(8) };
        return Show(overlay, title, "Speaker", list, () => list.SelectedIndex >= 0 ? channels[list.SelectedIndex] : null, "Auswählen");
    }

    public static Task<BanChoice?> Ban(OverlayHost overlay, string nickname)
    {
        var durations = BanChoice.Durations;
        var reason = new TextBox();
        var duration = new ComboBox { ItemsSource = durations.Select(d => d.Label).ToList(), SelectedIndex = 1, HorizontalAlignment = HorizontalAlignment.Stretch };
        var includeIp = new CheckBox { Content = "Auch die IP-Adresse sperren" };
        return Show(overlay, $"{nickname} bannen", "Prohibited", Stack(Field("Grund", reason), Field("Dauer", duration), includeIp),
            () => new BanChoice(reason.Text ?? "", durations[Math.Max(0, duration.SelectedIndex)].Minutes, includeIp.IsChecked == true),
            "Bannen", kind: Kind.Danger);
    }

    /// <summary>Only used for destructive actions, hence the red button.</summary>
    static CheckBox SavePasswordBox(TextBox password, bool isChecked)
    {
        var box = new CheckBox { Content = "Passwort speichern (verschlüsselt für deinen Windows-Benutzer)", IsChecked = isChecked };
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
        var body = Stack(Text("Der Server verlangt ein Passwort, oder das gespeicherte stimmt nicht mehr.", "muted"), Field("Serverpasswort", password), save);
        return Show(overlay, $"Passwort für {serverName}", "LockClosed", body,
            () => string.IsNullOrEmpty(password.Text) ? null : new PasswordAnswer(password.Text, save.IsChecked == true), "Verbinden");
    }

    public static Task<BookmarkEdit?> EditBookmark(OverlayHost overlay, Bookmark bookmark)
    {
        var name = new TextBox { Text = bookmark.Name };
        var host = new TextBox { Text = bookmark.Host };
        var port = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = bookmark.Port, FormatString = "0", Increment = 1, Width = 130 };
        var nickname = new TextBox { Text = bookmark.Nickname };
        var password = new TextBox { PasswordChar = '•', Text = bookmark.SavedPassword() ?? "" };
        var save = SavePasswordBox(password, bookmark.HasSavedPassword);
        var address = new DockPanel { Children = { Dock(Field("Port", port), Avalonia.Controls.Dock.Right), Field("Adresse", host) } };
        ((Control)address.Children[0]).Margin = new Thickness(12, 0, 0, 0);
        var body = Stack(Field("Name", name), address, Field("Nickname", nickname),
            Field("Serverpasswort", password, "Leer lassen oder den Haken entfernen, um kein Passwort zu speichern."), save);
        return Show(overlay, "Lesezeichen bearbeiten", "Edit", body, () =>
            string.IsNullOrWhiteSpace(name.Text) || string.IsNullOrWhiteSpace(host.Text) || string.IsNullOrWhiteSpace(nickname.Text)
                ? null
                : new BookmarkEdit(name.Text.Trim(), host.Text.Trim(), (int)(port.Value ?? 7000), nickname.Text.Trim(), password.Text, save.IsChecked == true),
            "Speichern");
    }

    public static async Task<bool> Confirm(OverlayHost overlay, string text) =>
        await Show(overlay, "Bestätigen", "Delete", Text(text), () => "ok", "Ja, löschen", kind: Kind.Danger) is not null;

    public static async Task<bool> Tofu(OverlayHost overlay, TofuPrompt prompt)
    {
        var fingerprint = string.Join(" ", Enumerable.Range(0, prompt.Fingerprint.Length / 8).Select(i => prompt.Fingerprint.Substring(i * 8, 8)));
        bool mismatch = prompt.Result == TofuResult.Mismatch;
        var text = mismatch
            ? $"Das Zertifikat von {prompt.Host}:{prompt.Port} hat sich geändert. Das passiert, wenn der Server neu aufgesetzt wurde, " +
              "kann aber auch ein Angriff sein. Frag im Zweifel den Serverbetreiber nach dem Fingerprint aus dem Serverlog."
            : $"Erste Verbindung zu {prompt.Host}:{prompt.Port}. Vergleiche den Fingerprint mit der Zeile 'Zertifikat-Fingerprint' im Serverlog.";
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
        return await Show(overlay, "Serverzertifikat prüfen", mismatch ? "Warning" : "LockClosed", Stack(message, Field("Fingerprint", code)), () => "ok",
            mismatch ? "Trotzdem vertrauen" : "Vertrauen", prompt.AcceptIsDefault, mismatch ? Kind.Danger : Kind.Normal) is not null;
    }

    public static Task<ConnectChoice?> Connect(OverlayHost overlay, ClientSettings settings, Bookmark? preselect = null)
    {
        var host = new TextBox { Watermark = "z. B. voice.example.org" };
        var port = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = 7000, FormatString = "0", Increment = 1, Width = 130 };
        var nickname = new TextBox();
        var password = new TextBox { PasswordChar = '•', RevealPassword = false };
        var reveal = new ToggleButton { Content = Icon("Eye", "muted"), Width = 36, Margin = new Thickness(6, 0, 0, 0) };
        ToolTip.SetTip(reveal, "Passwort anzeigen");
        reveal.IsCheckedChanged += (_, _) => password.RevealPassword = reveal.IsChecked == true;
        var save = new CheckBox { Content = "Als Lesezeichen speichern", IsChecked = true };
        var savePassword = new CheckBox { Content = "Passwort speichern (verschlüsselt für deinen Windows-Benutzer)", IsChecked = false };
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
            PlaceholderText = "Gespeicherten Server wählen",
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

        var address = new DockPanel { Children = { Dock(Field("Port", port), Avalonia.Controls.Dock.Right), Field("Adresse", host) } };
        ((Control)address.Children[0]).Margin = new Thickness(12, 0, 0, 0);
        var passwordRow = new DockPanel { Children = { Dock(reveal, Avalonia.Controls.Dock.Right), password } };

        UpdateSavePassword();
        var body = Stack(address, Field("Nickname", nickname), Field("Serverpasswort", passwordRow, "Nur nötig, wenn der Server eines hat."), save, savePassword, error);
        if (settings.Bookmarks.Count > 0) body.Children.Insert(0, Field("Lesezeichen", bookmarks));

        return Show(overlay, "Mit Server verbinden", "PlugConnected", body, () =>
            {
                string? problem = string.IsNullOrWhiteSpace(host.Text) ? "Bitte eine Adresse eingeben."
                    : string.IsNullOrWhiteSpace(nickname.Text) ? "Bitte einen Nickname eingeben."
                    : null;
                error.Text = problem ?? "";
                error.IsVisible = problem is not null;
                return problem is not null
                    ? null
                    : new ConnectChoice(host.Text!.Trim(), (int)(port.Value ?? 7000), nickname.Text!.Trim(), password.Text, save.IsChecked == true,
                        savePassword.IsChecked == true);
            },
            "Verbinden");
    }
}
