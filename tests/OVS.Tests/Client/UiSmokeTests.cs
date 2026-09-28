using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Automation;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Logging;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OVS.Client;
using OVS.Client.Audio;
using OVS.Client.Net;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Client.Views;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Tests.Client;
using OVS.Tests.TestSupport;

[assembly: AvaloniaTestApplication(typeof(UiTestApp))]

namespace OVS.Tests.Client;

public static class UiTestApp
{
    // Skia instead of the drawing stub: server logos (PNG, JPG) must really decode and scale in the tests.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>Collects what Avalonia reports while windows load: missing resources and broken bindings do not throw.</summary>
sealed class CollectingSink : ILogSink
{
    public readonly List<string> Problems = [];
    public bool IsEnabled(LogEventLevel level, string area) => level >= LogEventLevel.Warning;
    public void Log(LogEventLevel level, string area, object? source, string messageTemplate) => Problems.Add($"{area}: {messageTemplate}");

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
    {
        // "Server.X ... Value is null" is by design while not connected: those bindings have a FallbackValue.
        if (propertyValues.LastOrDefault() is "Value is null.") return;
        Problems.Add($"{area}: {messageTemplate} [{string.Join(", ", propertyValues)}]");
    }
}

/// <summary>
/// Loads the real windows in both themes. XAML problems such as a missing icon or brush only show at runtime,
/// so this is the test that catches them.
/// </summary>
public sealed class UiSmokeTests : IDisposable
{
    readonly string dir = Directory.CreateTempSubdirectory("ovs-ui-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(dir, true);
        }
        catch (IOException)
        {
        }
    }

    static IEnumerable<string?> Texts(Visual root) => root.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text);

    static bool ShowsImage(Visual root) => root.GetVisualDescendants().OfType<Image>().Any(i => i.Source is not null && i.IsEffectivelyVisible);

    [AvaloniaTheory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void Windows_LoadAndShowTheirContent(string variant)
    {
        var sink = new CollectingSink();
        Logger.Sink = sink;
        Application.Current!.RequestedThemeVariant = variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        var settings = new ClientSettings();
        settings.Bookmarks.Add(new Bookmark("voice.example.org:7000", "voice.example.org", 7000, "ich"));
        settings.Save(dir);
        new ServerIconCache(dir).Save("voice.example.org", 7000, TestImages.Encode(64, 64)); // logo seen earlier
        var vm = new MainViewModel(dir, a => a(), useAudioDevices: false);

        var main = new MainWindow { DataContext = vm };
        main.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("voice.example.org:7000", Texts(main)); // start screen with bookmark tile
        Assert.True(ShowsImage(main)); // the tile shows the cached server logo

        ExercisePagesAndDialogs(main, vm);
        main.Close();
        Logger.Sink = null;
        Assert.True(sink.Problems.Count == 0, string.Join(Environment.NewLine, sink.Problems.Distinct()));
    }

    /// <summary>A missing icon key is neither an exception nor a log entry in Avalonia: the icon just stays empty.</summary>
    [AvaloniaFact]
    public void EveryIconInXaml_Exists()
    {
        var src = new DirectoryInfo(AppContext.BaseDirectory);
        while (src is not null && !Directory.Exists(Path.Combine(src.FullName, "src", "OVS.Client"))) src = src.Parent;
        Assert.NotNull(src);
        var keys = Directory.GetFiles(Path.Combine(src.FullName, "src", "OVS.Client"), "*.axaml", SearchOption.AllDirectories)
            .SelectMany(f => System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(f), @"StaticResource (Icon\.\w+)").Select(m => m.Groups[1].Value))
            .Distinct().ToList();
        Assert.NotEmpty(keys);
        foreach (var key in keys)
            Assert.True(Application.Current!.TryFindResource(key, out var value) && value is Geometry, $"{key} fehlt in Styles/Icons.axaml");
    }

    /// <summary>Every page and every dialog, shown inside the main window.</summary>
    static void ExercisePagesAndDialogs(MainWindow main, MainViewModel vm)
    {
        vm.Server = FakeServers.Admin();
        foreach (var kind in Enum.GetValues<NoticeKind>()) vm.AddNotice($"Meldung {kind}", kind);
        Dispatcher.UIThread.RunJobs();
        Assert.False(ShowsImage(main)); // no logo yet: the letter badge
        vm.Server.IconPng = TestImages.Encode(64, 64);
        Dispatcher.UIThread.RunJobs();
        Assert.True(ShowsImage(main)); // sidebar shows the server logo
        var texts = Texts(main).ToList();
        Assert.Contains("Raid", texts);
        Assert.Contains("anna", texts);
        Assert.Contains("Meldung Error", texts);
        Assert.Contains("Willkommensnachricht des Servers", texts);

        // Package 32: the chat replaces the activity feed, own messages are marked
        Assert.Contains("Allgemein", texts);
        vm.Server.Apply(new ChatMessage(ChatTarget.Server, 2, "anna", null, null, "Hallo Gilde", DateTimeOffset.Now));
        vm.Server.Apply(new ChatMessage(ChatTarget.Server, 1, "ich", null, null, "Hallo anna", DateTimeOffset.Now));
        Dispatcher.UIThread.RunJobs();
        texts = Texts(main).ToList();
        Assert.Contains("Hallo Gilde", texts);
        Assert.Contains("Hallo anna", texts);
        Assert.Contains("(du)", texts);
        var composer = main.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "Nachricht");
        Assert.True(composer.IsEffectivelyEnabled);
        Assert.Single(main.GetVisualDescendants().OfType<Button>(), b => AutomationProperties.GetName(b) == "Senden");
        vm.Chat!.Draft = "per Enter";
        Dispatcher.UIThread.RunJobs();
        composer.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, Source = composer });
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("", vm.Chat.Draft); // Enter sent it

        // Package 33: "Privatnachricht" in the user menu opens a closable tab
        var annaRow = main.GetVisualDescendants().OfType<Border>()
            .Single(b => b.ContextMenu is not null && b.DataContext is UserViewModel { Nickname: "anna" });
        annaRow.ContextMenu!.Open(annaRow);
        Dispatcher.UIThread.RunJobs();
        var message = annaRow.ContextMenu.Items.OfType<MenuItem>().Single(m => m.Header as string == "Privatnachricht");
        Assert.True(message.IsVisible);
        annaRow.ContextMenu.Close();
        message.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("@anna", Texts(main));
        var close = main.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "@anna schliessen");
        Assert.True(close.IsEffectivelyVisible);
        close.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain("@anna", Texts(main));

        vm.OpenSettings();
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("DARSTELLUNG", Texts(main));
        Assert.Contains("Wirkt nach einem Neustart des Clients.", Texts(main)); // Package 45: language choice
        Assert.Contains("Alle Sounds aus", Texts(main)); // Package 47
        var soundControls = main.GetVisualDescendants().OfType<Control>().Select(AutomationProperties.GetName).ToList(); // Package 48
        foreach (var label in new[] { "Mikrofon aus", "Neue Privatnachricht" })
            foreach (var name in new[] { $"{label} abspielen", $"{label}: Lautstärke", $"{label}: stumm", $"{label}: Datei wählen", $"{label}: zurücksetzen" })
                Assert.Contains(name, soundControls);
        // Package 59: without a PTT key the transmit section says so (here the profile was saved before, so it has none)
        bool hasPtt = vm.Settings.KeyBindings.Any(b => b.Action == OVS.Client.Input.KeyAction.PushToTalk);
        Assert.Equal(!hasPtt, main.GetVisualDescendants().OfType<TextBlock>()
            .Any(t => t.Text == "Push-to-Talk ist gewählt, aber keine Taste belegt. Ohne Taste hört dich niemand." && t.IsEffectivelyVisible));
        // Package 42: version in "Über"; "dev.<stamp>" locally, the release version in GitHub Actions (GITHUB_ACTIONS=true)
        Assert.Contains($"OpenVoiceSpeak {OVS.Shared.BuildInfo.Current.Version}", Texts(main));
        Assert.Contains("Nach Updates suchen", Texts(main)); // Package 43
        Assert.Contains("Tastenaktion hinzufügen", Texts(main));
        vm.ClosePage();

        _ = vm.OpenAdminAsync();
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.IsAdminPage);
        Assert.Contains("Gruppen", Texts(main));
        Assert.Equal(2, main.GetVisualDescendants().OfType<Button>().Count(b => AutomationProperties.GetName(b) is "Gruppe nach oben" or "Gruppe nach unten"));
        var linksTab = main.GetVisualDescendants().OfType<TabItem>().Single(t => t.Header is "Links");
        linksTab.IsSelected = true; // Package 38: the link matrix
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("Ausgewählte miteinander verlinken", Texts(main));
        Assert.Contains(main.GetVisualDescendants().OfType<CheckBox>(), c => AutomationProperties.GetName(c) == "Link Lobby und Raid");
        vm.ClosePage();

        void Dialog(Func<OverlayHost, Task> open, string title)
        {
            var shown = open(main.Overlay);
            Dispatcher.UIThread.RunJobs();
            Assert.True(main.Overlay.IsOpen, title);
            Assert.Contains(title, Texts(main.Overlay));
            main.Overlay.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.True(shown.IsCompleted, title);
        }
        Dialog(o => SimpleDialogs.Connect(o, vm.Settings), "Mit Server verbinden");

        // Package 39: "Passwort speichern" needs a password and the bookmark; a saved one is filled in
        vm.Settings.Bookmarks.Insert(0, new Bookmark("Gilde", "gilde.example.org", 7000, "ich", PasswordProtector.Protect("pw")));
        var connecting = SimpleDialogs.Connect(main.Overlay, vm.Settings, vm.Settings.Bookmarks[0]);
        Dispatcher.UIThread.RunJobs();
        var boxes = main.Overlay.GetVisualDescendants().OfType<CheckBox>().ToList();
        var savePassword = boxes.Single(c => c.Content is string text && text.StartsWith("Passwort speichern"));
        var saveBookmark = boxes.Single(c => c.Content is "Als Lesezeichen speichern");
        Assert.Equal((true, true), (savePassword.IsEnabled, savePassword.IsChecked == true));
        Assert.Equal("pw", main.Overlay.GetVisualDescendants().OfType<TextBox>().Single(t => t.PasswordChar == '•').Text);
        saveBookmark.IsChecked = false;
        Assert.Equal((false, false), (savePassword.IsEnabled, savePassword.IsChecked == true));
        main.Overlay.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.True(connecting.IsCompleted);
        vm.Settings.Bookmarks.RemoveAt(0);
        Dialog(o => SimpleDialogs.Tofu(o, new TofuPrompt("h", 1, new string('a', 64), TofuResult.Mismatch)), "Serverzertifikat prüfen");
        Dialog(o => SimpleDialogs.Tofu(o, new TofuPrompt("h", 1, new string('a', 64), TofuResult.Unknown)), "Serverzertifikat prüfen");
        Dialog(o => SimpleDialogs.Ban(o, "anna"), "anna bannen");
        Dialog(o => SimpleDialogs.Confirm(o, "Wirklich?"), "Bestätigen");
        Dialog(o => SimpleDialogs.OfferUpdate(o, new UpdateOffer("280926.0b2c", "deploy-bbbbbbb", "- Neu", DateTimeOffset.UtcNow,
            new Uri("https://example.org/a"), new Uri("https://example.org/b"))), "Update verfügbar");
        Dialog(o => SimpleDialogs.EditKeyBinding(o, null, _ => Task.FromResult<OVS.Client.Input.KeyChord?>(null)), "Tastenaktion hinzufügen");
        // Package 54: one dialog for creating and editing, with every option both times
        (string Title, List<string?> Buttons, string? Name, bool? Muted, decimal? Slots, bool SlotsEnabled) ChannelDialogShows(ChannelEdit current, ChannelDialogMode mode)
        {
            var shown = ChannelDialog.ShowAsync(main.Overlay, current, mode);
            Dispatcher.UIThread.RunJobs();
            var o = main.Overlay.GetVisualDescendants().ToList();
            var title = o.OfType<TextBlock>().First(t => t.Classes.Contains("h2")).Text!;
            var buttons = o.OfType<Button>().Select(b => b.Content as string).ToList();
            var name = o.OfType<TextBox>().First().Text;
            var muted = o.OfType<CheckBox>().Single(c => c.Content is "Stummer Channel: niemand wird gehört").IsChecked;
            var slots = o.OfType<NumericUpDown>().Single();
            main.Overlay.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.True(shown.IsCompleted);
            return (title, buttons, name, muted, slots.Value, slots.IsEnabled);
        }
        var create = ChannelDialogShows(new ChannelEdit("", ""), ChannelDialogMode.Create);
        Assert.Equal(("Channel anlegen", "", false, 0m, true), (create.Title, create.Name ?? "", create.Muted, create.Slots, create.SlotsEnabled));
        Assert.Contains("Anlegen", create.Buttons);
        var edit = ChannelDialogShows(new ChannelEdit("Raid", "", IsMuted: true, MaxUsers: 8), ChannelDialogMode.Edit);
        Assert.Equal(("Channel bearbeiten", "Raid", true, 8m, true), (edit.Title, edit.Name, edit.Muted, edit.Slots, edit.SlotsEnabled));
        Assert.Contains("Speichern", edit.Buttons);
        Assert.False(ChannelDialogShows(new ChannelEdit("Lobby", ""), ChannelDialogMode.EditDefault).SlotsEnabled);
        Dialog(o => SimpleDialogs.PickChannel(o, "Verschieben nach", vm.Server!.Channels), "Verschieben nach");
        Dialog(o => SimpleDialogs.AskText(o, "Admin-Token einlösen", "Token:"), "Admin-Token einlösen");
    }

    /// <summary>A20: settings, administration and all dialogs live inside the one main window.</summary>
    [AvaloniaFact]
    public void NoSecondWindow_EverOpens()
    {
        int opened = 0;
        using var counting = Window.WindowOpenedEvent.AddClassHandler(typeof(Window), (_, _) => opened++);
        var vm = new MainViewModel(dir, a => a(), useAudioDevices: false);
        var main = new MainWindow { DataContext = vm };
        main.Show();
        Dispatcher.UIThread.RunJobs();

        ExercisePagesAndDialogs(main, vm);

        main.Close();
        Assert.Equal(1, opened);
    }

    [AvaloniaFact]
    public void Overlay_EscCancels_EnterConfirms_EscClosesPage()
    {
        var vm = new MainViewModel(dir, a => a(), useAudioDevices: false);
        var main = new MainWindow { DataContext = vm };
        main.Show();
        Dispatcher.UIThread.RunJobs();

        var first = SimpleDialogs.Confirm(main.Overlay, "Weg damit?");
        Dispatcher.UIThread.RunJobs();
        main.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(first.IsCompleted);
        Assert.False(first.Result);

        var second = SimpleDialogs.Confirm(main.Overlay, "Weg damit?");
        Dispatcher.UIThread.RunJobs();
        main.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(second.IsCompleted);
        Assert.True(second.Result);

        vm.OpenSettings();
        Dispatcher.UIThread.RunJobs();
        main.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.IsHomePage);
        main.Close();
    }

    /// <summary>Package 27: own title bar with the three window buttons instead of the Windows frame.</summary>
    [AvaloniaFact]
    public void TitleBar_ButtonsChangeWindowState_ShowsTitleAndServer()
    {
        var vm = new MainViewModel(dir, a => a(), useAudioDevices: false);
        var main = new MainWindow { DataContext = vm, Title = "OpenVoiceSpeak [zweit]" };
        main.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.True(main.ExtendClientAreaToDecorationsHint);
        Assert.NotNull(main.Icon); // Package 28: window and taskbar show the logo
        var bar = main.GetVisualDescendants().OfType<TitleBar>().Single();
        Assert.Single(bar.GetVisualDescendants().OfType<LogoMark>());
        Assert.Single(main.GetVisualDescendants().OfType<LogoMark>(), l => l.Bounds.Width >= 64); // start screen
        Button ButtonNamed(string name) => bar.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == name);
        void Click(Button b)
        {
            b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
        }

        Assert.Contains("OpenVoiceSpeak [zweit]", Texts(bar));
        vm.Server = FakeServers.Admin();
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("· Gilde", Texts(bar));

        Click(ButtonNamed("Maximieren"));
        Assert.Equal(WindowState.Maximized, main.WindowState);
        Click(ButtonNamed("Wiederherstellen")); // the same button, relabelled
        Assert.Equal(WindowState.Normal, main.WindowState);
        Click(ButtonNamed("Minimieren"));
        Assert.Equal(WindowState.Minimized, main.WindowState);
        main.WindowState = WindowState.Normal;

        bool closed = false;
        main.Closed += (_, _) => closed = true;
        Click(ButtonNamed("Schliessen"));
        Assert.True(closed);
    }

    /// <summary>Package 32 AC8: new lines scroll along, unless the reader scrolled up.</summary>
    [AvaloniaFact]
    public void Chat_FollowsNewLines_UnlessScrolledUp()
    {
        var vm = new MainViewModel(dir, a => a(), useAudioDevices: false) { Server = FakeServers.Admin() };
        var main = new MainWindow { DataContext = vm, Width = 900, Height = 500 };
        main.Show();
        var scroller = main.GetVisualDescendants().OfType<ChatView>().Single().GetVisualDescendants().OfType<ScrollViewer>().First();
        void Say(int i)
        {
            vm.Server!.Apply(new ChatMessage(ChatTarget.Server, 2, "anna", null, null, $"Nachricht {i}", DateTimeOffset.Now));
            Dispatcher.UIThread.RunJobs();
        }
        bool AtEnd() => scroller.Offset.Y >= scroller.Extent.Height - scroller.Viewport.Height - 1;

        for (int i = 0; i < 40; i++) Say(i);
        Assert.True(AtEnd(), $"{scroller.Offset} {scroller.Extent} {scroller.Viewport}");
        Assert.True(AtEnd());

        scroller.Offset = new Vector(0, 0); // the reader scrolls up
        Dispatcher.UIThread.RunJobs();
        Say(40);
        Assert.Equal(0, scroller.Offset.Y);

        scroller.ScrollToEnd(); // back at the end: follows again
        Dispatcher.UIThread.RunJobs();
        Say(41);
        Assert.True(AtEnd());
        main.Close();
    }

    /// <summary>Package 36: a real mouse drag in the channel tree sends the new order, a short click does not.</summary>
    [AvaloniaFact]
    public void ChannelTree_DragRaidAboveLobby_SendsOrder()
    {
        var sent = new List<Request>();
        var vm = new MainViewModel(dir, a => a(), useAudioDevices: false) { Server = FakeServers.Admin(sent) };
        var main = new MainWindow { DataContext = vm, Width = 900, Height = 600 };
        main.Show();
        Dispatcher.UIThread.RunJobs();
        Point Center(string name)
        {
            var row = main.GetVisualDescendants().OfType<Border>()
                .Single(b => b.Classes.Contains("row") && b.DataContext is ChannelViewModel c && c.Name == name);
            return row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), main)!.Value;
        }
        var raid = Center("Raid");
        var lobby = Center("Lobby");

        main.MouseDown(raid, MouseButton.Left);
        main.MouseUp(raid, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(sent.OfType<ReorderChannels>()); // a click is no drag

        main.MouseDown(raid, MouseButton.Left);
        main.MouseMove(raid + new Point(0, -10));
        main.MouseMove(lobby + new Point(0, -8)); // upper half of the lobby row
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.Server.Channels.Single(c => c.Name == "Lobby").IsDropAbove);
        main.MouseUp(lobby + new Point(0, -8), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { FakeServers.Raid, FakeServers.Lobby }, sent.OfType<ReorderChannels>().Single().ChannelIds);
        Assert.False(vm.Server.Channels.Single(c => c.Name == "Lobby").IsDropAbove);
        main.Close();
    }

    /// <summary>Package 37: dragging a group in the administration sends the new group order.</summary>
    [AvaloniaFact]
    public void GroupList_DragAdminAboveGuest_SendsOrder()
    {
        var sent = new List<Request>();
        var vm = new MainViewModel(dir, a => a(), useAudioDevices: false) { Server = FakeServers.Admin(sent) };
        var main = new MainWindow { DataContext = vm, Width = 1000, Height = 700 };
        main.Show();
        _ = vm.OpenAdminAsync();
        Dispatcher.UIThread.RunJobs();
        Point Center(string name)
        {
            var item = main.GetVisualDescendants().OfType<ListBoxItem>().Single(i => i.DataContext is GroupEditViewModel g && g.Name == name);
            return item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), main)!.Value;
        }
        var admin = Center("Admin");
        var guest = Center("Gast");
        main.MouseDown(admin, MouseButton.Left);
        main.MouseMove(admin + new Point(0, -10));
        main.MouseMove(guest + new Point(0, -6));
        Dispatcher.UIThread.RunJobs();
        main.MouseUp(guest + new Point(0, -6), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { WellKnownGroups.Admin, WellKnownGroups.Guest }, sent.OfType<ReorderGroups>().Single().GroupIds);
        main.Close();
    }

    /// <summary>Package 40: while disconnected the sidebar lists the bookmarks, with Verbinden, Bearbeiten, Löschen.</summary>
    [AvaloniaFact]
    public void Bookmarks_InSidebar_WhenDisconnected()
    {
        var settings = new ClientSettings();
        settings.Bookmarks.Add(new Bookmark("Gilde", "gilde.example.org", 7000, "ich"));
        settings.Save(dir);
        var vm = new MainViewModel(dir, a => a(), useAudioDevices: false);
        var main = new MainWindow { DataContext = vm, Width = 1000, Height = 650 };
        main.Show();
        Dispatcher.UIThread.RunJobs();

        var button = main.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Mit Gilde verbinden");
        Assert.True(button.TranslatePoint(default, main)!.Value.X < 300); // in the sidebar, not on the start screen
        button.ContextMenu!.Open(button);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["Verbinden", "Bearbeiten ...", "Löschen ..."], button.ContextMenu.Items.OfType<MenuItem>().Select(m => m.Header as string));
        button.ContextMenu.Close();

        vm.Server = FakeServers.Admin();
        Dispatcher.UIThread.RunJobs();
        Assert.False(button.IsEffectivelyVisible); // connected: the channels take the sidebar
        Assert.Contains("Raid", Texts(main));
        main.Close();
    }

    /// <summary>Package 59: push-to-talk without a key shows a hint with a button under "Übertragung".</summary>
    [AvaloniaFact]
    public void Settings_PttHint_WithSetKeyButton()
    {
        new ClientSettings().Save(dir); // an existing profile without keys
        var vm = new MainViewModel(dir, a => a(), useAudioDevices: false);
        var main = new MainWindow { DataContext = vm, Width = 1000, Height = 2400 };
        main.Show();
        vm.OpenSettings();
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(main.GetVisualDescendants().OfType<TextBlock>(),
            t => t.Text == "Push-to-Talk ist gewählt, aber keine Taste belegt. Ohne Taste hört dich niemand." && t.IsEffectivelyVisible);
        var button = main.GetVisualDescendants().OfType<Button>().Single(b => b.Content is "Taste festlegen");
        Assert.True(button.IsEffectivelyVisible);
        vm.SettingsPage!.VoiceActivation = true;
        Dispatcher.UIThread.RunJobs();
        Assert.False(button.IsEffectivelyVisible);
        main.Close();
    }

    /// <summary>Package 61: only the large backgrounds become see-through; text, cards and dialogs stay solid.</summary>
    [AvaloniaFact]
    public void BackgroundOpacity_OnlyBackgroundsSeeThrough()
    {
        var vm = new MainViewModel(dir, a => a(), useAudioDevices: false);
        var main = new MainWindow { DataContext = vm };
        main.Show();
        var app = Application.Current!;
        double Opacity(string key, ThemeVariant variant) =>
            app.Resources.TryGetResource(key, variant, out var value) && value is ISolidColorBrush brush ? brush.Opacity : double.NaN;
        try
        {
            WindowAppearance.Apply(app, main, new BackgroundAppearance(0.5f, false));
            foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
            {
                foreach (var key in WindowAppearance.BackgroundKeys) Assert.Equal(0.5, Opacity(key, variant), 3);
                foreach (var key in new[] { "Ovs.Text", "Ovs.Surface", "Ovs.DialogBg", "Ovs.DialogBar", "Ovs.Accent" }) Assert.Equal(1, Opacity(key, variant));
            }
            Assert.Equal([WindowTransparencyLevel.Transparent], main.TransparencyLevelHint);
            WindowAppearance.Apply(app, main, new BackgroundAppearance(0.5f, true));
            Assert.Equal(WindowTransparencyLevel.AcrylicBlur, main.TransparencyLevelHint[0]);

            _ = SimpleDialogs.Confirm(main.Overlay, "?");
            Dispatcher.UIThread.RunJobs();
            var card = main.Overlay.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("dialog"));
            Assert.Equal(1, ((ISolidColorBrush)card.Background!).Opacity); // dialogs stay solid
            main.Overlay.Close();
            Dispatcher.UIThread.RunJobs();

            WindowAppearance.Apply(app, main, new BackgroundAppearance(1f, false));
            Assert.Empty(main.TransparencyLevelHint); // a normal window again
            Assert.Equal(1, Opacity("Ovs.Bg", ThemeVariant.Dark));
        }
        finally
        {
            WindowAppearance.Apply(app, main, new BackgroundAppearance(1f, false)); // the resources are shared by all tests
            main.Close();
        }
    }

    /// <summary>Package 58: transmit and keys come right after the volume, the long sound list after them.</summary>
    [AvaloniaFact]
    public void Settings_SectionOrder()
    {
        var vm = new MainViewModel(dir, a => a(), useAudioDevices: false);
        var main = new MainWindow { DataContext = vm, Width = 1000, Height = 3200 };
        main.Show();
        vm.OpenSettings();
        Dispatcher.UIThread.RunJobs();
        var page = main.GetVisualDescendants().OfType<SettingsView>().Single();
        var sections = page.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("section") && t.IsEffectivelyVisible)
            .OrderBy(t => t.TranslatePoint(default, main)!.Value.Y).Select(t => t.Text).ToList();
        Assert.Equal(["GERÄTE", "LAUTSTÄRKE", "ÜBERTRAGUNG", "TASTEN", "SOUNDS", "DARSTELLUNG", "ÜBER"], sections.Select(s => s!.ToUpperInvariant()));
        main.Close();
    }

    /// <summary>Package 53: the self test button beside the transmit mode, with the headphone hint.</summary>
    [AvaloniaFact]
    public void Settings_SelfTestButton_TogglesText_ShowsHeadphoneHint()
    {
        var vm = new MainViewModel(dir, a => a(), useAudioDevices: false);
        var main = new MainWindow { DataContext = vm, Width = 1000, Height = 2400 };
        main.Show();
        vm.OpenSettings();
        Dispatcher.UIThread.RunJobs();
        Button SelfTest() => main.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) is "Selbsttest starten" or "Selbsttest beenden");
        Assert.Contains(Texts(main), t => t?.Contains("Kopfhörer") == true);
        Assert.Equal("Selbsttest starten", AutomationProperties.GetName(SelfTest()));
        SelfTest().Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Selbsttest beenden", AutomationProperties.GetName(SelfTest()));
        Assert.Contains("Selbsttest beenden", Texts(main));
        Assert.True(vm.Audio.SelfTest);
        vm.SettingsPage!.CancelCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(vm.Audio.SelfTest);
        main.Close();
    }

    /// <summary>Package 51: the volume slider sits in the context menu of everyone but me; a changed volume shows in the tree.</summary>
    [AvaloniaFact]
    public void UserContextMenu_VolumeSlider_OnlyForOthers()
    {
        var vm = new MainViewModel(dir, a => a(), useAudioDevices: false);
        var main = new MainWindow { DataContext = vm, Width = 1000, Height = 650 };
        main.Show();
        vm.Server = FakeServers.Admin();
        Dispatcher.UIThread.RunJobs();

        Border Row(string nick) => main.GetVisualDescendants().OfType<Border>()
            .Single(b => b.ContextMenu is not null && b.DataContext is UserViewModel u && u.Nickname == nick);
        (Slider? Slider, List<string?> Headers) Menu(string nick)
        {
            var menu = Row(nick).ContextMenu!;
            menu.Open(Row(nick));
            Dispatcher.UIThread.RunJobs();
            var visible = menu.Items.OfType<MenuItem>().Where(m => m.IsVisible).ToList();
            var slider = visible.Select(m => m.Header).OfType<Control>().SelectMany(h => Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(h).Prepend(h)).OfType<Slider>().SingleOrDefault();
            var headers = visible.Select(m => m.Header as string).ToList();
            menu.Close();
            Dispatcher.UIThread.RunJobs();
            return (slider, headers);
        }

        var (slider, headers) = Menu("anna");
        Assert.NotNull(slider);
        Assert.Equal((0d, 200d, 5d, 100d), (slider.Minimum, slider.Maximum, slider.TickFrequency, slider.Value));
        Assert.True(slider.IsSnapToTickEnabled);
        Assert.Equal("Lautstärke von anna", AutomationProperties.GetName(slider));
        Assert.Contains("Auf 100 % zurücksetzen", headers);
        var (own, ownHeaders) = Menu("ich");
        Assert.Null(own);
        Assert.DoesNotContain("Auf 100 % zurücksetzen", ownHeaders);

        var anna = (UserViewModel)Row("anna").DataContext!;
        bool IconShown(string tip) => Row("anna").GetVisualDescendants().OfType<PathIcon>().Any(i => ToolTip.GetTip(i) as string == tip && i.IsEffectivelyVisible);
        Assert.False(IconShown("Lautstärke 100 %"));
        slider.Value = 150; // as if dragged: the binding carries it to the user
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(150, anna.VolumePercent);
        Assert.True(IconShown("Lautstärke 150 %"));
        anna.VolumePercent = 0;
        Dispatcher.UIThread.RunJobs();
        Assert.True(IconShown("Für dich stumm"));
        main.Close();
    }

    /// <summary>Everything a user can read or hear from a screen reader: texts, tooltips, names, headers, menus.</summary>
    static IEnumerable<string> AllUserTexts(Visual root) =>
        root.GetVisualDescendants().OfType<Control>().SelectMany(c => new[]
            {
                (c as TextBlock)?.Text, ToolTip.GetTip(c) as string, AutomationProperties.GetName(c),
                (c as ContentControl)?.Content as string, (c as Avalonia.Controls.Primitives.HeaderedContentControl)?.Header as string, (c as TextBox)?.Watermark,
            }
            .Concat(c.ContextMenu?.Items.OfType<MenuItem>().Select(m => m.Header as string) ?? []))
            .OfType<string>();

    /// <summary>Package 46: in English, no German resource text is left anywhere in the window, its pages and dialogs.</summary>
    [AvaloniaFact]
    public void Windows_English_NoGermanResourceText()
    {
        var before = System.Globalization.CultureInfo.CurrentUICulture;
        System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        try
        {
            var settings = new ClientSettings();
            settings.Bookmarks.Add(new Bookmark("Gilde", "gilde.example.org", 7000, "ich"));
            settings.Save(dir);
            var vm = new MainViewModel(dir, a => a(), useAudioDevices: false);
            var main = new MainWindow { DataContext = vm, Width = 1100, Height = 750 };
            main.Show();
            Dispatcher.UIThread.RunJobs();
            var seen = AllUserTexts(main).ToList();
            Assert.Contains("Connect ...", seen);

            vm.Server = FakeServers.Admin();
            Dispatcher.UIThread.RunJobs();
            seen.AddRange(AllUserTexts(main));
            vm.OpenSettings();
            Dispatcher.UIThread.RunJobs();
            seen.AddRange(AllUserTexts(main));
            vm.ClosePage();
            _ = vm.OpenAdminAsync();
            Dispatcher.UIThread.RunJobs();
            foreach (var tab in main.GetVisualDescendants().OfType<TabControl>().First().Items.OfType<TabItem>())
            {
                tab.IsSelected = true;
                Dispatcher.UIThread.RunJobs();
                seen.AddRange(AllUserTexts(main));
            }
            vm.ClosePage();
            foreach (var open in new Func<OverlayHost, Task>[]
            {
                o => SimpleDialogs.Connect(o, vm.Settings), o => SimpleDialogs.Ban(o, "anna"), o => SimpleDialogs.Confirm(o, "?"),
                o => ChannelDialog.ShowAsync(o, new ChannelEdit("Raid", ""), ChannelDialogMode.Edit),
                o => ChannelDialog.ShowAsync(o, new ChannelEdit("", ""), ChannelDialogMode.Create),
                o => SimpleDialogs.EditKeyBinding(o, null, _ => Task.FromResult<OVS.Client.Input.KeyChord?>(null)),
                o => SimpleDialogs.Tofu(o, new TofuPrompt("h", 1, new string('a', 64), TofuResult.Unknown)),
            })
            {
                _ = open(main.Overlay);
                Dispatcher.UIThread.RunJobs();
                seen.AddRange(AllUserTexts(main.Overlay));
                main.Overlay.Close();
                Dispatcher.UIThread.RunJobs();
            }
            main.Close();

            var german = LocalizationTests.GermanOnly();
            var leftovers = seen.Where(german.Contains).Distinct().ToList();
            Assert.True(leftovers.Count == 0, "Noch deutsch: " + string.Join(" | ", leftovers));
            Assert.Contains("Settings", seen);
            Assert.Contains("APPEARANCE", seen);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = before;
        }
    }

    [Fact]
    public void Avatar_SameNicknameSameColor_InitialUpperCase()
    {
        var brush = (ISolidColorBrush)Ui.AvatarBrush.Convert("anna", typeof(IBrush), null, System.Globalization.CultureInfo.InvariantCulture)!;
        var again = (ISolidColorBrush)Ui.AvatarBrush.Convert("Anna", typeof(IBrush), null, System.Globalization.CultureInfo.InvariantCulture)!;
        Assert.Equal(brush.Color, again.Color);
        Assert.Equal("A", Ui.Initials.Convert(" anna", typeof(string), null, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("?", Ui.Initials.Convert("", typeof(string), null, System.Globalization.CultureInfo.InvariantCulture));
    }
}
