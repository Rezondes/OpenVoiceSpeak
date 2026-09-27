using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OVS.Client.Net;
using OVS.Client.Settings;
using OVS.Client.ViewModels;

namespace OVS.Client.Views;

/// <summary>Small dialogs built in code; each returns null when cancelled.</summary>
public static class SimpleDialogs
{
    static async Task<T?> Show<T>(Window owner, string title, Control body, Func<T?> accept, string okText = "OK") where T : class
    {
        T? result = null;
        var window = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            MinWidth = 380,
        };
        var ok = new Button { Content = okText, IsDefault = true };
        var cancel = new Button { Content = "Abbrechen", IsCancel = true };
        ok.Click += (_, _) =>
        {
            result = accept();
            if (result is not null) window.Close();
        };
        cancel.Click += (_, _) => window.Close();
        window.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 10,
            Children =
            {
                body,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { ok, cancel } },
            },
        };
        await window.ShowDialog(owner);
        return result;
    }

    static TextBlock Label(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 480 };

    static StackPanel Stack(params Control[] children)
    {
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.AddRange(children);
        return panel;
    }

    public static Task<string?> AskText(Window owner, string title, string prompt)
    {
        var box = new TextBox();
        return Show(owner, title, Stack(Label(prompt), box), () => box.Text ?? "");
    }

    public static Task<ChannelEdit?> EditChannel(Window owner, string title, string name, string description)
    {
        var nameBox = new TextBox { Text = name, Watermark = "Name" };
        var descriptionBox = new TextBox { Text = description, Watermark = "Beschreibung (optional)", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 70 };
        return Show(owner, title, Stack(Label("Name"), nameBox, Label("Beschreibung"), descriptionBox),
            () => string.IsNullOrWhiteSpace(nameBox.Text) ? null : new ChannelEdit(nameBox.Text.Trim(), descriptionBox.Text ?? ""));
    }

    public static Task<ChannelViewModel?> PickChannel(Window owner, string title, IReadOnlyList<ChannelViewModel> channels)
    {
        if (channels.Count == 0) return Show<ChannelViewModel>(owner, title, Label("Kein passender Channel vorhanden."), () => null);
        var list = new ListBox { ItemsSource = channels.Select(c => c.Name).ToList(), MaxHeight = 320, SelectedIndex = 0 };
        return Show(owner, title, list, () => list.SelectedIndex >= 0 ? channels[list.SelectedIndex] : null);
    }

    public static Task<BanChoice?> Ban(Window owner, string nickname)
    {
        (string Label, int? Minutes)[] durations = [("1 Stunde", 60), ("1 Tag", 1440), ("7 Tage", 10080), ("Dauerhaft", null)];
        var reason = new TextBox { Watermark = "Grund" };
        var duration = new ComboBox { ItemsSource = durations.Select(d => d.Label).ToList(), SelectedIndex = 1, HorizontalAlignment = HorizontalAlignment.Stretch };
        var includeIp = new CheckBox { Content = "Auch die IP-Adresse sperren" };
        return Show(owner, $"{nickname} bannen", Stack(Label("Grund"), reason, Label("Dauer"), duration, includeIp),
            () => new BanChoice(reason.Text ?? "", durations[Math.Max(0, duration.SelectedIndex)].Minutes, includeIp.IsChecked == true), "Bannen");
    }

    public static async Task<bool> Confirm(Window owner, string text) =>
        await Show(owner, "Bestätigen", Label(text), () => "ok", "Ja") is not null;

    public static async Task<bool> Tofu(Window owner, TofuPrompt prompt)
    {
        var fingerprint = string.Join(" ", Enumerable.Range(0, prompt.Fingerprint.Length / 8).Select(i => prompt.Fingerprint.Substring(i * 8, 8)));
        var text = prompt.Result == TofuResult.Mismatch
            ? $"WARNUNG: Das Zertifikat von {prompt.Host}:{prompt.Port} hat sich geändert!\n\n" +
              "Das passiert, wenn der Server neu aufgesetzt wurde, kann aber auch ein Angriff sein. " +
              "Frag im Zweifel den Serverbetreiber nach dem Fingerprint aus dem Serverlog."
            : $"Erste Verbindung zu {prompt.Host}:{prompt.Port}.\n\n" +
              "Vergleiche den Fingerprint mit der Zeile 'Zertifikat-Fingerprint' im Serverlog.";
        var body = Stack(Label(text), new SelectableTextBlock { Text = fingerprint, FontFamily = new FontFamily("Consolas,monospace"), TextWrapping = TextWrapping.Wrap, MaxWidth = 480 });
        return await Show(owner, "Serverzertifikat prüfen", body, () => "ok",
            prompt.Result == TofuResult.Mismatch ? "Trotzdem vertrauen" : "Vertrauen") is not null;
    }

    public static Task<ConnectChoice?> Connect(Window owner, ClientSettings settings)
    {
        var host = new TextBox { Watermark = "z. B. voice.example.org" };
        var port = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = 7000, FormatString = "0", Increment = 1 };
        var nickname = new TextBox { Watermark = "Nickname" };
        var password = new TextBox { Watermark = "Serverpasswort (optional)", PasswordChar = '•' };
        var save = new CheckBox { Content = "Als Lesezeichen speichern", IsChecked = true };
        var bookmarks = new ComboBox
        {
            ItemsSource = settings.Bookmarks.Select(b => $"{b.Name} ({b.Nickname})").ToList(),
            PlaceholderText = "Lesezeichen",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsVisible = settings.Bookmarks.Count > 0,
        };
        bookmarks.SelectionChanged += (_, _) =>
        {
            if (bookmarks.SelectedIndex < 0) return;
            var b = settings.Bookmarks[bookmarks.SelectedIndex];
            host.Text = b.Host;
            port.Value = b.Port;
            nickname.Text = b.Nickname;
        };
        if (settings.Bookmarks.Count > 0) bookmarks.SelectedIndex = 0;

        return Show(owner, "Mit Server verbinden",
            Stack(bookmarks, Label("Adresse"), host, Label("Port"), port, Label("Nickname"), nickname, password, save),
            () => string.IsNullOrWhiteSpace(host.Text) || string.IsNullOrWhiteSpace(nickname.Text)
                ? null
                : new ConnectChoice(host.Text.Trim(), (int)(port.Value ?? 7000), nickname.Text.Trim(), password.Text, save.IsChecked == true),
            "Verbinden");
    }
}
