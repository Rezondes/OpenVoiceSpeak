using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Automation;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Logging;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Xml.Linq;
using OVS.Client.Animations;
using OVS.Client.Appearance;
using OVS.Client.Controls;
using OVS.Client.Converters;
using OVS.Client.Dialogs;
using OVS.Client.Localization;
using OVS.Client.Models;
using OVS.Client.Net;
using OVS.Client.Net.Updates;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Client.Views;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Chat;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.Groups;
using OVS.Shared.Protocol.Messages.Links;
using OVS.Shared.Protocol.Messages.LogAccess;
using OVS.Shared.Protocol.Messages.Moderation;
using OVS.Shared.Protocol.Messages.ServerBackups;
using OVS.Shared.Protocol.Messages.ServerInfo;
using OVS.Shared.Protocol.Messages.Users;
using OVS.Tests.Client.Localization;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Views;

/// <summary>
/// Loads the real windows in both themes. XAML problems such as a missing icon or brush only show at runtime,
/// so this is the test that catches them.
/// </summary>
[Collection(MotionCollection.Name)]
public sealed class UiSmokeTests() : TempDirectoryTest("ovs-ui-", quiet: true)
{
    static IEnumerable<string?> Texts(Visual root) => root.GetVisualDescendants().OfType<TextBlock>().Select(textBlock => textBlock.Text);

    /// <summary>A server logo is shown; the app logo (LogoMark, a bitmap below 32 px) does not count.</summary>
    static bool ShowsImage(Visual root) => root.GetVisualDescendants().OfType<Image>()
        .Any(image => image.Source is not null && image.IsEffectivelyVisible && !image.GetVisualAncestors().OfType<LogoMark>().Any()
                  && !image.GetVisualAncestors().OfType<Border>().Any(border => border.Classes.Contains(PageMotion.PageGhostClass))); // not a passing picture (Package 104, 106)

    [AvaloniaTheory]
    [InlineData("Dark")]
    [InlineData("Light")]
    [InlineData("Dark", DisplayMode.Simplified)] // Package 102 (AC6): every page and dialog in both displays
    [InlineData("Light", DisplayMode.Simplified)]
    public void Pages_BothDisplays_LoadAndShowTheirContent(string variant, DisplayMode display = DisplayMode.Animated)
    {
        var sink = new CollectingSink();
        Logger.Sink = sink;
        Application.Current!.RequestedThemeVariant = variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        var settings = new ClientSettings();
        settings.Bookmarks.Add(new Bookmark("voice.example.org:7000", "voice.example.org", 7000, "ich"));
        settings.Display = display;
        settings.Save(dir);
        new ServerIconCache(dir).Save("voice.example.org", 7000, TestImages.Encode(64, 64)); // logo seen earlier
        var viewModel = MainViewModelSetup.Create(dir, action => action());

        var main = new MainWindow { DataContext = viewModel };
        main.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("voice.example.org:7000", Texts(main)); // start screen with bookmark tile
        Assert.True(ShowsImage(main)); // the tile shows the cached server logo

        ExercisePagesAndDialogs(main, viewModel);
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
            MotionWait.Pause(); // leaves the CPU to the timing tests running beside
        }
        // one frame after the 300 ms as well: under load a single frame can outlast the loop, and the fades it started
        // would still show their first step
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame()!;
        using var buffer = frame.Lock();
        var bytes = new byte[buffer.RowBytes * buffer.Size.Height];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
        return bytes;
    }

    static IEnumerable<Style> AllStyles(IEnumerable<Avalonia.Styling.IStyle> styles) =>
        styles.SelectMany(iStyle => iStyle switch
        {
            Style style => [style],
            Avalonia.Styling.Styles nested => AllStyles(nested),
            _ => [],
        });

    static string AppXamlPath()
    {
        return Path.Combine(RepoPaths.ClientSource, "App.axaml");
    }

    /// <summary>
    /// The position of the Motion styles in <c>Application.Styles</c>: the compiled XAML keeps no source name on the
    /// included styles, so it is the position of the <c>StyleInclude</c> of <c>/Styles/Motion.axaml</c> in <c>App.axaml</c>.
    /// </summary>
    static int MotionStylesIndex(string appXaml)
    {
        XNamespace avalonia = "https://github.com/avaloniaui";
        var children = XDocument.Parse(appXaml).Root!.Element(avalonia + "Application.Styles")!.Elements().ToList();
        return children.FindIndex(xElement => xElement.Name == avalonia + "StyleInclude" && (string?)xElement.Attribute("Source") == "/Styles/Motion.axaml");
    }

    /// <summary>The Motion styles are found by the include's source, wherever App.axaml puts them.</summary>
    [Fact]
    public void MotionStyles_Included_FoundBySource()
    {
        var app = File.ReadAllText(AppXamlPath());
        Assert.Equal(2, MotionStylesIndex(app)); // FluentTheme, Controls, Motion
        const string controls = "<StyleInclude Source=\"/Styles/Controls.axaml\" />";
        const string motion = "<StyleInclude Source=\"/Styles/Motion.axaml\" />";
        Assert.Contains(controls, app, StringComparison.Ordinal);
        Assert.Contains(motion, app, StringComparison.Ordinal);
        var reordered = app.Replace(controls, "@", StringComparison.Ordinal).Replace(motion, controls, StringComparison.Ordinal).Replace("@", motion, StringComparison.Ordinal);
        Assert.Equal(1, MotionStylesIndex(reordered));
    }

    /// <summary>
    /// Package 99 (A114): the simplified display is today's look, pixel for pixel. Every page renders exactly as it
    /// does without the animated display's styles (<c>Styles/Motion.axaml</c>).
    /// </summary>
    [AvaloniaFact]
    public async Task Window_SimplifiedDisplay_LooksLikeBefore()
    {
        var viewModel = MainViewModelSetup.Create(dir, action => Dispatcher.UIThread.Post(action));
        var main = new MainWindow { DataContext = viewModel, Width = 1100, Height = 700 };
        main.Show();
        var settings = viewModel.Settings;
        settings.Display = DisplayMode.Simplified; // saved, so closing the settings page keeps it
        await viewModel.ApplySettingsAsync(settings);
        Assert.DoesNotContain("animated", main.Classes);
        var styles = Application.Current!.Styles;
        var motion = (Avalonia.Styling.Styles)styles[MotionStylesIndex(await File.ReadAllTextAsync(AppXamlPath()))];
        // A115: every part of every selector list starts with the class on the window
        Assert.All(AllStyles(motion).SelectMany(style => style.Selector!.ToString()!.Split(',')), part => Assert.StartsWith("Window.animated", part.Trim(), StringComparison.Ordinal));

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
        MainViewModelSetup.ShowServer(viewModel, FakeServers.Admin());
        LooksLikeBefore("server");
        viewModel.PageFlow.OpenSettings();
        LooksLikeBefore("settings");
        viewModel.PageFlow.CloseWithoutAsking();
        await viewModel.PageFlow.OpenAdminAsync();
        // the waiting states show 150 ms after their request (Package 97); they must not appear between the frames, so every
        // list still loading shows its state first (a fixed pause was too short for a late timer on a busy machine)
        var admin = viewModel.PageFlow.AdminPage!;
        PendingActionViewModel[] loads = [admin.UsersAndBans.UsersLoad, admin.UsersAndBans.BansLoad, admin.BackupCard.BackupsLoad, admin.Logs.FilesLoad];
        MotionWait.WaitFor(() => loads.All(pendingAction => !pendingAction.IsRunning || pendingAction.IsBusy), 5000);
        Assert.All(loads, pendingAction => Assert.True(!pendingAction.IsRunning || pendingAction.IsBusy));
        LooksLikeBefore("admin");
        Assert.DoesNotContain("animated", main.Classes);
        main.Close();
    }

    /// <summary>A missing icon key is neither an exception nor a log entry in Avalonia: the icon just stays empty.</summary>
    [AvaloniaFact]
    public void Icons_EveryKeyInXaml_Exists()
    {
        var keys = Directory.GetFiles(RepoPaths.ClientSource, "*.axaml", SearchOption.AllDirectories)
            .SelectMany(text => System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(text), @"StaticResource (Icon\.\w+)").Select(match => match.Groups[1].Value))
            .Distinct().ToList();
        Assert.NotEmpty(keys);
        foreach (var key in keys)
            Assert.True(Application.Current!.TryFindResource(key, out var value) && value is Geometry, $"{key} fehlt in Styles/Icons.axaml");
    }

    /// <summary>Every page and every dialog, shown inside the main window.</summary>
    static void ExercisePagesAndDialogs(MainWindow main, MainViewModel viewModel)
    {
        MainViewModelSetup.ShowServer(viewModel, FakeServers.Admin());
        foreach (var kind in Enum.GetValues<NoticeKind>()) viewModel.AddNotice($"Meldung {kind}", kind);
        Dispatcher.UIThread.RunJobs();
        Assert.False(ShowsImage(main)); // no logo yet: the letter badge
        viewModel.Server!.IconPng = TestImages.Encode(64, 64);
        Dispatcher.UIThread.RunJobs();
        Assert.True(ShowsImage(main)); // sidebar shows the server logo
        var texts = Texts(main).ToList();
        Assert.Contains("Raid", texts);
        Assert.Contains("anna", texts);
        Assert.Contains("Meldung Error", texts);
        Assert.Contains("Willkommensnachricht des Servers", texts);

        // Package 32: the chat replaces the activity feed, own messages are marked
        Assert.Contains("Allgemein", texts);
        viewModel.Server.Apply(new ChatMessage(ChatTarget.Server, 2, "anna", null, null, "Hallo Gilde", DateTimeOffset.Now));
        viewModel.Server.Apply(new ChatMessage(ChatTarget.Server, 1, "ich", null, null, "Hallo anna", DateTimeOffset.Now));
        Dispatcher.UIThread.RunJobs();
        texts = Texts(main).ToList();
        Assert.Contains("Hallo Gilde", texts);
        Assert.Contains("Hallo anna", texts);
        Assert.Contains("(du)", texts);
        var composer = main.GetVisualDescendants().OfType<TextBox>().Single(textBox => AutomationProperties.GetName(textBox) == "Nachricht");
        Assert.True(composer.IsEffectivelyEnabled);
        Assert.Single(main.GetVisualDescendants().OfType<Button>(), button => AutomationProperties.GetName(button) == "Senden");
        viewModel.Chat!.Draft = "per Enter";
        Dispatcher.UIThread.RunJobs();
        composer.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, Source = composer });
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("", viewModel.Chat.Draft); // Enter sent it

        // Package 33: "Privatnachricht" in the user menu opens a closable tab
        var annaRow = main.GetVisualDescendants().OfType<Border>()
            .Single(border => border.ContextMenu is not null && border.DataContext is UserViewModel { Nickname: "anna" });
        annaRow.ContextMenu!.Open(annaRow);
        Dispatcher.UIThread.RunJobs();
        var message = annaRow.ContextMenu.Items.OfType<MenuItem>().Single(menuItem => menuItem.Header as string == "Privatnachricht");
        Assert.True(message.IsVisible);
        annaRow.ContextMenu.Close();
        message.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("@anna", Texts(main));
        var close = main.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button) == "@anna schliessen");
        Assert.True(close.IsEffectivelyVisible);
        close.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain("@anna", Texts(main));

        viewModel.PageFlow.OpenSettings();
        Dispatcher.UIThread.RunJobs();
        SettingsPages.Open(main, Strings.Nav_Appearance); // Package 152: one page at a time
        Assert.Contains("Darstellung", Texts(main));
        Assert.Contains("Wirkt nach einem Neustart des Clients.", Texts(main)); // Package 45: language choice
        SettingsPages.Open(main, Strings.Nav_Sounds);
        Assert.Contains("Alle Sounds aus", Texts(main)); // Package 47
        var soundControls = main.GetVisualDescendants().OfType<Control>().Select(AutomationProperties.GetName).ToList(); // Package 48
        foreach (var label in new[] { "Mikrofon aus", "Neue Privatnachricht" })
            foreach (var name in new[] { $"{label} abspielen", $"{label}: Lautstärke", $"{label}: stumm", $"{label}: Datei wählen", $"{label}: zurücksetzen" })
                Assert.Contains(name, soundControls);
        // Package 59: without a PTT key the transmit section says so (here the profile was saved before, so it has none)
        SettingsPages.Open(main, Strings.Nav_Transmit);
        bool hasPtt = viewModel.Settings.KeyBindings.Any(keyBinding => keyBinding.Action == OVS.Client.Input.KeyAction.PushToTalk);
        Assert.Equal(!hasPtt, main.GetVisualDescendants().OfType<TextBlock>()
            .Any(textBlock => textBlock.Text == "Push-to-Talk ist gewählt, aber keine Taste belegt. Ohne Taste hört dich niemand." && textBlock.IsEffectivelyVisible));
        // Package 42: version in "Über"; "dev.<stamp>" locally, the release version in GitHub Actions (GITHUB_ACTIONS=true)
        SettingsPages.Open(main, Strings.Nav_About);
        Assert.Contains($"OpenVoiceSpeak {OVS.Shared.BuildInfo.Current.Version}", Texts(main));
        Assert.Contains("Nach Updates suchen", Texts(main)); // Package 43
        SettingsPages.Open(main, Strings.Nav_Keys);
        Assert.Contains("Tastenaktion hinzufügen", Texts(main));
        viewModel.PageFlow.CloseWithoutAsking();

        _ = viewModel.PageFlow.OpenAdminAsync();
        Dispatcher.UIThread.RunJobs();
        Assert.True(viewModel.PageFlow.IsAdminPage);
        Assert.Contains("Gruppen", Texts(main));
        Assert.Equal(2, main.GetVisualDescendants().OfType<Button>().Count(button => AutomationProperties.GetName(button) is "Gruppe nach oben" or "Gruppe nach unten"));
        var linksTab = main.GetVisualDescendants().OfType<TabItem>().Single(tabItem => tabItem.Header is "Links");
        linksTab.IsSelected = true; // Package 38: the link matrix
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("Ausgewählte miteinander verlinken", Texts(main));
        Assert.Contains(main.GetVisualDescendants().OfType<CheckBox>(), checkBox => AutomationProperties.GetName(checkBox) == "Link Lobby und Raid");
        // Package 71: the user overview with search, filters and every stored value
        viewModel.PageFlow.AdminPage!.UsersAndBans.Apply(new UserList("r", [new KnownUserInfo("fp2", "anna", [WellKnownGroups.Guest], DateTimeOffset.Now.AddDays(-3),
            DateTimeOffset.Now, 4, TimeSpan.FromHours(2), "10.0.0.2", ["anni"], TimeSpan.FromMinutes(3), 7, CanBeModeratedByMe: true)]));
        main.GetVisualDescendants().OfType<TabItem>().Single(tabItem => tabItem.Header is "Nutzer").IsSelected = true;
        Dispatcher.UIThread.RunJobs();
        foreach (var text in new[] { "1 von 1 Nutzern", "anni", "10.0.0.2", "2 h", "3 min", "Online", "Frühere Nicknames" })
            Assert.Contains(text, Texts(main));
        Assert.Single(main.GetVisualDescendants().OfType<TextBox>(), textBox => AutomationProperties.GetName(textBox) == "Name, Fingerabdruck oder IP suchen");
        Assert.Equal(["Status", "Gruppe", "Sortierung"], main.GetVisualDescendants().OfType<AdminView>().Single()
            .GetVisualDescendants().OfType<ComboBox>().Select(AutomationProperties.GetName));
        Assert.Contains("Alle Nutzer", Texts(main)); // the combo boxes show their labels
        foreach (var text in new[] { "Bannen", "Nutzerdaten löschen" }) // Package 72: the actions of the card
            Assert.Contains(text, Texts(main));
        // Package 80: the ban overview with search, filters and every detail
        viewModel.PageFlow.AdminPage!.UsersAndBans.Apply(new BanList("r", [new BanInfo(Guid.NewGuid(), "fp3", "troll", "10.0.0.3", "Spam", "anna", null, DateTimeOffset.Now.AddDays(-1),
            "fp2", null, null, null, 2, DateTimeOffset.Now, "10.0.0.4")]));
        main.GetVisualDescendants().OfType<TabItem>().Single(tabItem => tabItem.Header is "Bans").IsSelected = true;
        Dispatcher.UIThread.RunJobs();
        foreach (var text in new[] { "1 von 1 Bans", "troll", "Spam", "aktiv", "10.0.0.3", "Blockierte Versuche", "Erstellt von", "anna", "Aktive Bans" })
            Assert.Contains(text, Texts(main));
        Assert.Single(main.GetVisualDescendants().OfType<TextBox>(), textBox => AutomationProperties.GetName(textBox) == "Name, IP, Grund, Moderator suchen");
        viewModel.PageFlow.CloseWithoutAsking();

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
        Dialog(overlayHost => ConnectDialog.ShowAsync(overlayHost, viewModel.Bookmarks.Saved), "Mit Server verbinden");

        // Package 39: "Passwort speichern" needs a password and the bookmark; a saved one is filled in
        viewModel.Settings.Bookmarks.Insert(0, new Bookmark("Gilde", "gilde.example.org", 7000, "ich", PasswordProtector.Protect("pw")));
        var connecting = ConnectDialog.ShowAsync(main.Overlay, viewModel.Bookmarks.Saved, viewModel.Settings.Bookmarks[0]);
        Dispatcher.UIThread.RunJobs();
        var boxes = main.Overlay.GetVisualDescendants().OfType<CheckBox>().ToList();
        var savePassword = boxes.Single(checkBox => checkBox.Content is string text && text.StartsWith("Passwort speichern", StringComparison.Ordinal));
        var saveBookmark = boxes.Single(checkBox => checkBox.Content is "Als Lesezeichen speichern");
        Assert.Equal((true, true), (savePassword.IsEnabled, savePassword.IsChecked == true));
        Assert.Equal("pw", main.Overlay.GetVisualDescendants().OfType<TextBox>().Single(textBox => textBox.PasswordChar == '•').Text);
        saveBookmark.IsChecked = false;
        Assert.Equal((false, false), (savePassword.IsEnabled, savePassword.IsChecked == true));
        main.Overlay.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.True(connecting.IsCompleted);
        viewModel.Settings.Bookmarks.RemoveAt(0);
        Dialog(overlayHost => TofuDialog.ShowAsync(overlayHost, new TofuPrompt("h", 1, new string('a', 64), TofuResult.Mismatch)), "Serverzertifikat prüfen");
        Dialog(overlayHost => TofuDialog.ShowAsync(overlayHost, new TofuPrompt("h", 1, new string('a', 64), TofuResult.Unknown)), "Serverzertifikat prüfen");
        Dialog(overlayHost => BanDialog.ShowAsync(overlayHost, "anna"), "anna bannen");
        Dialog(overlayHost => ConfirmDialog.ShowAsync(overlayHost, "Wirklich?"), "Bestätigen");
        Dialog(overlayHost => DeleteUserDialog.ShowAsync(overlayHost, "anna"), "Alle Daten von anna löschen?"); // Package 72
        _ = DeleteUserDialog.ShowAsync(main.Overlay, "anna");
        Dispatcher.UIThread.RunJobs();
        foreach (var text in new[] { "Endgültig löschen", "Nutzerdatensatz", "Gruppen", "Statistiken", "Bans" })
            Assert.Contains(main.Overlay.GetVisualDescendants().OfType<TextBlock>(), textBlock => textBlock.Text?.Contains(text, StringComparison.Ordinal) == true);
        main.Overlay.Close();
        Dispatcher.UIThread.RunJobs();
        Dialog(overlayHost => UpdateOfferDialog.ShowAsync(overlayHost, new UpdateOffer("280926.0b2c", "deploy-bbbbbbb", "- Neu", DateTimeOffset.UtcNow,
            new Uri("https://example.org/a"), new Uri("https://example.org/b"))), "Update verfügbar");
        Dialog(overlayHost => KeyBindingDialog.ShowAsync(overlayHost, null, _ => Task.FromResult<OVS.Client.Input.KeyChord?>(null)), "Tastenaktion hinzufügen");
        // Package 54: one dialog for creating and editing, with every option both times
        ChannelDialogLook ChannelDialogShows(ChannelEdit current, ChannelDialogMode mode)
        {
            var shown = ChannelDialogView.ShowAsync(main.Overlay, current, mode);
            Dispatcher.UIThread.RunJobs();
            var visuals = main.Overlay.GetVisualDescendants().ToList();
            var title = visuals.OfType<TextBlock>().First(textBlock => textBlock.Classes.Contains("h2")).Text!;
            var buttons = visuals.OfType<Button>().Select(button => button.Content as string).ToList();
            var name = visuals.OfType<TextBox>().First().Text;
            var muted = visuals.OfType<CheckBox>().Single(checkBox => checkBox.Content is "Stummer Channel: niemand wird gehört").IsChecked;
            var numbers = visuals.OfType<NumericUpDown>().ToList();
            Assert.Equal(2, numbers.Count);
            var slots = numbers[0];
            Assert.Equal(current.Bitrate, numbers[1].Value); // Package 132: "Qualität" below the slots
            main.Overlay.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.True(shown.IsCompleted);
            return new(title, buttons, name, muted, slots.Value, slots.IsEnabled);
        }
        var create = ChannelDialogShows(new ChannelEdit("", ""), ChannelDialogMode.Create);
        Assert.Equal(("Channel anlegen", "", false, 0m, true), (create.Title, create.Name ?? "", create.Muted, create.Slots, create.SlotsEnabled));
        Assert.Contains("Anlegen", create.Buttons);
        var edit = ChannelDialogShows(new ChannelEdit("Raid", "", IsMuted: true, MaxUsers: 8, Bitrate: 96), ChannelDialogMode.Edit);
        Assert.Equal(("Channel bearbeiten", "Raid", true, 8m, true), (edit.Title, edit.Name, edit.Muted, edit.Slots, edit.SlotsEnabled));
        Assert.Contains("Speichern", edit.Buttons);
        Assert.False(ChannelDialogShows(new ChannelEdit("Lobby", ""), ChannelDialogMode.EditDefault).SlotsEnabled);
        Dialog(overlayHost => ChannelPickerDialog.ShowAsync(overlayHost, "Verschieben nach", viewModel.Server!.Tree.Channels), "Verschieben nach");
        Dialog(overlayHost => TextDialog.ShowAsync(overlayHost, "Admin-Token einlösen", "Token:"), "Admin-Token einlösen");
    }

    /// <summary>A20: settings, administration and all dialogs live inside the one main window.</summary>
    [AvaloniaFact]
    public void Pages_AllOpened_NoSecondWindow()
    {
        int opened = 0;
        using var counting = Window.WindowOpenedEvent.AddClassHandler(typeof(Window), (_, _) => opened++);
        var viewModel = MainViewModelSetup.Create(dir, action => action());
        var main = new MainWindow { DataContext = viewModel };
        main.Show();
        Dispatcher.UIThread.RunJobs();

        ExercisePagesAndDialogs(main, viewModel);

        main.Close();
        Assert.Equal(1, opened);
    }

    [AvaloniaFact]
    public void Overlay_EscOrEnter_CancelsConfirmsOrClosesPage()
    {
        var viewModel = MainViewModelSetup.Create(dir, action => action());
        var main = new MainWindow { DataContext = viewModel };
        main.Show();
        Dispatcher.UIThread.RunJobs();

        var first = ConfirmDialog.ShowAsync(main.Overlay, "Weg damit?");
        Dispatcher.UIThread.RunJobs();
        main.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(first.IsCompleted);
        Assert.False(first.Result);

        var second = ConfirmDialog.ShowAsync(main.Overlay, "Weg damit?");
        Dispatcher.UIThread.RunJobs();
        main.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(second.IsCompleted);
        Assert.True(second.Result);

        viewModel.PageFlow.OpenSettings();
        Dispatcher.UIThread.RunJobs();
        main.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(viewModel.PageFlow.IsHomePage);
        main.Close();
    }

    /// <summary>Package 27: own title bar with the three window buttons instead of the Windows frame.</summary>
    [AvaloniaFact]
    public void TitleBar_ButtonsChangeWindowState_ShowsTitleAndServer()
    {
        var viewModel = MainViewModelSetup.Create(dir, action => action());
        var main = new MainWindow { DataContext = viewModel, Title = "OpenVoiceSpeak [zweit]" };
        main.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.True(main.ExtendClientAreaToDecorationsHint);
        Assert.NotNull(main.Icon); // Package 28: window and taskbar show the logo
        var bar = main.GetVisualDescendants().OfType<TitleBarView>().Single();
        // The title bar logo is 24 px and shows the pixel-hinted 24 px ICO frame, the same artwork as the taskbar
        var logo = bar.GetVisualDescendants().OfType<LogoMark>().Single();
        Assert.Equal(24, logo.Bounds.Width);
        Assert.True(logo.Bounds.Bottom <= bar.Bounds.Height);
        var small = logo.GetVisualDescendants().OfType<Image>().Single();
        Assert.True(small.IsEffectivelyVisible);
        Assert.Equal(24, ((Avalonia.Media.Imaging.Bitmap)small.Source!).PixelSize.Width);
        Assert.False(logo.GetVisualDescendants().OfType<Viewbox>().Single().IsVisible);
        Assert.Single(main.GetVisualDescendants().OfType<LogoMark>(), logoMark => logoMark.Bounds.Width >= 64); // start screen
        Button ButtonNamed(string name) => bar.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button) == name);

        Assert.Contains("OpenVoiceSpeak [zweit]", Texts(bar));
        MainViewModelSetup.ShowServer(viewModel, FakeServers.Admin());
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("· Gilde", Texts(bar));

        OverlayRig.Click(ButtonNamed("Maximieren"));
        Assert.Equal(WindowState.Maximized, main.WindowState);
        OverlayRig.Click(ButtonNamed("Wiederherstellen")); // the same button, relabelled
        Assert.Equal(WindowState.Normal, main.WindowState);
        OverlayRig.Click(ButtonNamed("Minimieren"));
        Assert.Equal(WindowState.Minimized, main.WindowState);
        main.WindowState = WindowState.Normal;

        bool closed = false;
        main.Closed += (_, _) => closed = true;
        OverlayRig.Click(ButtonNamed("Schliessen"));
        Assert.True(closed);
    }

    /// <summary>Package 62: the update card covers everything below the title bar and nothing underneath takes input.</summary>
    [AvaloniaFact]
    public void UpdateProgress_CoversWholeWindow_BlocksInput()
    {
        var viewModel = MainViewModelSetup.Create(dir, action => action(), server: FakeServers.Admin());
        var main = new MainWindow { DataContext = viewModel, Width = 1000, Height = 650 };
        main.Show();
        Dispatcher.UIThread.RunJobs();
        var layer = main.FindControl<Panel>("UpdateLayer")!;
        Assert.False(layer.IsVisible);

        viewModel.Update.Progress = new UpdateProgress("290926.0lh6", UpdatePhase.Downloading, 20 * 1024 * 1024, 50 * 1024 * 1024);
        Dispatcher.UIThread.RunJobs();
        var grid = (Control)layer.Parent!;
        Assert.True(layer.IsEffectivelyVisible);
        Assert.Equal(grid.Bounds.Size, layer.Bounds.Size);
        var bar = main.FindControl<ProgressBar>("UpdateBar")!;
        Assert.Equal((40d, false), (bar.Value, bar.IsIndeterminate));
        Assert.Contains(Texts(layer), text => text?.Contains("290926.0lh6", StringComparison.Ordinal) == true);
        Assert.Contains("40", main.FindControl<TextBlock>("UpdateDetail")!.Text, StringComparison.Ordinal);
        var chat = main.GetVisualDescendants().OfType<ChatView>().Single();
        Assert.False(chat.IsEffectivelyEnabled); // no clicks, no keys
        var card = layer.Children.OfType<Border>().Single();
        Assert.Equal(layer.Bounds.Width / 2, card.Bounds.Center.X, 1);

        viewModel.Update.Progress = viewModel.Update.Progress with { Total = null };
        Dispatcher.UIThread.RunJobs();
        Assert.True(bar.IsIndeterminate);

        viewModel.Update.Progress = null;
        Dispatcher.UIThread.RunJobs();
        Assert.False(layer.IsVisible);
        Assert.True(chat.IsEffectivelyEnabled);
        main.Close();
    }

    /// <summary>Package 64: own messages on the right, everyone else on the left, notices across the whole width.</summary>
    [AvaloniaFact]
    public void ChatLayout_OwnOthersAndNotices_RightLeftAndFullWidth()
    {
        var viewModel = MainViewModelSetup.Create(dir, action => action(), server: FakeServers.Admin());
        var main = new MainWindow { DataContext = viewModel, Width = 1000, Height = 650 };
        main.Show();
        viewModel.Chat!.AddNotice(new Notice(DateTime.Now, "Willkommen hier", NoticeKind.Welcome));
        viewModel.Server!.Apply(new ChatMessage(ChatTarget.Server, 2, "anna", null, null, "Hallo Gilde", DateTimeOffset.Now));
        viewModel.Server.Apply(new ChatMessage(ChatTarget.Server, 1, "ich", null, null, "Hallo anna", DateTimeOffset.Now));
        Dispatcher.UIThread.RunJobs();
        AssertSides(main, "Hallo Gilde", "Hallo anna");

        // the welcome card still spans the history
        var history = ChatHistory(main);
        var card = history.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("welcome"));
        Assert.Equal(history.Bounds.Width, card.Bounds.Width, 1);
        main.Close();
    }

    /// <summary>Package 64: a long own message stays inside three quarters of the history and wraps.</summary>
    [AvaloniaFact]
    public void Chat_LongMessage_BubbleCappedAndWraps()
    {
        var viewModel = MainViewModelSetup.Create(dir, action => action(), server: FakeServers.Admin());
        var main = new MainWindow { DataContext = viewModel, Width = 1000, Height = 650 };
        main.Show();
        var text = new string('x', 600);
        viewModel.Server!.Apply(new ChatMessage(ChatTarget.Server, 1, "ich", null, null, text, DateTimeOffset.Now));
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
        var viewModel = MainViewModelSetup.Create(dir, action => action(), server: FakeServers.Admin());
        var main = new MainWindow { DataContext = viewModel, Width = 1000, Height = 650 };
        main.Show();
        viewModel.Server!.Apply(new ChatMessage(ChatTarget.Private, 2, "anna", null, 1, "psst", DateTimeOffset.Now));
        viewModel.Server.Apply(new ChatMessage(ChatTarget.Private, 1, "ich", null, 2, "ja?", DateTimeOffset.Now));
        viewModel.Chat!.Selected = viewModel.Chat.Tabs.Single(chatTab => chatTab.IsPrivate);
        Dispatcher.UIThread.RunJobs();
        AssertSides(main, "psst", "ja?");
        main.Close();
    }

    static ItemsControl ChatHistory(MainWindow main) =>
        main.GetVisualDescendants().OfType<ChatView>().Single().GetVisualDescendants().OfType<ItemsControl>().Single(itemsControl => itemsControl is not Avalonia.Controls.Primitives.TabStrip);

    static SelectableTextBlock MessageBlock(MainWindow main, string text) =>
        ChatHistory(main).GetVisualDescendants().OfType<SelectableTextBlock>().Single(selectableTextBlock => selectableTextBlock.Text == text && selectableTextBlock.IsEffectivelyVisible);

    static Border Bubble(MainWindow main, string text) =>
        MessageBlock(main, text).GetVisualAncestors().OfType<Border>().First(border => border.Classes.Contains("bubble"));

    static void AssertSides(MainWindow main, string other, string own)
    {
        var history = ChatHistory(main);
        // Package 107: from the layout, not where a message still sliding in is drawn
        Rect InHistory(Visual visual) => new(new Point(LayoutAssert.X(visual, history)!.Value, LayoutAssert.Y(visual, history)!.Value), visual.Bounds.Size);

        var ownBubble = Bubble(main, own);
        Assert.Contains("own", ownBubble.Classes);
        Assert.Equal(history.Bounds.Width, InHistory(ownBubble).Right, 1);
        var ownRow = ownBubble.GetVisualAncestors().OfType<Grid>().First();
        Assert.DoesNotContain(ownRow.GetVisualDescendants().OfType<Border>(), border => border.Classes.Contains("avatar") && border.IsEffectivelyVisible);

        var otherBubble = Bubble(main, other);
        Assert.DoesNotContain("own", otherBubble.Classes);
        var otherRow = otherBubble.GetVisualAncestors().OfType<Grid>().First();
        var avatar = otherRow.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("avatar"));
        Assert.True(avatar.IsEffectivelyVisible);
        Assert.Equal(0, InHistory(avatar).Left, 1);
        Assert.True(InHistory(otherBubble).Left > InHistory(avatar).Right);
        Assert.True(InHistory(otherBubble).Right < history.Bounds.Width * 0.75 + 1);
    }

    /// <summary>Package 32 AC8: new lines scroll along, unless the reader scrolled up.</summary>
    [AvaloniaFact]
    public void Chat_FollowsNewLines_UnlessScrolledUp()
    {
        var viewModel = MainViewModelSetup.Create(dir, action => action(), server: FakeServers.Admin());
        var main = new MainWindow { DataContext = viewModel, Width = 900, Height = 500 };
        main.Show();
        var scroller = main.GetVisualDescendants().OfType<ChatView>().Single().GetVisualDescendants().OfType<ScrollViewer>().First();
        void Say(int index)
        {
            viewModel.Server!.Apply(new ChatMessage(ChatTarget.Server, 2, "anna", null, null, $"Nachricht {index}", DateTimeOffset.Now));
            Dispatcher.UIThread.RunJobs();
        }
        bool AtEnd() => scroller.Offset.Y >= scroller.Extent.Height - scroller.Viewport.Height - 1;

        for (int index = 0; index < 40; index++) Say(index);
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
        var viewModel = MainViewModelSetup.Create(dir, action => action(), server: FakeServers.Admin(sent));
        var main = new MainWindow { DataContext = viewModel, Width = 900, Height = 600 };
        main.Show();
        Dispatcher.UIThread.RunJobs();
        Point Center(string name) => Rows.Center(Rows.ChannelRow(main, name), main);
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
        Thickness LobbyMargin() => main.FindControl<ItemsControl>("ChannelItems")!.ContainerFromItem(viewModel.Server!.Tree.Channels.Single(channel => channel.Name == "Lobby"))!.Margin;
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
        var viewModel = MainViewModelSetup.Create(dir, action => action(), server: FakeServers.Admin(sent));
        var main = new MainWindow { DataContext = viewModel, Width = 1000, Height = 700 };
        main.Show();
        _ = viewModel.PageFlow.OpenAdminAsync();
        Dispatcher.UIThread.RunJobs();
        Point Center(string name)
        {
            var item = main.GetVisualDescendants().OfType<ListBoxItem>().Single(listBoxItem => listBoxItem.DataContext is GroupEditViewModel groupEdit && groupEdit.Name == name);
            return Rows.Center(item, main);
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
    public void LogoDropZone_DragOver_Highlighted()
    {
        var viewModel = MainViewModelSetup.Create(dir, action => action(), server: FakeServers.Admin());
        var main = new MainWindow { DataContext = viewModel, Width = 1100, Height = 700 };
        main.Show();
        _ = viewModel.PageFlow.OpenAdminAsync();
        Dispatcher.UIThread.RunJobs();
        main.GetVisualDescendants().OfType<TabItem>().Single(tabItem => tabItem.Header is "Server").IsSelected = true;
        Dispatcher.UIThread.RunJobs();
        var zone = main.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "IconDrop");
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
        var viewModel = MainViewModelSetup.Create(dir, action => action());
        var main = new MainWindow { DataContext = viewModel, Width = 1000, Height = 650 };
        main.Show();
        Dispatcher.UIThread.RunJobs();

        var button = main.GetVisualDescendants().OfType<Button>().Single(otherButton => AutomationProperties.GetName(otherButton) == "Mit Gilde verbinden");
        Assert.True(button.TranslatePoint(default, main)!.Value.X < 300); // in the sidebar, not on the start screen
        button.ContextMenu!.Open(button);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["Verbinden", "Bearbeiten ...", "Löschen ..."], button.ContextMenu.Items.OfType<MenuItem>().Select(menuItem => menuItem.Header as string));
        button.ContextMenu.Close();

        MainViewModelSetup.ShowServer(viewModel, FakeServers.Admin());
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
        var viewModel = MainViewModelSetup.Create(dir, action => action());
        var main = new MainWindow { DataContext = viewModel, Width = 1000, Height = 2400 };
        main.Show();
        viewModel.PageFlow.OpenSettings();
        Dispatcher.UIThread.RunJobs();
        SettingsPages.Open(main, Strings.Nav_Transmit);
        Assert.Contains(main.GetVisualDescendants().OfType<TextBlock>(),
            textBlock => textBlock.Text == "Push-to-Talk ist gewählt, aber keine Taste belegt. Ohne Taste hört dich niemand." && textBlock.IsEffectivelyVisible);
        var button = main.GetVisualDescendants().OfType<Button>().Single(otherButton => otherButton.Content is "Taste festlegen");
        Assert.True(button.IsEffectivelyVisible);
        viewModel.PageFlow.SettingsPage!.VoiceActivation = true;
        Dispatcher.UIThread.RunJobs();
        Assert.False(button.IsEffectivelyVisible);
        main.Close();
    }

    /// <summary>Package 61: only the large backgrounds become see-through; text, cards and dialogs stay solid.</summary>
    [AvaloniaFact]
    public void BackgroundOpacity_Lowered_OnlyBackgroundsSeeThrough()
    {
        var viewModel = MainViewModelSetup.Create(dir, action => action());
        var main = new MainWindow { DataContext = viewModel };
        main.Show();
        var app = Application.Current!;
        double Opacity(string key, ThemeVariant variant) =>
            app.Resources.TryGetResource(key, variant, out var value) && value is ISolidColorBrush brush ? brush.Opacity : double.NaN;
        try
        {
            BackgroundTransparency.Apply(app, main, new BackgroundAppearance(0.5f, false));
            foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
            {
                foreach (var key in BackgroundTransparency.BackgroundKeys) Assert.Equal(0.5, Opacity(key, variant), 3);
                foreach (var key in new[] { "Ovs.Text", "Ovs.Surface", "Ovs.DialogBg", "Ovs.DialogBar", "Ovs.Accent" }) Assert.Equal(1, Opacity(key, variant));
            }
            Assert.Equal([WindowTransparencyLevel.Transparent], main.TransparencyLevelHint);
            BackgroundTransparency.Apply(app, main, new BackgroundAppearance(0.5f, true));
            Assert.Equal(WindowTransparencyLevel.AcrylicBlur, main.TransparencyLevelHint[0]);

            _ = ConfirmDialog.ShowAsync(main.Overlay, "?");
            Dispatcher.UIThread.RunJobs();
            var card = main.Overlay.GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("dialog"));
            Assert.Equal(1, ((ISolidColorBrush)card.Background!).Opacity); // dialogs stay solid
            main.Overlay.Close();
            Dispatcher.UIThread.RunJobs();

            BackgroundTransparency.Apply(app, main, new BackgroundAppearance(1f, false));
            Assert.Empty(main.TransparencyLevelHint); // a normal window again
            Assert.Equal(1, Opacity("Ovs.Bg", ThemeVariant.Dark));
        }
        finally
        {
            BackgroundTransparency.Apply(app, main, new BackgroundAppearance(1f, false)); // the resources are shared by all tests
            main.Close();
        }
    }

    /// <summary>Package 152: the settings pages under their two groups; Audio is chosen when the page opens.</summary>
    [AvaloniaFact]
    public void Settings_Pages_ListedInGroups()
    {
        var viewModel = MainViewModelSetup.Create(dir, action => action());
        var main = new MainWindow { DataContext = viewModel, Width = 1100, Height = 800 };
        main.Show();
        viewModel.PageFlow.OpenSettings();
        Dispatcher.UIThread.RunJobs();
        var sections = SettingsPages.Sections(main);
        Assert.Equal(["SPRECHEN UND HÖREN", "Audio", "Sendemodus", "Tastenbelegung", "Sounds", "APP", "Darstellung", "Über und Updates"],
            sections.Items.OfType<TabItem>().Select(tabItem => tabItem.Header as string));
        Assert.Equal(["SPRECHEN UND HÖREN", "APP"], sections.Items.OfType<TabItem>().Where(tabItem => tabItem.Classes.Contains("navGroup")).Select(tabItem => tabItem.Header as string));
        Assert.Equal("Audio", ((TabItem)sections.SelectedItem!).Header);
        main.Close();
    }

    /// <summary>Package 152 (and 58): every section and named control of the former single page is on exactly one page.</summary>
    [AvaloniaFact]
    public void Settings_EveryControl_OnOnePage()
    {
        var viewModel = MainViewModelSetup.Create(dir, action => action());
        var main = new MainWindow { DataContext = viewModel, Width = 1100, Height = 3200 };
        main.Show();
        viewModel.PageFlow.OpenSettings();
        Dispatcher.UIThread.RunJobs();
        var found = new List<string>();
        foreach (var settingsPage in SettingsPages.All(main))
        {
            SettingsPages.Open(main, (string)settingsPage.Header!);
            var content = (Control)settingsPage.Content!;
            found.AddRange(content.GetVisualDescendants().OfType<TextBlock>().Where(textBlock => textBlock.Classes.Contains("section")).Select(textBlock => $"{settingsPage.Header}: {textBlock.Text}"));
            found.AddRange(content.GetVisualDescendants().OfType<Control>().Where(control => control.Name is "SelfTestButton" or "UpdateCheckBusy").Select(control => $"{settingsPage.Header}: {control.Name}"));
        }
        Assert.Equal(["Audio: GERÄTE", "Audio: LAUTSTÄRKE", "Audio: SPRACHFILTER", "Sendemodus: SelfTestButton", "Über und Updates: UpdateCheckBusy"], found);
        main.Close();
    }

    /// <summary>Package 53: the self test button beside the transmit mode, with the headphone hint.</summary>
    [AvaloniaFact]
    public void SelfTestButton_Clicked_TogglesTextAndShowsHeadphoneHint()
    {
        var viewModel = MainViewModelSetup.Create(dir, action => action());
        var main = new MainWindow { DataContext = viewModel, Width = 1000, Height = 2400 };
        main.Show();
        viewModel.PageFlow.OpenSettings();
        Dispatcher.UIThread.RunJobs();
        SettingsPages.Open(main, Strings.Nav_Transmit);
        Button SelfTest() => main.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button) is "Selbsttest starten" or "Selbsttest beenden");
        Assert.Contains(Texts(main), text => text?.Contains("Kopfhörer", StringComparison.Ordinal) == true);
        Assert.Equal("Selbsttest starten", AutomationProperties.GetName(SelfTest()));
        SelfTest().Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Selbsttest beenden", AutomationProperties.GetName(SelfTest()));
        Assert.Contains("Selbsttest beenden", Texts(main));
        Assert.True(viewModel.Audio.SelfTest);
        viewModel.PageFlow.SettingsPage!.CancelCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(viewModel.Audio.SelfTest);
        main.Close();
    }

    /// <summary>Package 51: the volume slider sits in the context menu of everyone but me; a changed volume shows in the tree.</summary>
    [AvaloniaFact]
    public void UserContextMenu_VolumeSlider_OnlyForOthers()
    {
        var viewModel = MainViewModelSetup.Create(dir, action => action());
        var main = new MainWindow { DataContext = viewModel, Width = 1000, Height = 650 };
        main.Show();
        MainViewModelSetup.ShowServer(viewModel, FakeServers.Admin());
        Dispatcher.UIThread.RunJobs();

        Border Row(string nick) => main.GetVisualDescendants().OfType<Border>()
            .Single(border => border.ContextMenu is not null && border.DataContext is UserViewModel user && user.Nickname == nick);
        UserMenuLook Menu(string nick)
        {
            var menu = Row(nick).ContextMenu!;
            menu.Open(Row(nick));
            Dispatcher.UIThread.RunJobs();
            var visible = menu.Items.OfType<MenuItem>().Where(menuItem => menuItem.IsVisible).ToList();
            var slider = visible.Select(menuItem => menuItem.Header).OfType<Control>().SelectMany(control => Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(control).Prepend(control)).OfType<Slider>().SingleOrDefault();
            var headers = visible.Select(menuItem => menuItem.Header as string).ToList();
            menu.Close();
            Dispatcher.UIThread.RunJobs();
            return new(slider, headers);
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
        bool IconShown(string tip) => Row("anna").GetVisualDescendants().OfType<PathIcon>().Any(pathIcon => ToolTip.GetTip(pathIcon) as string == tip && pathIcon.IsEffectivelyVisible);
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

    /// <summary>Package 119 (AC1, AC3, AC4): the user menu's "Gruppen" ticks the user's groups; a click sends at once.</summary>
    [AvaloniaFact]
    public void UserContextMenu_GroupsSubmenu_TicksAndSendsAtOnce()
    {
        var viewModel = MainViewModelSetup.Create(dir, action => action());
        var main = new MainWindow { DataContext = viewModel, Width = 1000, Height = 650 };
        main.Show();
        var sent = new List<Request>();
        var server = FakeServers.Admin(sent);
        MainViewModelSetup.ShowServer(viewModel, server);
        Dispatcher.UIThread.RunJobs();

        Border Row(string nick) => main.GetVisualDescendants().OfType<Border>()
            .Single(border => border.ContextMenu is not null && border.DataContext is UserViewModel user && user.Nickname == nick);
        MenuItem? Submenu(string nick, out ContextMenu menu)
        {
            menu = Row(nick).ContextMenu!;
            menu.Open(Row(nick));
            Dispatcher.UIThread.RunJobs();
            var groups = menu.Items.OfType<MenuItem>().SingleOrDefault(menuItem => menuItem.IsVisible && menuItem.Header as string == "Gruppen");
            if (groups is not null) groups.IsSubMenuOpen = true;
            Dispatcher.UIThread.RunJobs();
            return groups;
        }
        List<MenuTick> Ticks(MenuItem groups) => groups.GetLogicalChildren().OfType<MenuItem>()
            .Select(menuItem => new MenuTick(menuItem.Header as string, menuItem.IsChecked, menuItem.IsEffectivelyEnabled)).ToList();

        Assert.Null(Submenu("ich", out var own)); // never on myself
        own.Close();
        var groups = Submenu("anna", out var menu)!;
        Assert.NotNull(groups);
        Assert.Equal([new MenuTick("Gast", true, true), new MenuTick("Admin", false, true)], Ticks(groups));
        Assert.All(groups.GetLogicalChildren().OfType<MenuItem>(), menuItem => Assert.Equal(MenuItemToggleType.CheckBox, menuItem.ToggleType));

        // a click ticks and sends at once (a real click: the menu item ticks itself before it runs the command)
        var admin = groups.GetLogicalChildren().OfType<MenuItem>().Single(menuItem => menuItem.Header as string == "Admin");
        var popup = TopLevel.GetTopLevel(admin)!;
        var at = Rows.Center(admin, popup);
        popup.MouseDown(at, MouseButton.Left);
        popup.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.True(admin.IsChecked);
        Assert.Equal(new AssignGroup("fp2", WellKnownGroups.Admin), sent[^1] with { RequestId = null });
        menu.Close();
        Dispatcher.UIThread.RunJobs();

        // the server's update (or a change made elsewhere) shows the next time the menu opens
        server.Apply(new UserUpdated(server.Mirror.Users[2] with { GroupIds = [WellKnownGroups.Admin] }));
        Dispatcher.UIThread.RunJobs();
        groups = Submenu("anna", out menu)!;
        Assert.Equal([new MenuTick("Gast", false, true), new MenuTick("Admin", true, true)], Ticks(groups));
        menu.Close();
        main.Close();
    }

    /// <summary>Package 120 (AC1): "Verschieben" lists the other channels; a click moves at once, no dialog opens.</summary>
    [AvaloniaFact]
    public void UserContextMenu_MoveSubmenu_ListsChannelsAndMovesAtOnce()
    {
        var viewModel = MainViewModelSetup.Create(dir, action => action());
        var main = new MainWindow { DataContext = viewModel, Width = 1000, Height = 650 };
        main.Show();
        var sent = new List<Request>();
        MainViewModelSetup.ShowServer(viewModel, FakeServers.Admin(sent));
        Dispatcher.UIThread.RunJobs();

        Border Row(string nick) => main.GetVisualDescendants().OfType<Border>()
            .Single(border => border.ContextMenu is not null && border.DataContext is UserViewModel user && user.Nickname == nick);
        MenuItem? Submenu(string nick, out ContextMenu menu)
        {
            menu = Row(nick).ContextMenu!;
            menu.Open(Row(nick));
            Dispatcher.UIThread.RunJobs();
            var move = menu.Items.OfType<MenuItem>().SingleOrDefault(menuItem => menuItem.IsVisible && menuItem.Header as string == "Verschieben");
            if (move is not null) move.IsSubMenuOpen = true;
            Dispatcher.UIThread.RunJobs();
            return move;
        }

        Assert.Null(Submenu("ich", out var own)); // never on myself: dragging my row joins
        own.Close();
        var move = Submenu("anna", out var menu)!;
        var lobby = Assert.Single(move.GetLogicalChildren().OfType<MenuItem>()); // anna sits in Raid
        Assert.Equal("Lobby", lobby.Header);
        var popup = TopLevel.GetTopLevel(lobby)!;
        var at = Rows.Center(lobby, popup);
        popup.MouseDown(at, MouseButton.Left);
        popup.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new MoveUser(2, FakeServers.Lobby), sent.OfType<MoveUser>().Single() with { RequestId = null });
        Assert.False(main.Overlay.IsOpen);
        menu.Close();
        main.Close();
    }

    /// <summary>Everything a user can read or hear from a screen reader: texts, tooltips, names, headers, menus.</summary>
    static IEnumerable<string> AllUserTexts(Visual root) =>
        root.GetVisualDescendants().OfType<Control>().SelectMany(control => new[]
            {
                (control as TextBlock)?.Text, ToolTip.GetTip(control) as string, AutomationProperties.GetName(control),
                (control as ContentControl)?.Content as string, (control as Avalonia.Controls.Primitives.HeaderedContentControl)?.Header as string, (control as TextBox)?.Watermark,
            }
            .Concat(control.ContextMenu?.Items.OfType<MenuItem>().Select(menuItem => menuItem.Header as string) ?? []))
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
            var viewModel = MainViewModelSetup.Create(dir, action => action(), server: FakeServers.Admin());
            var main = new MainWindow { DataContext = viewModel, Width = 1100, Height = 700 };
            main.Show();
            Dispatcher.UIThread.RunJobs();
            var visible = main.GetVisualDescendants().OfType<TextBlock>().Where(textBlock => textBlock.IsEffectivelyVisible).Select(textBlock => textBlock.Text).ToList();
            Assert.Contains(expected, visible);
            Assert.DoesNotContain(expected + " ...", visible);
            Assert.EndsWith(" ...", OVS.Client.Localization.Strings.Ui_RedeemTokenMenu, StringComparison.Ordinal); // it opens a dialog
            main.Close();
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = before;
        }
    }

    static List<PathIcon> VisibleIcons(Visual row) => row.GetVisualDescendants().OfType<PathIcon>().Where(pathIcon => pathIcon.IsEffectivelyVisible).ToList();

    /// <summary>Package 66: a muted channel shows the mute icon in front instead of the speaker, and none behind its name.</summary>
    [AvaloniaFact]
    public void MutedChannel_Shown_IconReplacesSpeaker()
    {
        var viewModel = MainViewModelSetup.Create(dir, action => action(), server: FakeServers.Admin());
        var main = new MainWindow { DataContext = viewModel, Width = 1100, Height = 700 };
        main.Show();
        viewModel.Server!.Apply(new ChannelUpdated(new ChannelInfo(FakeServers.Lobby, "Lobby", "Start", 0, IsMuted: true)));
        Dispatcher.UIThread.RunJobs();
        var micOff = main.FindResource("Icon.MicOff");
        var speaker = main.FindResource("Icon.Speaker");

        var lobby = VisibleIcons(Rows.ChannelRow(main, "Lobby"));
        Assert.Same(micOff, lobby[0].Data);
        Assert.Contains("warning", lobby[0].Classes);
        Assert.Equal(OVS.Client.Localization.Strings.Dlg_MutedChannel, ToolTip.GetTip(lobby[0]));
        Assert.DoesNotContain(lobby, pathIcon => pathIcon.Data == speaker);
        Assert.Single(lobby, pathIcon => pathIcon.Data == micOff); // nothing behind the name
        Assert.Same(speaker, VisibleIcons(Rows.ChannelRow(main, "Raid"))[0].Data);

        viewModel.Server.Apply(new ChannelUpdated(new ChannelInfo(FakeServers.Lobby, "Lobby", "Start", 0)));
        Dispatcher.UIThread.RunJobs();
        lobby = VisibleIcons(Rows.ChannelRow(main, "Lobby"));
        Assert.Same(speaker, lobby[0].Data);
        Assert.DoesNotContain(lobby, pathIcon => pathIcon.Data == micOff);
        main.Close();
    }

    /// <summary>Package 67: a linked channel shows only a link icon behind its name, the partners are in its tooltip.</summary>
    [AvaloniaFact]
    public void LinkedChannel_IconWithTooltip_NoChip()
    {
        var viewModel = MainViewModelSetup.Create(dir, action => action(), server: FakeServers.Admin());
        var main = new MainWindow { DataContext = viewModel, Width = 1100, Height = 700 };
        main.Show();
        Dispatcher.UIThread.RunJobs();
        var row = Rows.ChannelRow(main, "Lobby");
        Assert.DoesNotContain(row.GetVisualDescendants().OfType<Border>(), border => border.Classes.Contains("chip"));
        var link = Assert.Single(VisibleIcons(row), pathIcon => pathIcon.Data == main.FindResource("Icon.Link"));
        Assert.Contains("link", link.Classes);
        Assert.Equal("Verlinkt mit Raid", ToolTip.GetTip(link));
        main.Close();
    }

    static Border Sidebar(MainWindow main) => main.FindControl<Border>("Sidebar")!;

    static readonly string[] SquadChannels =
        ["Infantry Squad 1", "Infantry Squad 2", "Sabotage Squad", "Logistics and Support Squad", "Artillery", "FoB"];

    /// <summary>
    /// Packages 24 (AC6) and 27 (AC5): every icon button and every title bar button has a tooltip and a screen reader name;
    /// a disabled icon button is dimmed.
    /// </summary>
    [AvaloniaFact]
    public void IconButtons_Connected_HaveTooltipAndNameAndDimWhenDisabled()
    {
        var viewModel = MainViewModelSetup.Create(dir, action => action());
        var main = new MainWindow { DataContext = viewModel, Width = 1100, Height = 700 };
        main.Show();
        MainViewModelSetup.ShowServer(viewModel, FakeServers.WithChannels(SquadChannels));
        Dispatcher.UIThread.RunJobs();

        var buttons = main.GetVisualDescendants().OfType<Button>()
            .Where(button => button.IsEffectivelyVisible && (button.Classes.Contains("icon") || button.Classes.Contains("caption"))).ToList();
        Assert.True(buttons.Count >= 5, $"only {buttons.Count} icon buttons");
        Assert.All(buttons, button => Assert.False(string.IsNullOrEmpty(AutomationProperties.GetName(button)), $"{button.Name} has no name"));
        Assert.All(buttons, button => Assert.False(ToolTip.GetTip(button) is null or "", $"{AutomationProperties.GetName(button)} has no tooltip"));

        var icon = buttons.First(button => button.Classes.Contains("icon"));
        icon.IsEnabled = false;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0.4, icon.Opacity);
    }

    /// <summary>Package 67: after connecting, every channel name fits without trimming and the sidebar is not wider than needed.</summary>
    [AvaloniaFact]
    public void Sidebar_Connected_FitsLongestChannel()
    {
        var viewModel = MainViewModelSetup.Create(dir, action => action());
        var main = new MainWindow { DataContext = viewModel, Width = 1100, Height = 700 };
        main.Show();
        var server = FakeServers.WithChannels(SquadChannels);
        // Package 93: the lock icon behind the longest name counts as well
        var longest = server.Mirror.Channels.Values.Single(channel => channel.Name == "Logistics and Support Squad");
        server.Apply(new ChannelUpdated(longest with { AllowedGroupIds = [WellKnownGroups.Admin] }));
        MainViewModelSetup.ShowServer(viewModel, server);
        Dispatcher.UIThread.RunJobs();

        Assert.True(Sidebar(main).Bounds.Width >= 240);
        var slack = new List<double>();
        foreach (var name in SquadChannels)
        {
            var row = Rows.ChannelRow(main, name);
            var text = row.GetVisualDescendants().OfType<TextBlock>().Single(textBlock => textBlock.Classes.Contains("channelName"));
            Assert.False(text.TextLayout.TextLines.Any(textLine => textLine.HasCollapsed), $"{name} ist abgeschnitten");
            var group = (Control)text.Parent!; // name, home and link icon
            var count = row.GetVisualDescendants().OfType<TextBlock>().Single(textBlock => textBlock.Classes.Contains("channelCount"));
            var groupRight = group.TranslatePoint(new Point(group.Bounds.Width, 0), row)!.Value.X;
            var countLeft = count.TranslatePoint(default, row)!.Value.X;
            slack.Add(countLeft - groupRight - count.Margin.Left);
        }
        Assert.True(slack.Min() >= -0.5, $"überlappt: {slack.Min()}");
        Assert.True(slack.Min() <= 1, $"zu breit: {slack.Min()}");
        main.Close();
    }

    /// <summary>Package 112: a channel without description and links has no tooltip on its row, so none opens.</summary>
    [AvaloniaFact]
    public void ChannelRow_NoDescription_NoTooltipOpens()
    {
        var viewModel = MainViewModelSetup.Create(dir, action => action());
        var main = new MainWindow { DataContext = viewModel, Width = 1100, Height = 700 };
        main.Show();
        MainViewModelSetup.ShowServer(viewModel, FakeServers.WithChannels("Lobby", "Raid"));
        viewModel.Server!.Apply(new ChannelsUnlinked(viewModel.Server.Tree.Channels[0].Id, viewModel.Server.Tree.Channels[1].Id));
        Dispatcher.UIThread.RunJobs();
        var row = main.FindControl<ItemsControl>("ChannelItems")!.ContainerFromIndex(1)!
            .GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("row"));
        Assert.Null(ToolTip.GetTip(row));
        ToolTip.SetIsOpen(row, true); // what hovering does once the delay is over
        Dispatcher.UIThread.RunJobs();
        Assert.False(ToolTip.GetIsOpen(row));
        main.Close();
    }

    /// <summary>Package 111: a separator is one line across its row: no icon, no number, no name; the fitted width ignores it.</summary>
    [AvaloniaFact]
    public void Separator_IsALine_NoIconNoCount()
    {
        var viewModel = MainViewModelSetup.Create(dir, action => action());
        var main = new MainWindow { DataContext = viewModel, Width = 1100, Height = 700 };
        main.Show();
        MainViewModelSetup.ShowServer(viewModel, FakeServers.WithChannels(SquadChannels));
        Dispatcher.UIThread.RunJobs();
        var fitted = Sidebar(main).Bounds.Width;
        var id = Guid.NewGuid();
        viewModel.Server!.Apply(new ChannelAdded(new ChannelInfo(id, "", "", 1, Kind: ChannelKind.Separator)));
        Dispatcher.UIThread.RunJobs();

        // the new row slides in (animated display): until it is in, not for a fixed time that a busy machine may not give it frames in
        var items = main.FindControl<ItemsControl>("ChannelItems")!;
        Control? Container() => items.GetRealizedContainers().SingleOrDefault(control => control.DataContext is ChannelViewModel { Id: var cid } && cid == id);
        MotionWait.WaitFor(() => Container() is { } entered && !entered.Classes.Contains("entering"), 5000);
        var container = Container();
        Assert.NotNull(container);
        Assert.DoesNotContain("entering", container.Classes);
        var row = container.GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("row"));
        var line = row.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("separatorLine"));
        Assert.True(line.IsEffectivelyVisible);
        Assert.Equal(1, line.Bounds.Height);
        Assert.True(line.Bounds.Width >= row.Bounds.Width - row.Padding.Left - row.Padding.Right - 1, "across the row");
        Assert.DoesNotContain(row.GetVisualDescendants().OfType<Control>(), control => control.IsEffectivelyVisible
            && (control.Classes.Contains("channelIcon") || control.Classes.Contains("channelCount") || control.Classes.Contains("channelName")));
        Assert.Null(ToolTip.GetTip(row));
        Assert.Equal(fitted, Sidebar(main).Bounds.Width, 1);
        main.Close();
    }

    /// <summary>Package 67: a width the user dragged stays until the next connect, even when channels change.</summary>
    [AvaloniaFact]
    public void SidebarWidth_DraggedByUser_KeptUntilReconnect()
    {
        var viewModel = MainViewModelSetup.Create(dir, action => action());
        var main = new MainWindow { DataContext = viewModel, Width = 1100, Height = 700 };
        main.Show();
        MainViewModelSetup.ShowServer(viewModel, FakeServers.WithChannels(SquadChannels));
        Dispatcher.UIThread.RunJobs();
        var fitted = Sidebar(main).Bounds.Width;

        // a new, longer channel widens the sidebar while the user has not dragged
        viewModel.Server!.Apply(new ChannelAdded(new ChannelInfo(Guid.NewGuid(), "Logistics and Support Squad Number Two", "", 9)));
        Dispatcher.UIThread.RunJobs();
        var wider = Sidebar(main).Bounds.Width;
        Assert.True(wider > fitted, $"{wider} <= {fitted}");

        var splitter = main.GetVisualDescendants().OfType<GridSplitter>().Single();
        ((Grid)splitter.Parent!).ColumnDefinitions[0].Width = new GridLength(wider + 80);
        splitter.RaiseEvent(new Avalonia.Input.VectorEventArgs { RoutedEvent = Avalonia.Controls.Primitives.Thumb.DragCompletedEvent });
        Dispatcher.UIThread.RunJobs();
        viewModel.Server.Apply(new ChannelAdded(new ChannelInfo(Guid.NewGuid(), "Logistics and Support Squad Number Three and Four", "", 10)));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(wider + 80, Sidebar(main).Bounds.Width, 1);

        MainViewModelSetup.ShowServer(viewModel, FakeServers.WithChannels(SquadChannels));
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
            var viewModel = MainViewModelSetup.Create(dir, action => action());
            var main = new MainWindow { DataContext = viewModel, Width = 1100, Height = 750 };
            main.Show();
            Dispatcher.UIThread.RunJobs();
            var seen = AllUserTexts(main).ToList();
            Assert.Contains("Connect ...", seen);

            MainViewModelSetup.ShowServer(viewModel, FakeServers.Admin());
            Dispatcher.UIThread.RunJobs();
            seen.AddRange(AllUserTexts(main));
            viewModel.PageFlow.OpenSettings();
            Dispatcher.UIThread.RunJobs();
            foreach (var settingsPage in SettingsPages.All(main))
            {
                SettingsPages.Open(main, (string)settingsPage.Header!);
                seen.AddRange(AllUserTexts(main));
            }
            viewModel.PageFlow.CloseWithoutAsking();
            _ = viewModel.PageFlow.OpenAdminAsync();
            Dispatcher.UIThread.RunJobs();
            foreach (var tab in main.GetVisualDescendants().OfType<TabControl>().First().Items.OfType<TabItem>())
            {
                tab.IsSelected = true;
                Dispatcher.UIThread.RunJobs();
                seen.AddRange(AllUserTexts(main));
            }
            viewModel.PageFlow.CloseWithoutAsking();
            foreach (var open in new Func<OverlayHost, Task>[]
            {
                overlayHost => ConnectDialog.ShowAsync(overlayHost, viewModel.Bookmarks.Saved), overlayHost => BanDialog.ShowAsync(overlayHost, "anna"), overlayHost => ConfirmDialog.ShowAsync(overlayHost, "?"),
                overlayHost => ChannelDialogView.ShowAsync(overlayHost, new ChannelEdit("Raid", ""), ChannelDialogMode.Edit),
                overlayHost => ChannelDialogView.ShowAsync(overlayHost, new ChannelEdit("", ""), ChannelDialogMode.Create),
                overlayHost => KeyBindingDialog.ShowAsync(overlayHost, null, _ => Task.FromResult<OVS.Client.Input.KeyChord?>(null)),
                overlayHost => TofuDialog.ShowAsync(overlayHost, new TofuPrompt("h", 1, new string('a', 64), TofuResult.Unknown)),
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
            Assert.Contains("Appearance", seen);
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
    WaitingScene OpenWaiting(double width = 1000)
    {
        var viewModel = MainViewModelSetup.Create(dir, action => action());
        var main = new MainWindow { DataContext = viewModel, Width = width, Height = 700 };
        main.Show();
        var sent = new List<Request>();
        var time = new ManualTimeProvider();
        ServerViewModel? server = null;
        viewModel.Dialogs = OverlayDialogs.Create(main.Overlay, () => server?.Mirror.Groups); // as App does: the admin page asks through the page flow
        server = FakeServers.Admin(sent, time, viewModel.Dialogs.Server);
        MainViewModelSetup.ShowServer(viewModel, server);
        Dispatcher.UIThread.RunJobs();
        return new(main, viewModel, server, sent, time);
    }

    static void Advance(ManualTimeProvider time, int milliseconds)
    {
        time.Advance(TimeSpan.FromMilliseconds(milliseconds));
        Dispatcher.UIThread.RunJobs();
    }

    static Button OverlayButton(MainWindow main, string text) =>
        main.Overlay.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == text
            || button.GetVisualDescendants().OfType<TextBlock>().Any(textBlock => textBlock.Text == text && textBlock.IsEffectivelyVisible));

    static bool SpinnerIn(Visual root) => root.GetVisualDescendants().OfType<BusySpinner>().Any(busySpinner => busySpinner.IsEffectivelyVisible);

    /// <summary>Package 98 (AC3): a dialog that sends keeps its card open with a busy button until the server confirms; errors show inside.</summary>
    [AvaloniaFact]
    public void SendingDialogs_Sent_StayOpenBusyUntilConfirmed()
    {
        var (main, _, server, sent, time) = OpenWaiting();

        // create a channel: busy, then closed by the server's ChannelAdded
        _ = server.NewChannelCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        main.Overlay.GetVisualDescendants().OfType<TextBox>().First().Text = "Neu";
        OverlayRig.Click(OverlayButton(main, "Anlegen"));
        Assert.Single(sent.OfType<CreateChannel>());
        Assert.True(main.Overlay.IsOpen);
        Advance(time, 150);
        var create = OverlayButton(main, "Anlegen");
        Assert.False(create.IsEnabled);
        Assert.True(SpinnerIn(create));
        OverlayRig.Click(create); // a second click meanwhile sends nothing
        Assert.Single(sent.OfType<CreateChannel>());
        server.Apply(new ChannelAdded(new ChannelInfo(Guid.NewGuid(), "Neu", "", 2), sent.OfType<CreateChannel>().Single().RequestId));
        Dispatcher.UIThread.RunJobs();
        Assert.False(main.Overlay.IsOpen);

        // ban: refused, the card stays with the reason and the button works again
        var anna = server.Tree.Channels.SelectMany(channel => channel.Users).Single(user => user.Nickname == "anna");
        _ = anna.BanCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        OverlayRig.Click(OverlayButton(main, "Bannen"));
        Advance(time, 150);
        Assert.True(SpinnerIn(main.Overlay));
        server.Apply(new Error(sent.OfType<BanSession>().Single().RequestId, ReasonCodes.PermissionDenied));
        Dispatcher.UIThread.RunJobs();
        Assert.True(main.Overlay.IsOpen);
        Assert.Contains(main.Overlay.GetVisualDescendants().OfType<TextBlock>(), textBlock => textBlock.Text == ErrorTexts.For(ReasonCodes.PermissionDenied) && textBlock.IsEffectivelyVisible);
        Assert.True(OverlayButton(main, "Bannen").IsEnabled);
        Assert.False(SpinnerIn(main.Overlay));

        // no answer at all: after 10 s the card says so and stays
        OverlayRig.Click(OverlayButton(main, "Bannen"));
        Advance(time, 10_000);
        Assert.True(main.Overlay.IsOpen);
        Assert.Contains(main.Overlay.GetVisualDescendants().OfType<TextBlock>(), textBlock => textBlock.Text == OVS.Client.Localization.Strings.Pending_NoAnswer && textBlock.IsEffectivelyVisible);
        main.Overlay.Close();
        Dispatcher.UIThread.RunJobs();

        // redeem a token: closed once the own user comes back
        _ = server.RedeemTokenCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        main.Overlay.GetVisualDescendants().OfType<TextBox>().Single().Text = "geheim";
        OverlayRig.Click(OverlayButton(main, "OK"));
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
    public void WaitingActions_Every_BusyVisibleWithin400ms()
    {
        var (main, viewModel, server, sent, time) = OpenWaiting();
        var checkedActions = new List<string>();
        Request Last<T>() where T : Request => sent.OfType<T>().Last();
        ChannelViewModel Channel(string name) => server.Tree.Channels.Single(channel => channel.Name == name);
        UserViewModel User(string name) => server.Tree.Channels.SelectMany(channel => channel.Users).Single(user => user.Nickname == name);
        UserInfo Info(string name) => server.Mirror.Users.Values.Single(user => user.Nickname == name);
        bool Clock() => main.GetVisualDescendants().OfType<PathIcon>().Any(pathIcon => ToolTip.GetTip(pathIcon) as string == OVS.Client.Localization.Strings.Chat_Sending && pathIcon.IsEffectivelyVisible);
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
                .Where(busySpinner => busySpinner.IsEffectivelyVisible).Select(busySpinner => busySpinner.GetVisualAncestors().OfType<Control>().FirstOrDefault(control => control.Name is not null)?.Name ?? "?")));
            checkedActions.Add(name);
        }
        void Dialog(string ok, Action<Visual>? fill = null)
        {
            Dispatcher.UIThread.RunJobs();
            fill?.Invoke(main.Overlay);
            OverlayRig.Click(OverlayButton(main, ok));
        }

        // ---- the channel tree and the chat ----
        Waits("join", () => _ = Channel("Raid").JoinCommand.ExecuteAsync(null), () => [new UserUpdated(Info("ich") with { ChannelId = FakeServers.Raid })]);
        Waits("chat", () =>
        {
            viewModel.Chat!.Draft = "Hallo";
            _ = viewModel.Chat.SendCommand.ExecuteAsync(null);
        }, () => [new ChatMessage(ChatTarget.Server, 1, "ich", null, null, "Hallo", DateTimeOffset.Now)]);
        Waits("server mute", () => _ = User("anna").ToggleServerMuteCommand.ExecuteAsync(null), () => [new UserUpdated(Info("anna") with { ServerMuted = !Info("anna").ServerMuted })]);
        Waits("reorder", () => _ = server.Tree.MoveChannelAsync(Channel("Raid"), Channel("Lobby"), after: false),
            () => [new ChannelUpdated(new ChannelInfo(FakeServers.Raid, "Raid", "", 0)), new ChannelUpdated(new ChannelInfo(FakeServers.Lobby, "Lobby", "Start", 1))]);
        var created = Guid.NewGuid();
        Waits("create channel", () =>
        {
            _ = server.NewChannelCommand.ExecuteAsync(null);
            Dialog("Anlegen", visual => visual.GetVisualDescendants().OfType<TextBox>().First().Text = "Neu");
        }, () => [new ChannelAdded(new ChannelInfo(created, "Neu", "", 2), Last<CreateChannel>().RequestId)]);
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
        Waits("move user", () => User("anna").MoveTargets[0].Command.Execute(null), () => [new UserUpdated(Info("anna") with { ChannelId = ((MoveUser)Last<MoveUser>()).ChannelId })]);
        Waits("redeem token", () =>
        {
            _ = server.RedeemTokenCommand.ExecuteAsync(null);
            Dialog("OK", visual => visual.GetVisualDescendants().OfType<TextBox>().Single().Text = "geheim");
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
            _ = viewModel.PageFlow.OpenAdminAsync();
            Dispatcher.UIThread.RunJobs();
            main.GetVisualDescendants().OfType<AdminView>().Single().GetVisualDescendants().OfType<TabItem>().Single(tabItem => tabItem.Header as string == "Nutzer").IsSelected = true;
        }, () =>
        [
            new UserList(Last<ListUsers>().RequestId, [new KnownUserInfo("fp2", "anna", [WellKnownGroups.Guest], ServerMuted: true, CanBeModeratedByMe: true)]),
            new BanList(Last<ListBans>().RequestId, [new BanInfo(Guid.NewGuid(), "fp4", "troll", null, "Spam", "ich", null)]),
            new BackupList(Last<ListBackups>().RequestId, []),
            new LogList(Last<ListLogs>().RequestId, []),
        ]);
        var admin = viewModel.PageFlow.AdminPage!;
        var page = main.GetVisualDescendants().OfType<AdminView>().Single();
        void Tab(string header)
        {
            page.GetVisualDescendants().OfType<TabItem>().Single(tabItem => tabItem.Header as string == header).IsSelected = true;
            Dispatcher.UIThread.RunJobs();
        }
        Tab("Gruppen");
        Waits("group save", () =>
        {
            admin.GroupList.SelectedGroup = admin.GroupList.Groups.Single(groupEdit => groupEdit.Name == "Gast");
            _ = admin.GroupList.SaveGroupCommand.ExecuteAsync(null);
        }, () => [new GroupsChanged(server.Mirror.Groups)]);
        Waits("group order", () => _ = admin.GroupList.MoveGroupAsync(admin.GroupList.Groups.Single(groupEdit => groupEdit.Name == "Admin"), admin.GroupList.Groups[0], after: false),
            () => [new GroupsChanged([.. Enumerable.Reverse(server.Mirror.Groups)])]);
        Tab("Nutzer");
        // the group changes asked for the user list again (the rights on others may change): answered first, also the wish
        // that came meanwhile and follows a second later
        for (var index = 0; index < 2; index++)
        {
            server.Apply(new UserList(Last<ListUsers>().RequestId, [new KnownUserInfo("fp2", "anna", [WellKnownGroups.Guest], ServerMuted: true, CanBeModeratedByMe: true)]));
            Advance(time, 1000);
        }
        Waits("user card", () => _ = admin.UsersAndBans.Users.Single().LiftMuteCommand.ExecuteAsync(null),
            () => [new UserList(Last<ListUsers>().RequestId, [new KnownUserInfo("fp2", "anna", [WellKnownGroups.Guest], CanBeModeratedByMe: true)])]);
        Tab("Bans");
        Waits("unban", () => _ = admin.UsersAndBans.Bans.Single().UnbanCommand.ExecuteAsync(null), () => [new BanList(Last<Unban>().RequestId, [])]);
        Tab("Links");
        Waits("link apply", () =>
        {
            admin.Links.Set(FakeServers.Lobby, FakeServers.Raid, false);
            _ = admin.Links.ApplyCommand.ExecuteAsync(null);
        }, () => [new ChannelsUnlinked(FakeServers.Lobby, FakeServers.Raid)]);
        Tab("Server");
        page.FindControl<Button>("NewBackupButton")!.BringIntoView();
        Waits("backup", () => _ = admin.BackupCard.NewBackupCommand.ExecuteAsync(null), () => [new BackupList(Last<CreateBackup>().RequestId, [])]);
        page.FindControl<Border>("IconDrop")!.BringIntoView();
        Waits("logo", () => admin.ServerIcon.UploadIcon(TestImages.Encode(64, 64), null), () => [new ServerSettingsChanged(server.Mirror.Settings)]);
        Tab("Logs");
        Waits("log search", () =>
        {
            admin.Logs.SearchText = "fehler";
            _ = admin.Logs.SearchCommand.ExecuteAsync(null);
        }, () => [new LogSearchResult(Last<SearchLogs>().RequestId, [], false, false)]);
        viewModel.PageFlow.CloseWithoutAsking();

        // ---- the settings: saving while the audio devices restart, the update check ----
        var restart = new TaskCompletionSource();
        var check = new TaskCompletionSource<string>();
        viewModel.PageFlow.ShownPage = new SettingsViewModel(viewModel.Settings, [], [], time: time) { ApplySaved = () => restart.Task, CheckNow = () => check.Task };
        viewModel.PageFlow.Page = Page.Settings;
        Dispatcher.UIThread.RunJobs();
        SettingsPages.Open(main, OVS.Client.Localization.Strings.Nav_About);
        Waits("update check", () => _ = viewModel.PageFlow.SettingsPage!.CheckUpdatesCommand.ExecuteAsync(null), () =>
        {
            check.SetResult("Aktuell");
            return [];
        });
        Waits("settings save", () => _ = viewModel.PageFlow.SettingsPage!.SaveAndCloseCommand.ExecuteAsync(null), () =>
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
        var brush = (ISolidColorBrush)AvatarConverters.AvatarBrush.Convert("anna", typeof(IBrush), null, System.Globalization.CultureInfo.InvariantCulture)!;
        var again = (ISolidColorBrush)AvatarConverters.AvatarBrush.Convert("Anna", typeof(IBrush), null, System.Globalization.CultureInfo.InvariantCulture)!;
        Assert.Equal(brush.Color, again.Color);
        Assert.Equal("A", AvatarConverters.Initials.Convert(" anna", typeof(string), null, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("?", AvatarConverters.Initials.Convert("", typeof(string), null, System.Globalization.CultureInfo.InvariantCulture));
    }

    sealed record ChannelDialogLook(string Title, List<string?> Buttons, string? Name, bool? Muted, decimal? Slots, bool SlotsEnabled);

    sealed record WaitingScene(MainWindow Main, MainViewModel Vm, ServerViewModel Server, List<Request> Sent, ManualTimeProvider Time);

    sealed record MenuTick(string? Header, bool IsChecked, bool IsEnabled);

    sealed record UserMenuLook(Slider? Slider, List<string?> Headers);
}
