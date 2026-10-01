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

    /// <summary>A server logo is shown; the app logo (LogoMark, a bitmap below 32 px) does not count.</summary>
    static bool ShowsImage(Visual root) => root.GetVisualDescendants().OfType<Image>()
        .Any(i => i.Source is not null && i.IsEffectivelyVisible && !i.GetVisualAncestors().OfType<LogoMark>().Any()
                  && !i.GetVisualAncestors().OfType<Border>().Any(b => b.Classes.Contains(PageMotion.PageGhostClass))); // not a passing picture (Package 104, 106)

    [AvaloniaTheory]
    [InlineData("Dark")]
    [InlineData("Light")]
    [InlineData("Dark", DisplayMode.Simplified)] // Package 102 (AC6): every page and dialog in both displays
    [InlineData("Light", DisplayMode.Simplified)]
    public void Windows_LoadAndShowTheirContent(string variant, DisplayMode display = DisplayMode.Animated)
    {
        var sink = new CollectingSink();
        Logger.Sink = sink;
        Application.Current!.RequestedThemeVariant = variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        var settings = new ClientSettings();
        settings.Bookmarks.Add(new Bookmark("voice.example.org:7000", "voice.example.org", 7000, "ich"));
        settings.Display = display;
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

    /// <summary>A frame once running transitions are over (restyling replays the 100 ms fades of the simplified look).</summary>
    static byte[] Pixels(Window window)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 300)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(1); // leaves the CPU to the timing tests running beside
        }
        using var frame = window.CaptureRenderedFrame()!;
        using var buffer = frame.Lock();
        var bytes = new byte[buffer.RowBytes * buffer.Size.Height];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
        return bytes;
    }

    static IEnumerable<Style> AllStyles(IEnumerable<Avalonia.Styling.IStyle> styles) =>
        styles.SelectMany(s => s switch
        {
            Style style => [style],
            Avalonia.Styling.Styles nested => AllStyles(nested),
            _ => [],
        });

    /// <summary>
    /// Package 99 (A114): the simplified display is today's look, pixel for pixel. Every page renders exactly as it
    /// does without the animated display's styles (<c>Styles/Motion.axaml</c>).
    /// </summary>
    [AvaloniaFact]
    public async Task Simplified_LooksLikeBefore()
    {
        var vm = new MainViewModel(dir, a => Dispatcher.UIThread.Post(a), useAudioDevices: false);
        var main = new MainWindow { DataContext = vm, Width = 1100, Height = 700 };
        main.Show();
        var settings = vm.Settings;
        settings.Display = DisplayMode.Simplified; // saved, so closing the settings page keeps it
        vm.ApplySettings(settings);
        Assert.DoesNotContain("animated", main.Classes);
        var styles = Application.Current!.Styles;
        var motion = (Avalonia.Styling.Styles)styles[^1]; // App.axaml includes Motion.axaml last
        // A115: every part of every selector list starts with the class on the window
        Assert.All(AllStyles(motion).SelectMany(style => style.Selector!.ToString()!.Split(',')), part => Assert.StartsWith("Window.animated", part.Trim()));

        void LooksLikeBefore(string page)
        {
            // spinners keep turning between two frames (the admin lists wait for a server that never answers)
            foreach (var spinner in main.GetVisualDescendants().OfType<BusySpinner>().ToList()) spinner.IsVisible = false;
            main.FocusManager?.ClearFocus(); // and a text cursor blinks
            var simplified = Pixels(main);
            Assert.True(simplified.Distinct().Count() > 16, $"{page}: the frame is blank");
            int index = styles.IndexOf(motion);
            styles.Remove(motion);
            var before = Pixels(main);
            styles.Insert(index, motion);
            Assert.True(simplified.SequenceEqual(before), page);
        }

        LooksLikeBefore("home");
        vm.Server = FakeServers.Admin();
        LooksLikeBefore("server");
        vm.OpenSettings();
        LooksLikeBefore("settings");
        vm.ClosePage();
        await vm.OpenAdminAsync();
        await Task.Delay(400); // the waiting states show after 150 ms (Package 97); they must not appear between the frames
        LooksLikeBefore("admin");
        Assert.DoesNotContain("animated", main.Classes);
        Motion.IsAnimated = true;
        main.Close();
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
        // Package 71: the user overview with search, filters and every stored value
        vm.Server.Apply(new UserList("r", [new KnownUserInfo("fp2", "anna", [WellKnownGroups.Guest], DateTimeOffset.Now.AddDays(-3),
            DateTimeOffset.Now, 4, TimeSpan.FromHours(2), "10.0.0.2", ["anni"], TimeSpan.FromMinutes(3), 7, CanBeModeratedByMe: true)]));
        main.GetVisualDescendants().OfType<TabItem>().Single(t => t.Header is "Nutzer").IsSelected = true;
        Dispatcher.UIThread.RunJobs();
        foreach (var text in new[] { "1 von 1 Nutzern", "anni", "10.0.0.2", "2 h", "3 min", "Online", "Frühere Nicknames" })
            Assert.Contains(text, Texts(main));
        Assert.Single(main.GetVisualDescendants().OfType<TextBox>(), t => AutomationProperties.GetName(t) == "Name, Fingerabdruck oder IP suchen");
        Assert.Equal(["Status", "Gruppe", "Sortierung"], main.GetVisualDescendants().OfType<AdminView>().Single()
            .GetVisualDescendants().OfType<ComboBox>().Select(AutomationProperties.GetName));
        Assert.Contains("Alle Nutzer", Texts(main)); // the combo boxes show their labels
        foreach (var text in new[] { "Bannen", "Nutzerdaten löschen" }) // Package 72: the actions of the card
            Assert.Contains(text, Texts(main));
        // Package 80: the ban overview with search, filters and every detail
        vm.Server.Apply(new BanList("r", [new BanInfo(Guid.NewGuid(), "fp3", "troll", "10.0.0.3", "Spam", "anna", null, DateTimeOffset.Now.AddDays(-1),
            "fp2", null, null, null, 2, DateTimeOffset.Now, "10.0.0.4")]));
        main.GetVisualDescendants().OfType<TabItem>().Single(t => t.Header is "Bans").IsSelected = true;
        Dispatcher.UIThread.RunJobs();
        foreach (var text in new[] { "1 von 1 Bans", "troll", "Spam", "aktiv", "10.0.0.3", "Blockierte Versuche", "Erstellt von", "anna", "Aktive Bans" })
            Assert.Contains(text, Texts(main));
        Assert.Single(main.GetVisualDescendants().OfType<TextBox>(), t => AutomationProperties.GetName(t) == "Name, IP, Grund, Moderator suchen");
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
        Dialog(o => SimpleDialogs.ConfirmDeleteUser(o, "anna"), "Alle Daten von anna löschen?"); // Package 72
        _ = SimpleDialogs.ConfirmDeleteUser(main.Overlay, "anna");
        Dispatcher.UIThread.RunJobs();
        foreach (var text in new[] { "Endgültig löschen", "Nutzerdatensatz", "Gruppen", "Statistiken", "Bans" })
            Assert.Contains(main.Overlay.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains(text) == true);
        main.Overlay.Close();
        Dispatcher.UIThread.RunJobs();
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
        // The title bar logo is 24 px and shows the pixel-hinted 24 px ICO frame, the same artwork as the taskbar
        var logo = bar.GetVisualDescendants().OfType<LogoMark>().Single();
        Assert.Equal(24, logo.Bounds.Width);
        Assert.True(logo.Bounds.Bottom <= bar.Bounds.Height);
        var small = logo.GetVisualDescendants().OfType<Image>().Single();
        Assert.True(small.IsEffectivelyVisible);
        Assert.Equal(24, ((Avalonia.Media.Imaging.Bitmap)small.Source!).PixelSize.Width);
        Assert.False(logo.GetVisualDescendants().OfType<Viewbox>().Single().IsVisible);
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

    /// <summary>Package 62: the update card covers everything below the title bar and nothing underneath takes input.</summary>
    [AvaloniaFact]
    public void UpdateProgress_CoversWholeWindow_BlocksInput()
    {
        var vm = new MainViewModel(dir, a => a(), useAudioDevices: false) { Server = FakeServers.Admin() };
        var main = new MainWindow { DataContext = vm, Width = 1000, Height = 650 };
        main.Show();
        Dispatcher.UIThread.RunJobs();
        var layer = main.FindControl<Panel>("UpdateLayer")!;
        Assert.False(layer.IsVisible);

        vm.UpdateInProgress = new UpdateProgress("290926.0lh6", UpdatePhase.Downloading, 20 * 1024 * 1024, 50 * 1024 * 1024);
        Dispatcher.UIThread.RunJobs();
        var grid = (Control)layer.Parent!;
        Assert.True(layer.IsEffectivelyVisible);
        Assert.Equal(grid.Bounds.Size, layer.Bounds.Size);
        var bar = main.FindControl<ProgressBar>("UpdateBar")!;
        Assert.Equal((40d, false), (bar.Value, bar.IsIndeterminate));
        Assert.Contains(Texts(layer), t => t?.Contains("290926.0lh6") == true);
        Assert.Contains("40", main.FindControl<TextBlock>("UpdateDetail")!.Text);
        var chat = main.GetVisualDescendants().OfType<ChatView>().Single();
        Assert.False(chat.IsEffectivelyEnabled); // no clicks, no keys
        var card = layer.Children.OfType<Border>().Single();
        Assert.Equal(layer.Bounds.Width / 2, card.Bounds.Center.X, 1);

        vm.UpdateInProgress = vm.UpdateInProgress with { Total = null };
        Dispatcher.UIThread.RunJobs();
        Assert.True(bar.IsIndeterminate);

        vm.UpdateInProgress = null;
        Dispatcher.UIThread.RunJobs();
        Assert.False(layer.IsVisible);
        Assert.True(chat.IsEffectivelyEnabled);
        main.Close();
    }

    /// <summary>Package 64: own messages on the right, everyone else on the left, notices across the whole width.</summary>
    [AvaloniaFact]
    public void Chat_OwnRight_OthersLeft_NoticesFullWidth()
    {
        var vm = new MainViewModel(dir, a => a(), useAudioDevices: false) { Server = FakeServers.Admin() };
        var main = new MainWindow { DataContext = vm, Width = 1000, Height = 650 };
        main.Show();
        vm.Chat!.AddNotice(new Notice(DateTime.Now, "Willkommen hier", NoticeKind.Welcome));
        vm.Server!.Apply(new ChatMessage(ChatTarget.Server, 2, "anna", null, null, "Hallo Gilde", DateTimeOffset.Now));
        vm.Server.Apply(new ChatMessage(ChatTarget.Server, 1, "ich", null, null, "Hallo anna", DateTimeOffset.Now));
        Dispatcher.UIThread.RunJobs();
        AssertSides(main, "Hallo Gilde", "Hallo anna");

        // the welcome card still spans the history
        var history = ChatHistory(main);
        var card = history.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("welcome"));
        Assert.Equal(history.Bounds.Width, card.Bounds.Width, 1);
        main.Close();
    }

    /// <summary>Package 64: a long own message stays inside three quarters of the history and wraps.</summary>
    [AvaloniaFact]
    public void Chat_LongMessage_BubbleCappedAndWraps()
    {
        var vm = new MainViewModel(dir, a => a(), useAudioDevices: false) { Server = FakeServers.Admin() };
        var main = new MainWindow { DataContext = vm, Width = 1000, Height = 650 };
        main.Show();
        var text = new string('x', 600);
        vm.Server!.Apply(new ChatMessage(ChatTarget.Server, 1, "ich", null, null, text, DateTimeOffset.Now));
        Dispatcher.UIThread.RunJobs();

        var history = ChatHistory(main);
        var bubble = Bubble(main, text);
        Assert.True(bubble.Bounds.Width <= history.Bounds.Width * 0.75 + 1, $"{bubble.Bounds.Width} von {history.Bounds.Width}");
        var line = MessageBlock(main, text);
        Assert.True(line.Bounds.Height > 2 * line.LineHeight, $"Höhe {line.Bounds.Height}");
        main.Close();
    }

    /// <summary>Package 64: the same sides in a private tab.</summary>
    [AvaloniaFact]
    public void Chat_PrivateTab_OwnRight()
    {
        var vm = new MainViewModel(dir, a => a(), useAudioDevices: false) { Server = FakeServers.Admin() };
        var main = new MainWindow { DataContext = vm, Width = 1000, Height = 650 };
        main.Show();
        vm.Server!.Apply(new ChatMessage(ChatTarget.Private, 2, "anna", null, 1, "psst", DateTimeOffset.Now));
        vm.Server.Apply(new ChatMessage(ChatTarget.Private, 1, "ich", null, 2, "ja?", DateTimeOffset.Now));
        vm.Chat!.Selected = vm.Chat.Tabs.Single(t => t.IsPrivate);
        Dispatcher.UIThread.RunJobs();
        AssertSides(main, "psst", "ja?");
        main.Close();
    }

    static ItemsControl ChatHistory(MainWindow main) =>
        main.GetVisualDescendants().OfType<ChatView>().Single().GetVisualDescendants().OfType<ItemsControl>().Single(i => i is not Avalonia.Controls.Primitives.TabStrip);

    static SelectableTextBlock MessageBlock(MainWindow main, string text) =>
        ChatHistory(main).GetVisualDescendants().OfType<SelectableTextBlock>().Single(t => t.Text == text && t.IsEffectivelyVisible);

    static Border Bubble(MainWindow main, string text) =>
        MessageBlock(main, text).GetVisualAncestors().OfType<Border>().First(b => b.Classes.Contains("bubble"));

    static void AssertSides(MainWindow main, string other, string own)
    {
        var history = ChatHistory(main);
        Rect InHistory(Visual v) => new(v.TranslatePoint(new Point(), history)!.Value, v.Bounds.Size);

        var ownBubble = Bubble(main, own);
        Assert.Contains("own", ownBubble.Classes);
        Assert.Equal(history.Bounds.Width, InHistory(ownBubble).Right, 1);
        var ownRow = ownBubble.GetVisualAncestors().OfType<Grid>().First();
        Assert.DoesNotContain(ownRow.GetVisualDescendants().OfType<Border>(), b => b.Classes.Contains("avatar") && b.IsEffectivelyVisible);

        var otherBubble = Bubble(main, other);
        Assert.DoesNotContain("own", otherBubble.Classes);
        var otherRow = otherBubble.GetVisualAncestors().OfType<Grid>().First();
        var avatar = otherRow.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("avatar"));
        Assert.True(avatar.IsEffectivelyVisible);
        Assert.Equal(0, InHistory(avatar).Left, 1);
        Assert.True(InHistory(otherBubble).Left > InHistory(avatar).Right);
        Assert.True(InHistory(otherBubble).Right < history.Bounds.Width * 0.75 + 1);
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
        Thickness LobbyMargin() => main.FindControl<ItemsControl>("ChannelItems")!.ContainerFromItem(vm.Server.Channels.Single(c => c.Name == "Lobby"))!.Margin;
        Assert.True(LobbyMargin().Top > 0); // Package 96: the gap opens above the lobby
        main.MouseUp(lobby + new Point(0, -8), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { FakeServers.Raid, FakeServers.Lobby }, sent.OfType<ReorderChannels>().Single().ChannelIds);
        Assert.Equal(default, LobbyMargin());
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

    /// <summary>Package 96: the server logo field lights up while a file is dragged over it, and goes back when it leaves.</summary>
    [AvaloniaFact]
    public void LogoDropZone_HighlightsOnDragOver()
    {
        var vm = new MainViewModel(dir, a => a(), useAudioDevices: false) { Server = FakeServers.Admin() };
        var main = new MainWindow { DataContext = vm, Width = 1100, Height = 700 };
        main.Show();
        _ = vm.OpenAdminAsync();
        Dispatcher.UIThread.RunJobs();
        main.GetVisualDescendants().OfType<TabItem>().Single(t => t.Header is "Server").IsSelected = true;
        Dispatcher.UIThread.RunJobs();
        var zone = main.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "IconDrop");
        var idle = zone.BorderBrush;
        void Raise(RoutedEvent<DragEventArgs> routed, IDataTransfer data) =>
            zone.RaiseEvent(new DragEventArgs(routed, data, zone, new Point(10, 10), KeyModifiers.None));
        var file = new DataTransfer();
        file.Add(DataTransferItem.Create(DataFormat.File, () => (Avalonia.Platform.Storage.IStorageItem?)null));
        var text = new DataTransfer();
        text.Add(DataTransferItem.CreateText("kein Bild"));

        Raise(DragDrop.DragOverEvent, text);
        Assert.DoesNotContain("dropHover", zone.Classes);
        Raise(DragDrop.DragOverEvent, file);
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("dropHover", zone.Classes);
        Assert.NotEqual(idle, zone.BorderBrush);
        Raise(DragDrop.DragLeaveEvent, file);
        Assert.DoesNotContain("dropHover", zone.Classes);
        Assert.Equal(idle, zone.BorderBrush);
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

    /// <summary>Package 65: the administration is a page, not a dialog, so its header button has no " ...".</summary>
    [AvaloniaTheory]
    [InlineData("de-DE", "Verwaltung")]
    [InlineData("en-US", "Administration")]
    public void Header_AdminButton_WithoutEllipsis(string culture, string expected)
    {
        var before = System.Globalization.CultureInfo.CurrentUICulture;
        System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(culture);
        try
        {
            var vm = new MainViewModel(dir, a => a(), useAudioDevices: false) { Server = FakeServers.Admin() };
            var main = new MainWindow { DataContext = vm, Width = 1100, Height = 700 };
            main.Show();
            Dispatcher.UIThread.RunJobs();
            var visible = main.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
            Assert.Contains(expected, visible);
            Assert.DoesNotContain(expected + " ...", visible);
            Assert.EndsWith(" ...", OVS.Client.Localization.Strings.Ui_RedeemTokenMenu); // it opens a dialog
            main.Close();
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = before;
        }
    }

    /// <summary>The channel's own row (not its users), found by name.</summary>
    static Border ChannelRow(Visual root, string name) => root.GetVisualDescendants().OfType<Border>()
        .Single(b => b.Classes.Contains("row") && b.DataContext is ChannelViewModel c && c.Name == name);

    static List<PathIcon> VisibleIcons(Visual row) => row.GetVisualDescendants().OfType<PathIcon>().Where(i => i.IsEffectivelyVisible).ToList();

    /// <summary>Package 66: a muted channel shows the mute icon in front instead of the speaker, and none behind its name.</summary>
    [AvaloniaFact]
    public void MutedChannel_IconReplacesSpeaker()
    {
        var vm = new MainViewModel(dir, a => a(), useAudioDevices: false) { Server = FakeServers.Admin() };
        var main = new MainWindow { DataContext = vm, Width = 1100, Height = 700 };
        main.Show();
        vm.Server!.Apply(new ChannelUpdated(new ChannelInfo(FakeServers.Lobby, "Lobby", "Start", 0, IsMuted: true)));
        Dispatcher.UIThread.RunJobs();
        var micOff = main.FindResource("Icon.MicOff");
        var speaker = main.FindResource("Icon.Speaker");

        var lobby = VisibleIcons(ChannelRow(main, "Lobby"));
        Assert.Same(micOff, lobby[0].Data);
        Assert.Contains("warning", lobby[0].Classes);
        Assert.Equal(OVS.Client.Localization.Strings.Dlg_MutedChannel, ToolTip.GetTip(lobby[0]));
        Assert.DoesNotContain(lobby, i => i.Data == speaker);
        Assert.Single(lobby, i => i.Data == micOff); // nothing behind the name
        Assert.Same(speaker, VisibleIcons(ChannelRow(main, "Raid"))[0].Data);

        vm.Server.Apply(new ChannelUpdated(new ChannelInfo(FakeServers.Lobby, "Lobby", "Start", 0)));
        Dispatcher.UIThread.RunJobs();
        lobby = VisibleIcons(ChannelRow(main, "Lobby"));
        Assert.Same(speaker, lobby[0].Data);
        Assert.DoesNotContain(lobby, i => i.Data == micOff);
        main.Close();
    }

    /// <summary>Package 67: a linked channel shows only a link icon behind its name, the partners are in its tooltip.</summary>
    [AvaloniaFact]
    public void LinkedChannel_IconWithTooltip_NoChip()
    {
        var vm = new MainViewModel(dir, a => a(), useAudioDevices: false) { Server = FakeServers.Admin() };
        var main = new MainWindow { DataContext = vm, Width = 1100, Height = 700 };
        main.Show();
        Dispatcher.UIThread.RunJobs();
        var row = ChannelRow(main, "Lobby");
        Assert.DoesNotContain(row.GetVisualDescendants().OfType<Border>(), b => b.Classes.Contains("chip"));
        var link = Assert.Single(VisibleIcons(row), i => i.Data == main.FindResource("Icon.Link"));
        Assert.Contains("link", link.Classes);
        Assert.Equal("Verlinkt mit Raid", ToolTip.GetTip(link));
        main.Close();
    }

    static Border Sidebar(MainWindow main) => main.FindControl<Border>("Sidebar")!;

    static readonly string[] SquadChannels =
        ["Infantry Squad 1", "Infantry Squad 2", "Sabotage Squad", "Logistics and Support Squad", "Artillery", "FoB"];

    /// <summary>Package 67: after connecting, every channel name fits without trimming and the sidebar is not wider than needed.</summary>
    [AvaloniaFact]
    public void Connect_SidebarFitsLongestChannel()
    {
        var vm = new MainViewModel(dir, a => a(), useAudioDevices: false);
        var main = new MainWindow { DataContext = vm, Width = 1100, Height = 700 };
        main.Show();
        var server = FakeServers.WithChannels(SquadChannels);
        // Package 93: the lock icon behind the longest name counts as well
        var longest = server.Mirror.Channels.Values.Single(c => c.Name == "Logistics and Support Squad");
        server.Apply(new ChannelUpdated(longest with { AllowedGroupIds = [WellKnownGroups.Admin] }));
        vm.Server = server;
        Dispatcher.UIThread.RunJobs();

        Assert.True(Sidebar(main).Bounds.Width >= 240);
        var slack = new List<double>();
        foreach (var name in SquadChannels)
        {
            var row = ChannelRow(main, name);
            var text = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("channelName"));
            Assert.False(text.TextLayout.TextLines.Any(l => l.HasCollapsed), $"{name} ist abgeschnitten");
            var group = (Control)text.Parent!; // name, home and link icon
            var count = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("channelCount"));
            var groupRight = group.TranslatePoint(new Point(group.Bounds.Width, 0), row)!.Value.X;
            var countLeft = count.TranslatePoint(default, row)!.Value.X;
            slack.Add(countLeft - groupRight - count.Margin.Left);
        }
        Assert.True(slack.Min() >= -0.5, $"überlappt: {slack.Min()}");
        Assert.True(slack.Min() <= 1, $"zu breit: {slack.Min()}");
        main.Close();
    }

    /// <summary>Package 67: a width the user dragged stays until the next connect, even when channels change.</summary>
    [AvaloniaFact]
    public void UserDraggedWidth_KeptUntilReconnect()
    {
        var vm = new MainViewModel(dir, a => a(), useAudioDevices: false);
        var main = new MainWindow { DataContext = vm, Width = 1100, Height = 700 };
        main.Show();
        vm.Server = FakeServers.WithChannels(SquadChannels);
        Dispatcher.UIThread.RunJobs();
        var fitted = Sidebar(main).Bounds.Width;

        // a new, longer channel widens the sidebar while the user has not dragged
        vm.Server.Apply(new ChannelAdded(new ChannelInfo(Guid.NewGuid(), "Logistics and Support Squad Number Two", "", 9)));
        Dispatcher.UIThread.RunJobs();
        var wider = Sidebar(main).Bounds.Width;
        Assert.True(wider > fitted, $"{wider} <= {fitted}");

        var splitter = main.GetVisualDescendants().OfType<GridSplitter>().Single();
        ((Grid)splitter.Parent!).ColumnDefinitions[0].Width = new GridLength(wider + 80);
        splitter.RaiseEvent(new Avalonia.Input.VectorEventArgs { RoutedEvent = Avalonia.Controls.Primitives.Thumb.DragCompletedEvent });
        Dispatcher.UIThread.RunJobs();
        vm.Server.Apply(new ChannelAdded(new ChannelInfo(Guid.NewGuid(), "Logistics and Support Squad Number Three and Four", "", 10)));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(wider + 80, Sidebar(main).Bounds.Width, 1);

        vm.Server = FakeServers.WithChannels(SquadChannels);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(fitted, Sidebar(main).Bounds.Width, 1);
        main.Close();
    }

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

    // ---- Package 98 (A113): waiting states ----

    /// <summary>
    /// The main window with the admin fake server on a manual clock, its dialogs the real ones of the overlay. The
    /// server is made on the UI thread, so the waiting marks' timers come back through the dispatcher.
    /// </summary>
    (MainWindow Main, MainViewModel Vm, ServerViewModel Server, List<Request> Sent, ManualTimeProvider Time) OpenWaiting(double width = 1000)
    {
        var vm = new MainViewModel(dir, a => a(), useAudioDevices: false);
        var main = new MainWindow { DataContext = vm, Width = width, Height = 700 };
        main.Show();
        var sent = new List<Request>();
        var time = new ManualTimeProvider();
        ServerViewModel? server = null;
        server = FakeServers.Admin(sent, time, SimpleDialogs.For(main.Overlay, () => server?.Mirror.Groups));
        vm.Server = server;
        Dispatcher.UIThread.RunJobs();
        return (main, vm, server, sent, time);
    }

    static void Advance(ManualTimeProvider time, int ms)
    {
        time.Advance(TimeSpan.FromMilliseconds(ms));
        Dispatcher.UIThread.RunJobs();
    }

    static Button OverlayButton(MainWindow main, string text) =>
        main.Overlay.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == text
            || b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == text && t.IsEffectivelyVisible));

    static void Click(Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    static bool SpinnerIn(Visual root) => root.GetVisualDescendants().OfType<BusySpinner>().Any(s => s.IsEffectivelyVisible);

    /// <summary>Package 98 (AC3): a dialog that sends keeps its card open with a busy button until the server confirms; errors show inside.</summary>
    [AvaloniaFact]
    public void SendingDialogs_StayOpenBusyUntilConfirmed()
    {
        var (main, _, server, sent, time) = OpenWaiting();

        // create a channel: busy, then closed by the server's ChannelAdded
        _ = server.NewChannelCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        main.Overlay.GetVisualDescendants().OfType<TextBox>().First().Text = "Neu";
        Click(OverlayButton(main, "Anlegen"));
        Assert.Single(sent.OfType<CreateChannel>());
        Assert.True(main.Overlay.IsOpen);
        Advance(time, 150);
        var create = OverlayButton(main, "Anlegen");
        Assert.False(create.IsEnabled);
        Assert.True(SpinnerIn(create));
        Click(create); // a second click meanwhile sends nothing
        Assert.Single(sent.OfType<CreateChannel>());
        server.Apply(new ChannelAdded(new ChannelInfo(Guid.NewGuid(), "Neu", "", 2)));
        Dispatcher.UIThread.RunJobs();
        Assert.False(main.Overlay.IsOpen);

        // ban: refused, the card stays with the reason and the button works again
        var anna = server.Channels.SelectMany(c => c.Users).Single(u => u.Nickname == "anna");
        _ = anna.BanCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        Click(OverlayButton(main, "Bannen"));
        Advance(time, 150);
        Assert.True(SpinnerIn(main.Overlay));
        server.Apply(new Error(sent.OfType<Ban>().Single().RequestId, Codes.PermissionDenied));
        Dispatcher.UIThread.RunJobs();
        Assert.True(main.Overlay.IsOpen);
        Assert.Contains(main.Overlay.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == ErrorTexts.For(Codes.PermissionDenied) && t.IsEffectivelyVisible);
        Assert.True(OverlayButton(main, "Bannen").IsEnabled);
        Assert.False(SpinnerIn(main.Overlay));

        // no answer at all: after 10 s the card says so and stays
        Click(OverlayButton(main, "Bannen"));
        Advance(time, 10_000);
        Assert.True(main.Overlay.IsOpen);
        Assert.Contains(main.Overlay.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == OVS.Client.Localization.Strings.Pending_NoAnswer && t.IsEffectivelyVisible);
        main.Overlay.Close();
        Dispatcher.UIThread.RunJobs();

        // redeem a token: closed once the own user comes back
        _ = server.RedeemTokenCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        main.Overlay.GetVisualDescendants().OfType<TextBox>().Single().Text = "geheim";
        Click(OverlayButton(main, "OK"));
        Advance(time, 150);
        Assert.True(main.Overlay.IsOpen);
        server.Apply(new UserUpdated(server.Mirror.Self!));
        Dispatcher.UIThread.RunJobs();
        Assert.False(main.Overlay.IsOpen);
        main.Close();
    }

    /// <summary>
    /// Package 98 (AC6): every action that waits shows something within 400 ms. A fake server answers each after 1 s;
    /// the busy indicator must be there after 400 ms and gone after the answer.
    /// </summary>
    [AvaloniaFact]
    public void EveryWaitingAction_VisibleWithin400ms()
    {
        var (main, vm, server, sent, time) = OpenWaiting();
        var checkedActions = new List<string>();
        Request Last<T>() where T : Request => sent.OfType<T>().Last();
        ChannelViewModel Channel(string name) => server.Channels.Single(c => c.Name == name);
        UserViewModel User(string name) => server.Channels.SelectMany(c => c.Users).Single(u => u.Nickname == name);
        UserInfo Info(string name) => server.Mirror.Users.Values.Single(u => u.Nickname == name);
        bool Clock() => main.GetVisualDescendants().OfType<PathIcon>().Any(i => ToolTip.GetTip(i) as string == OVS.Client.Localization.Strings.Chat_Sending && i.IsEffectivelyVisible);
        bool Busy() => SpinnerIn(main) || Clock();

        void Waits(string name, Action start, Func<IEnumerable<Message>> answer)
        {
            start();
            Dispatcher.UIThread.RunJobs();
            Advance(time, 400);
            Assert.True(Busy(), $"{name}: nothing shows after 400 ms");
            Advance(time, 600);
            foreach (var message in answer()) server.Apply(message);
            Dispatcher.UIThread.RunJobs();
            Assert.False(Busy(), $"{name}: still busy after the answer: " + string.Join(", ", main.GetVisualDescendants().OfType<BusySpinner>()
                .Where(s => s.IsEffectivelyVisible).Select(s => s.GetVisualAncestors().OfType<Control>().FirstOrDefault(c => c.Name is not null)?.Name ?? "?")));
            checkedActions.Add(name);
        }
        void Dialog(string ok, Action<Visual>? fill = null)
        {
            Dispatcher.UIThread.RunJobs();
            fill?.Invoke(main.Overlay);
            Click(OverlayButton(main, ok));
        }

        // ---- the channel tree and the chat ----
        Waits("join", () => _ = Channel("Raid").JoinCommand.ExecuteAsync(null), () => [new UserUpdated(Info("ich") with { ChannelId = FakeServers.Raid })]);
        Waits("chat", () =>
        {
            vm.Chat!.Draft = "Hallo";
            _ = vm.Chat.SendCommand.ExecuteAsync(null);
        }, () => [new ChatMessage(ChatTarget.Server, 1, "ich", null, null, "Hallo", DateTimeOffset.Now)]);
        Waits("server mute", () => _ = User("anna").ToggleServerMuteCommand.ExecuteAsync(null), () => [new UserUpdated(Info("anna") with { ServerMuted = !Info("anna").ServerMuted })]);
        Waits("reorder", () => _ = server.MoveChannelAsync(Channel("Raid"), Channel("Lobby"), after: false),
            () => [new ChannelUpdated(new ChannelInfo(FakeServers.Raid, "Raid", "", 0)), new ChannelUpdated(new ChannelInfo(FakeServers.Lobby, "Lobby", "Start", 1))]);
        var created = Guid.NewGuid();
        Waits("create channel", () =>
        {
            _ = server.NewChannelCommand.ExecuteAsync(null);
            Dialog("Anlegen", o => o.GetVisualDescendants().OfType<TextBox>().First().Text = "Neu");
        }, () => [new ChannelAdded(new ChannelInfo(created, "Neu", "", 2))]);
        Waits("edit channel", () =>
        {
            _ = Channel("Neu").EditCommand.ExecuteAsync(null);
            Dialog("Speichern");
        }, () => [new ChannelUpdated(new ChannelInfo(created, "Neu", "", 2))]);
        Waits("unlink", () =>
        {
            _ = Channel("Lobby").UnlinkCommand.ExecuteAsync(null);
            Dialog("Auswählen");
        }, () => [new ChannelsUnlinked(FakeServers.Lobby, FakeServers.Raid)]);
        Waits("link", () =>
        {
            _ = Channel("Lobby").LinkCommand.ExecuteAsync(null);
            Dialog("Auswählen");
        }, () => [new ChannelsLinked(FakeServers.Lobby, (Guid)((LinkChannels)Last<LinkChannels>()).B)]);
        Waits("move user", () =>
        {
            _ = User("anna").MoveCommand.ExecuteAsync(null);
            Dialog("Auswählen");
        }, () => [new UserUpdated(Info("anna") with { ChannelId = ((MoveUser)Last<MoveUser>()).ChannelId })]);
        Waits("redeem token", () =>
        {
            _ = server.RedeemTokenCommand.ExecuteAsync(null);
            Dialog("OK", o => o.GetVisualDescendants().OfType<TextBox>().Single().Text = "geheim");
        }, () => [new UserUpdated(Info("ich"))]);
        Waits("delete channel", () =>
        {
            _ = Channel("Neu").DeleteCommand.ExecuteAsync(null);
            Dialog("Ja, löschen");
        }, () => [new ChannelRemoved(created)]);
        Waits("kick", () =>
        {
            _ = User("anna").KickCommand.ExecuteAsync(null);
            Dialog("OK");
        }, () => [new UserLeft(2)]);
        server.Apply(new UserJoined(new UserInfo(3, "fp3", "bert", FakeServers.Lobby, false, false, false, Permission.None, [WellKnownGroups.Guest],
            CanBeModeratedByMe: true)));
        Waits("ban", () =>
        {
            _ = User("bert").BanCommand.ExecuteAsync(null);
            Dialog("Bannen");
        }, () => [new UserLeft(3)]);

        // ---- the administration ----
        Waits("admin lists", () =>
        {
            _ = vm.OpenAdminAsync();
            Dispatcher.UIThread.RunJobs();
            main.GetVisualDescendants().OfType<AdminView>().Single().GetVisualDescendants().OfType<TabItem>().Single(t => t.Header as string == "Nutzer").IsSelected = true;
        }, () =>
        [
            new UserList(Last<ListUsers>().RequestId, [new KnownUserInfo("fp2", "anna", [WellKnownGroups.Guest], ServerMuted: true, CanBeModeratedByMe: true)]),
            new BanList(Last<ListBans>().RequestId, [new BanInfo(Guid.NewGuid(), "fp4", "troll", null, "Spam", "ich", null)]),
            new BackupList(Last<ListBackups>().RequestId, []),
            new LogList(Last<ListLogs>().RequestId, []),
        ]);
        var admin = vm.AdminPage!;
        var page = main.GetVisualDescendants().OfType<AdminView>().Single();
        void Tab(string header)
        {
            page.GetVisualDescendants().OfType<TabItem>().Single(t => t.Header as string == header).IsSelected = true;
            Dispatcher.UIThread.RunJobs();
        }
        Tab("Gruppen");
        Waits("group save", () =>
        {
            admin.SelectedGroup = admin.Groups.Single(g => g.Name == "Gast");
            _ = admin.SaveGroupCommand.ExecuteAsync(null);
        }, () => [new GroupsChanged(server.Mirror.Groups)]);
        Waits("group order", () => _ = admin.MoveGroupAsync(admin.Groups.Single(g => g.Name == "Admin"), admin.Groups[0], after: false),
            () => [new GroupsChanged([.. Enumerable.Reverse(server.Mirror.Groups)])]);
        Tab("Nutzer");
        // the group changes asked for the user list again (the rights on others may change): answered first, also the wish
        // that came meanwhile and follows a second later
        for (var i = 0; i < 2; i++)
        {
            server.Apply(new UserList(Last<ListUsers>().RequestId, [new KnownUserInfo("fp2", "anna", [WellKnownGroups.Guest], ServerMuted: true, CanBeModeratedByMe: true)]));
            Advance(time, 1000);
        }
        Waits("user card", () => _ = admin.Users.Single().LiftMuteCommand.ExecuteAsync(null),
            () => [new UserList(Last<ListUsers>().RequestId, [new KnownUserInfo("fp2", "anna", [WellKnownGroups.Guest], CanBeModeratedByMe: true)])]);
        Tab("Bans");
        Waits("unban", () => _ = admin.Bans.Single().UnbanCommand.ExecuteAsync(null), () => [new BanList(Last<Unban>().RequestId, [])]);
        Tab("Links");
        Waits("link apply", () =>
        {
            admin.Links.Set(FakeServers.Lobby, FakeServers.Raid, false);
            _ = admin.Links.ApplyCommand.ExecuteAsync(null);
        }, () => [new ChannelsUnlinked(FakeServers.Lobby, FakeServers.Raid)]);
        Tab("Server");
        page.FindControl<Button>("NewBackupButton")!.BringIntoView();
        Waits("backup", () => _ = admin.NewBackupCommand.ExecuteAsync(null), () => [new BackupList(null, [])]);
        page.FindControl<Border>("IconDrop")!.BringIntoView();
        Waits("logo", () => _ = admin.UploadIconAsync(TestImages.Encode(64, 64), null), () => [new ServerSettingsChanged(server.Mirror.Settings)]);
        Tab("Logs");
        Waits("log search", () =>
        {
            admin.Logs.SearchText = "fehler";
            _ = admin.Logs.SearchCommand.ExecuteAsync(null);
        }, () => [new LogSearchResult(Last<SearchLogs>().RequestId, [], false, false)]);
        vm.ClosePage();

        // ---- the settings: saving while the audio devices restart, the update check ----
        var restart = new TaskCompletionSource();
        var check = new TaskCompletionSource<string>();
        vm.SettingsPage = new SettingsViewModel(vm.Settings, [], [], time: time) { ApplySaved = () => restart.Task, CheckNow = () => check.Task };
        vm.Page = Page.Settings;
        Dispatcher.UIThread.RunJobs();
        Waits("update check", () => _ = vm.SettingsPage.CheckUpdatesCommand.ExecuteAsync(null), () =>
        {
            check.SetResult("Aktuell");
            return [];
        });
        Waits("settings save", () => _ = vm.SettingsPage.SaveCommand.ExecuteAsync(null), () =>
        {
            restart.SetResult();
            return [];
        });
        Assert.Equal(24, checkedActions.Count);
        main.Close();
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
