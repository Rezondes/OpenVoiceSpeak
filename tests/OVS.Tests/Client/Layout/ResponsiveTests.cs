using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OVS.Client.Animations;
using OVS.Client.Controls;
using OVS.Client.Dialogs;
using OVS.Client.Input;
using OVS.Client.Layout;
using OVS.Client.Localization;
using OVS.Client.Models;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Client.Views;
using OVS.Shared.Protocol.Messages.Users;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Layout;

/// <summary>Package 68 (A93 to A95): the main window adapts to every width from 360 px.</summary>
[Collection(MotionCollection.Name)]
public sealed class ResponsiveTests() : TempDirectoryTest("ovs-resp-", quiet: true)
{
    static readonly string[] UnsavedButtons = ["UnsavedReset", "UnsavedSave"];
    static readonly string[] ServerSectionControls = ["MaxUsersInput", "LogDaysInput", "LogRotateDailyInput", "AutoRestartInput", "AutoRestartTimeInput"];
    static readonly string[] BackupControls = ["UploadBackupButton", "UploadRestoreButton", "BackupTransfer"];
    static readonly string[] LogPageControls = ["LogPager", "LogLineFilter", "LogLines"];
    static readonly string[] LogSearchControls = ["LogSearch", "LogSourceFilter", "LogFrom", "LogTo", "LogFileList", "LogDownloadBar", "LogDownloadButton", "LogTransfer", "LogSearchBusy"];

    MainWindow Open(double width, ServerViewModel? server)
    {
        // the device list loads in the background (Package 52): its result comes back through the dispatcher, not on a pool thread
        var viewModel = MainViewModelSetup.Create(dir, action => Dispatcher.UIThread.Post(action));
        var main = new MainWindow { DataContext = viewModel, Width = width, Height = 700 };
        main.Closed += (_, _) => { _ = viewModel.DisposeAsync().AsTask(); }; // the window owns its view model
        main.Show();
        MainViewModelSetup.ShowServer(viewModel, server);
        Dispatcher.UIThread.RunJobs();
        return main;
    }

    static void Resize(Window main, double width)
    {
        main.Width = width;
        Dispatcher.UIThread.RunJobs();
    }

    static Border Sidebar(MainWindow main) => main.FindControl<Border>("Sidebar")!;

    /// <summary>Package 105: closing counts at once, the drawer only slides away for a moment (in the animated display).</summary>
    static void AssertDrawerClosed(MainWindow main)
    {
        // generous: under load one frame can take longer than the whole slide, and the connect sequence runs beside it
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (Sidebar(main).IsEffectivelyVisible && watch.ElapsedMilliseconds < 5000)
        {
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            MotionWait.Pause();
        }
        Assert.False(Sidebar(main).IsEffectivelyVisible);
    }

    /// <summary>
    /// The control is drawn where its layout puts it and no row of the root still enters: nothing moves it any more, so a
    /// click by coordinate lands where the layout says.
    /// </summary>
    static bool AtRest(Control control, Control root) =>
        control.TranslatePoint(default, root) is { } drawn && LayoutAssert.X(control, root) is { } left && LayoutAssert.Y(control, root) is { } top
        && Math.Abs(drawn.X - left) < 0.5 && Math.Abs(drawn.Y - top) < 0.5
        && !root.GetVisualDescendants().OfType<Control>().Any(otherControl => otherControl.Classes.Contains("entering"));

    static ColumnDefinition SidebarColumn(MainWindow main) => ((Grid)Sidebar(main).Parent!).ColumnDefinitions[0];

    /// <summary>The header button whose text (or automation name) is the given resource text.</summary>
    static Button HeaderButton(MainWindow main, string text) => main.GetVisualDescendants().OfType<Button>()
        .Single(button => button.IsEffectivelyVisible && AutomationProperties.GetName(button) == text);

    static void FullyInside(MainWindow main, Control control)
    {
        var left = control.TranslatePoint(default, main)!.Value.X;
        Assert.True(left >= 0 && left + control.Bounds.Width <= main.Bounds.Width + 1,
            $"{AutomationProperties.GetName(control)} at {left}..{left + control.Bounds.Width}, window {main.Bounds.Width}");
    }

    static void Click(Window main, Control control)
    {
        var center = Rows.Center(control, main);
        main.MouseDown(center, MouseButton.Left);
        main.MouseUp(center, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    static Button MenuButton(MainWindow main) => main.GetVisualDescendants().OfType<Button>()
        .Single(button => button.IsEffectivelyVisible && AutomationProperties.GetName(button) == Strings.Ui_ShowChannels);

    /// <summary>
    /// Clicks the menu button once it is at rest and checks that the click opened the drawer. After connecting, the header
    /// slides in: a click at the centre computed mid-slide finds no button there, so on a busy machine it would miss.
    /// </summary>
    static void OpenDrawer(MainWindow main)
    {
        var button = MenuButton(main);
        int frames = 0; // at least one frame, so the input sees the button where it is drawn
        MotionWait.WaitFor(() => frames++ >= 1 && AtRest(button, main), 5000);
        Click(main, button);
        Assert.True(main.Classes.Contains("drawer"), $"the menu click at {Rows.Center(button, main)} did not open the drawer");
    }

    /// <summary>Bug from the screenshot: at 776 px a wide sidebar pushed Ping, Verwaltung and Trennen out of the window.</summary>
    /// <summary>Package 104 (AC6): the settings and every administration tab fit in the simplified display as well.</summary>
    [AvaloniaTheory]
    [InlineData(360)]
    [InlineData(1100)]
    public void SettingsAndAdmin_SimplifiedDisplay_Fit(double width)
    {
        display = DisplayMode.Simplified;
        new ClientSettings { Display = display }.Save(dir);
        Settings_At360To1100_Fit(width, "de-DE");
        Admin_EveryTab_Fits(width, "de-DE");
    }

    [AvaloniaFact]
    public void Sidebar_DraggedWide_HeaderButtonsStayVisible()
    {
        var main = Open(776, FakeServers.Admin());
        var splitter = main.GetVisualDescendants().OfType<GridSplitter>().Single();
        SidebarColumn(main).Width = new GridLength(600);
        splitter.RaiseEvent(new VectorEventArgs { RoutedEvent = Avalonia.Controls.Primitives.Thumb.DragCompletedEvent });
        Dispatcher.UIThread.RunJobs();

        Assert.True(Sidebar(main).Bounds.Width < 600, $"Seitenleiste {Sidebar(main).Bounds.Width}");
        Assert.True(Sidebar(main).Bounds.Width >= 200);
        FullyInside(main, HeaderButton(main, Strings.Ui_AdminMenu));
        FullyInside(main, HeaderButton(main, Strings.Ui_Disconnect));
        FullyInside(main, main.FindControl<Border>("PingChip")!);
        main.Close();
    }

    /// <summary>Package 139 (AC5): the countdown and "Abbrechen" fit on the start screen at 360 px.</summary>
    [AvaloniaTheory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void Reconnecting_At360_StatusAndCancelFit(string culture) => TestCulture.With(culture, () =>
    {
        var main = Open(360, null);
        var viewModel = (MainViewModel)main.DataContext!;
        viewModel.Status = TextFormat.Of(Strings.Status_Reconnecting, 16);
        viewModel.Connection.IsReconnecting = true;
        Dispatcher.UIThread.RunJobs();
        var status = main.FindControl<TextBlock>("StatusText")!;
        var cancel = main.FindControl<Button>("CancelReconnect")!;
        Assert.True(status.IsEffectivelyVisible && cancel.IsEffectivelyVisible);
        FullyInside(main, status);
        FullyInside(main, cancel);
        Assert.Equal(viewModel.Status, status.Text);
        main.Close();
        return 0;
    });

    [AvaloniaFact]
    public void WidthClasses_WindowResized_Follow()
    {
        var main = Open(1100, FakeServers.Admin());
        Assert.DoesNotContain("compact", main.Classes);
        Assert.DoesNotContain("narrow", main.Classes);
        Resize(main, 650);
        Assert.Contains("compact", main.Classes);
        Assert.DoesNotContain("narrow", main.Classes);
        Resize(main, 360);
        Assert.Contains("compact", main.Classes);
        Assert.Contains("narrow", main.Classes);
        Resize(main, 1100);
        Assert.DoesNotContain("compact", main.Classes);
        Assert.DoesNotContain("narrow", main.Classes);
        Assert.Equal(360, main.MinWidth);
        Assert.Equal(480, main.MinHeight);
        main.Close();
    }

    [AvaloniaFact]
    public void Compact_SidebarAsOverlay_OpenClose()
    {
        var main = Open(360, FakeServers.Admin());
        var viewModel = (MainViewModel)main.DataContext!;
        Assert.False(Sidebar(main).IsEffectivelyVisible);
        var scrim = main.FindControl<Border>("DrawerScrim")!;

        OpenDrawer(main);
        Assert.True(Sidebar(main).IsEffectivelyVisible);
        Assert.True(scrim.IsEffectivelyVisible);
        Assert.True(Sidebar(main).Bounds.Width <= 312, $"{Sidebar(main).Bounds.Width}");
        Assert.Equal(0, LayoutAssert.X(Sidebar(main), main)!.Value - LayoutAssert.X(main.GetVisualDescendants().OfType<Grid>().First(), main)!.Value, 1); // where it ends up, not mid-slide

        // double click on Raid joins it and closes the drawer, once the drawer has slid in and its rows have built up. Before
        // the first frame nothing has moved yet, so at least two frames pass; under load each step can outlast a whole slide.
        Border Raid() => Rows.ChannelRow(main, "Raid");
        int frames = 0;
        MotionWait.WaitFor(() => frames++ >= 2 && AtRest(Sidebar(main), main) && AtRest(Raid(), Sidebar(main)), 5000);
        var raid = Raid();
        var point = Rows.Center(raid, main);
        main.MouseDown(point, MouseButton.Left);
        main.MouseUp(point, MouseButton.Left);
        main.MouseDown(point, MouseButton.Left);
        main.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        AssertDrawerClosed(main);

        OpenDrawer(main);
        main.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();
        AssertDrawerClosed(main);
        Assert.True(viewModel.PageFlow.IsHomePage);

        OpenDrawer(main);
        var outside = new Point(main.Bounds.Width - 10, main.Bounds.Height / 2);
        main.MouseDown(outside, MouseButton.Left);
        main.MouseUp(outside, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        AssertDrawerClosed(main);

        OpenDrawer(main);
        viewModel.PageFlow.OpenSettings();
        Dispatcher.UIThread.RunJobs();
        AssertDrawerClosed(main);
        viewModel.PageFlow.CloseWithoutAsking();

        // wide again: the sidebar is back in its column
        OpenDrawer(main);
        Resize(main, 1100);
        Assert.True(Sidebar(main).IsEffectivelyVisible);
        Assert.False(scrim.IsEffectivelyVisible);
        Assert.True(SidebarColumn(main).ActualWidth >= 240);
        main.Close();
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Narrow_HeaderIconOnly_NothingClipped(bool admin)
    {
        var server = FakeServers.Admin();
        server.IsAdmin = admin; // not admin: "Admin-Token einlösen" joins the header
        var main = Open(360, server);
        var viewModel = (MainViewModel)main.DataContext!;
        viewModel.Connection.PingText = "23 ms";
        Dispatcher.UIThread.RunJobs();

        var buttons = new List<string> { Strings.Ui_AdminMenu, Strings.Ui_Disconnect };
        if (!admin) buttons.Add(Strings.Ui_RedeemTokenMenu);
        foreach (var text in buttons)
        {
            var button = HeaderButton(main, text);
            Assert.Equal(text, ToolTip.GetTip(button));
            Assert.DoesNotContain(button.GetVisualDescendants().OfType<TextBlock>(), textBlock => textBlock.IsEffectivelyVisible && textBlock.Text == text);
            FullyInside(main, button);
        }
        var ping = main.FindControl<Border>("PingChip")!;
        Assert.DoesNotContain(ping.GetVisualDescendants().OfType<TextBlock>(), textBlock => textBlock.IsEffectivelyVisible && textBlock.Text == "23 ms");
        // Package 133: the signal icon stays, crossed out while UDP is unreachable
        viewModel.Stats.Unreachable = true;
        Dispatcher.UIThread.RunJobs();
        FullyInside(main, main.FindControl<Button>("SignalButton")!);
        Assert.True(main.FindControl<Avalonia.Controls.Shapes.Line>("SignalCross")!.IsEffectivelyVisible);
        viewModel.Stats.Unreachable = false;
        viewModel.Stats.Update(TimeSpan.FromMilliseconds(20), default, default, default, 64); // the first sample is its own baseline: green
        Dispatcher.UIThread.RunJobs();
        var bars = main.FindControl<Button>("SignalButton")!.GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("signalBar")).ToList();
        Assert.Equal(4, bars.Count(border => border.Classes.Contains("on")));
        Assert.Contains("bars4", bars[0].GetVisualAncestors().OfType<Panel>().First(panel => panel.Classes.Contains("bars4") || panel.Classes.Contains("bars1")).Classes);
        Assert.False(main.FindControl<Avalonia.Controls.Shapes.Line>("SignalCross")!.IsEffectivelyVisible);
        LayoutAssert.FitsHorizontally(main);

        Resize(main, 1100); // wide: the texts are back
        Assert.Contains(HeaderButton(main, Strings.Ui_Disconnect).GetVisualDescendants().OfType<TextBlock>(),
            textBlock => textBlock.IsEffectivelyVisible && textBlock.Text == Strings.Ui_Disconnect);
        main.Close();
    }

    [AvaloniaFact]
    public void Sidebar_WindowShrinks_GivesWay()
    {
        var main = Open(1100, FakeServers.Admin());
        var splitter = main.GetVisualDescendants().OfType<GridSplitter>().Single();
        SidebarColumn(main).Width = new GridLength(350);
        splitter.RaiseEvent(new VectorEventArgs { RoutedEvent = Avalonia.Controls.Primitives.Thumb.DragCompletedEvent });
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(350, Sidebar(main).Bounds.Width, 1);

        Resize(main, 760);
        Assert.True(Sidebar(main).Bounds.Width >= 200);
        FullyInside(main, HeaderButton(main, Strings.Ui_AdminMenu));
        FullyInside(main, HeaderButton(main, Strings.Ui_Disconnect));
        LayoutAssert.FitsHorizontally(main);
        Resize(main, 1100); // the dragged width comes back
        Assert.Equal(350, Sidebar(main).Bounds.Width, 1);
        main.Close();
    }

    [AvaloniaFact]
    public void StartScreen_Widths_SameRules()
    {
        var settings = new ClientSettings();
        settings.Bookmarks.Add(new Bookmark("Gilde", "gilde.example.org", 7000, "ich"));
        settings.Save(dir);
        var main = Open(360, null);
        Assert.Contains("compact", main.Classes);
        Assert.False(Sidebar(main).IsEffectivelyVisible);
        LayoutAssert.FitsHorizontally(main);

        Click(main, MenuButton(main));
        Assert.True(Sidebar(main).IsEffectivelyVisible);
        Assert.Contains(Sidebar(main).GetVisualDescendants().OfType<TextBlock>(), textBlock => textBlock.IsEffectivelyVisible && textBlock.Text == "Gilde");
        main.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();
        AssertDrawerClosed(main);

        Resize(main, 1100);
        Assert.DoesNotContain("compact", main.Classes);
        Assert.True(Sidebar(main).IsEffectivelyVisible);
        Assert.DoesNotContain(main.GetVisualDescendants().OfType<Button>(), button => button.IsEffectivelyVisible && AutomationProperties.GetName(button) == Strings.Ui_ShowChannels);
        main.Close();
    }

    // ---- Package 77: the settings page at every width ----

    /// <summary>Settings with the worst case: a key row next to the push-to-talk hint (a key, but none for push-to-talk).</summary>
    DisplayMode display = DisplayMode.Animated; // Package 104: the layout checks run in both displays

    MainWindow OpenSettings(double width)
    {
        var settings = new ClientSettings { KeyBindings = [new OVS.Client.Input.KeyBinding(KeyAction.ToggleMute, new KeyChord(0x70))], Display = display };
        settings.Save(dir);
        var main = Open(width, null);
        var viewModel = (MainViewModel)main.DataContext!;
        Assert.Equal(display == DisplayMode.Animated, Motion.IsAnimated(main)); // the window took the display from the settings
        viewModel.PageFlow.OpenSettings();
        Dispatcher.UIThread.RunJobs();
        Assert.True(viewModel.PageFlow.SettingsPage!.ShowPttHint);
        return main;
    }

    [AvaloniaTheory]
    [InlineData(360, "de-DE")]
    [InlineData(480, "de-DE")]
    [InlineData(600, "de-DE")]
    [InlineData(1100, "de-DE")]
    [InlineData(360, "en-US")]
    [InlineData(480, "en-US")]
    [InlineData(600, "en-US")]
    [InlineData(1100, "en-US")]
    public void Settings_At360To1100_Fit(double width, string culture) => TestCulture.With(culture, () =>
    {
        var main = OpenSettings(width);
        var page = main.GetVisualDescendants().OfType<SettingsView>().Single();
        LayoutAssert.FitsHorizontally(main); // the list of pages, shown first when stacked
        ((SettingsViewModel)page.DataContext!).CheckForUpdates = !((SettingsViewModel)page.DataContext!).CheckForUpdates; // a change: the bar shows
        // Package 152: every page in turn, each scrolled through
        foreach (var settingsPage in SettingsPages.All(page))
        {
            SettingsPages.Open(page, (string)settingsPage.Header!);
            if ((string)settingsPage.Header! == Strings.Nav_Transmit)
            {
                // Package 129: voice activation shows the "Automatisch" box, its hint, the meter and the slider
                ((SettingsViewModel)page.DataContext!).VoiceActivation = true;
                Dispatcher.UIThread.RunJobs();
                var automatic = page.GetVisualDescendants().OfType<CheckBox>().Single(checkBox => checkBox.Content as string == Strings.Ui_VadAutomatic);
                Assert.True(automatic.IsEffectivelyVisible);
            }
            var scroller = ((Control)settingsPage.Content!).GetSelfAndVisualDescendants().OfType<ScrollViewer>().First();
            for (var scrolled = 0.0; ; scrolled += Math.Max(1, scroller.Viewport.Height))
            {
                scroller.Offset = new Vector(0, scrolled);
                Dispatcher.UIThread.RunJobs();
                LayoutAssert.FitsHorizontally(main);
                if (scrolled + scroller.Viewport.Height >= scroller.Extent.Height) break;
            }
            foreach (var row in page.GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("soundRow")))
            {
                var name = row.GetVisualDescendants().OfType<TextBlock>().First(textBlock => textBlock.Classes.Contains("label"));
                Assert.True(name.Bounds.Width > 60, $"Name {name.Text}: {name.Bounds.Width}");
            }

            // Package 153: with a change the unsaved-changes bar stays in view with both buttons
            foreach (var name in UnsavedButtons)
            {
                var button = page.GetVisualDescendants().OfType<Button>().Single(otherButton => otherButton.Name == name);
                Assert.True(button.IsEffectivelyVisible, name);
                var top = button.TranslatePoint(default, main)!.Value.Y;
                Assert.True(top >= 0 && top + button.Bounds.Height <= main.Bounds.Height + 1, $"{name} at {top}");
            }
        }
        main.Close();
        return 0;
    });

    static SoundRowParts FirstSoundRow(MainWindow main)
    {
        SettingsPages.Open(main, Strings.Nav_Sounds); // Package 152
        var row = main.GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("soundRow"));
        return new(row.GetVisualDescendants().OfType<TextBlock>().First(textBlock => textBlock.Classes.Contains("label")),
            row.GetVisualDescendants().OfType<Slider>().Single());
    }

    static VerticalExtent Vertical(Control control, Visual root)
    {
        var top = control.TranslatePoint(default, root)!.Value.Y;
        return new(top, top + control.Bounds.Height);
    }

    [AvaloniaTheory]
    [InlineData(360)]
    [InlineData(600)] // not narrow yet, but too tight for name and controls side by side
    public void SoundRow_Narrow_TwoLinesWithNameVisible(double width)
    {
        var main = OpenSettings(width);
        var (name, slider) = FirstSoundRow(main);
        Assert.True(name.Bounds.Width > 0);
        Assert.False(name.TextLayout.TextLines.Any(textLine => textLine.HasCollapsed), "Name abgeschnitten");
        Assert.True(Vertical(slider, main).Top >= Vertical(name, main).Bottom, "Regler nicht unter dem Namen");
        main.Close();
    }

    [AvaloniaFact]
    public void Settings_Wide_LayoutUnchanged()
    {
        var main = OpenSettings(1400); // Package 152: beside the page list the sound rows need a wider window for one line
        Assert.DoesNotContain("narrow", main.Classes);
        var (name, slider) = FirstSoundRow(main);
        var (top, bottom) = Vertical(slider, main);
        var middle = (Vertical(name, main).Top + Vertical(name, main).Bottom) / 2;
        Assert.True(middle > top - 12 && middle < bottom + 12, "Sound-Zeile nicht einzeilig");
        Assert.True(slider.TranslatePoint(default, main)!.Value.X >= name.TranslatePoint(default, main)!.Value.X + name.Bounds.Width - 1);
        Assert.Equal(150, slider.Bounds.Width, 1);
        main.Close();
    }

    // ---- Package 78: the administration at every width ----

    /// <summary>The administration with 8 channels, 20 known users and three bans (one with a long reason).</summary>
    MainWindow OpenAdmin(double width, out AdminView page)
    {
        var server = FakeServers.Crowded();
        var main = Open(width, server);
        var viewModel = (MainViewModel)main.DataContext!;
        _ = viewModel.PageFlow.OpenAdminAsync();
        Dispatcher.UIThread.RunJobs();
        var admin = (AdminViewModel)main.GetVisualDescendants().OfType<AdminView>().Single().DataContext!;
        var groups = server.Mirror.Groups.Select(groupInfo => groupInfo.Id).ToList();
        // Package 71: cards with every detail, long previous nicknames and an active ban
        admin.UsersAndBans.Apply(new OVS.Shared.Protocol.Messages.Users.UserList("r", Enumerable.Range(1, 20)
            .Select(index => new OVS.Shared.Protocol.Messages.Users.KnownUserInfo(new string((char)('a' + index % 6), 64), $"Mitspieler{index}", index % 3 == 0 ? groups : [groups[0]],
                DateTimeOffset.Now.AddDays(-40), DateTimeOffset.Now.AddHours(-index), index, TimeSpan.FromHours(30 + index), "2001:db8:85a3::8a2e:370:7334",
                ["Ein sehr langer früherer Nickname", "Noch ein Name", "Dritter"], TimeSpan.FromMinutes(95), 1234, index % 2 == 0, null,
                index % 4 == 0 ? [new OVS.Shared.Protocol.Messages.Moderation.BanInfo(Guid.NewGuid(), "x", "y", null, "Hat wiederholt den Raid-Channel mit Musik beschallt", "ich", null)] : null,
                index % 5 == 0, // Package 85: stored server mute with its banner and lift button
                CanBeModeratedByMe: index % 3 != 0)) // Package 92: as the server judges it, members of the Admin group are not weaker
            .ToList()));
        foreach (var user in admin.UsersAndBans.Users) user.IsExpanded = true; // Package 155: every row open, the widest case
        // Package 80: 12 bans of every status (active, expired, lifted, old without details), all shown
        var now = DateTimeOffset.Now;
        admin.UsersAndBans.Apply(new OVS.Shared.Protocol.Messages.Moderation.BanList("r", Enumerable.Range(0, 12).Select(index => new OVS.Shared.Protocol.Messages.Moderation.BanInfo(Guid.NewGuid(),
            new string((char)('a' + index % 6), 64), $"Störenfried mit langem Namen {index}", index % 2 == 0 ? "2001:db8:85a3::8a2e:370:7334" : null,
            index % 3 == 0 ? "Hat wiederholt den Raid-Channel mit Musik beschallt und Warnungen ignoriert" : "", "Moderator mit langem Namen",
            (index % 4) switch { 0 => null, 1 => now.AddDays(3), 2 => now.AddDays(-1), _ => now.AddHours(5) },
            index == 11 ? null : now.AddDays(-index - 2), index == 11 ? null : new string('f', 64), index % 4 is 1 or 3 ? 4320 : index % 4 == 2 ? 1440 : null,
            index % 4 == 3 ? now.AddHours(-1) : null, index % 4 == 3 ? "Administrator mit langem Namen" : null, index * 7,
            index % 2 == 1 ? now.AddMinutes(-index) : null, index % 2 == 1 ? "2001:db8:85a3::8a2e:370:7335" : null)).ToList()));
        admin.UsersAndBans.SelectedBanStatusFilter = admin.UsersAndBans.BanStatusFilters.Single(choice => choice.Value == BanStatusFilter.All);
        // Package 74: backups in the server tab, one of them the safety backup with the longer title
        admin.BackupCard.Apply(new OVS.Shared.Protocol.Messages.ServerBackups.BackupList("r",
        [
            new("vor-wiederherstellung_2026-09-30_12-00-00.ovsbackup", DateTimeOffset.Now, 12_345_678, "300926.1a2b"),
            new("2026-09-29_12-00-00.ovsbackup", DateTimeOffset.Now.AddDays(-1), 800_000, "dev.0000"),
            new("2026-09-28_12-00-00.ovsbackup", DateTimeOffset.Now.AddDays(-2), 900, "dev.0000"),
        ]));
        // Package 81: log files of the server and of channels with long names, one opened on a page with long lines
        var channel = server.Mirror.Channels.Values.First(channelInfo => channelInfo.Name.StartsWith("Raid", StringComparison.Ordinal)).Id;
        admin.Logs.Apply(new OVS.Shared.Protocol.Messages.LogAccess.LogList("r", Enumerable.Range(0, 12).Select(index => new OVS.Shared.Protocol.Messages.LogAccess.LogFileInfo(
            index % 3 == 0 ? $"server/2026-09-{30 - index:00}_12-00-00.log" : $"channels/{channel}/2026-09-{30 - index:00}_12-00-00.log",
            index % 3 == 0 ? OVS.Shared.Protocol.Messages.LogAccess.LogKind.Server : OVS.Shared.Protocol.Messages.LogAccess.LogKind.Channel, index % 3 == 0 ? null : channel,
            index % 3 == 0 ? null : index == 11 ? "Ein gelöschter Channel mit einem sehr langen Namen" : "Raidgruppe Nummer 2",
            now.AddDays(-index), now.AddDays(-index).AddHours(5), 1_234_567L * (index + 1))).ToList()));
        // Package 82: some files checked (the Server tab's transfer above shows here too)
        foreach (var file in admin.Logs.Files.Where((_, index) => index % 2 == 0)) file.IsSelected = true;
        admin.Logs.OpenCommand.Execute(admin.Logs.Files[0]);
        admin.Logs.Apply(new OVS.Shared.Protocol.Messages.LogAccess.LogPage(null, admin.Logs.Files[0].Info.Id, 3, 3, 2001, Enumerable.Range(2001, 40)
            .Select(count => count % 5 == 0 ? $"2026-09-30 12:00:{count % 60:00}.000 Mitspieler{count} verbunden (" + new string('a', 64) + ", 2001:db8:85a3::8a2e:370:7334) " + new string('x', 300)
                : $"2026-09-30 12:00:{count % 60:00}.000 Zeile {count}").ToList()));
        admin.Logs.LineFilter = "0";
        Dispatcher.UIThread.RunJobs();
        page = main.GetVisualDescendants().OfType<AdminView>().Single();
        return main;
    }


    [AvaloniaTheory]
    [InlineData(360, "de-DE")]
    [InlineData(480, "de-DE")]
    [InlineData(600, "de-DE")]
    [InlineData(1100, "de-DE")]
    [InlineData(360, "en-US")]
    [InlineData(480, "en-US")]
    [InlineData(600, "en-US")]
    [InlineData(1100, "en-US")]
    public void Admin_EveryTab_Fits(double width, string culture) => TestCulture.With(culture, () =>
    {
        var main = OpenAdmin(width, out var page);
        var admin = (AdminViewModel)page.DataContext!;
        // Package 75: nothing running, so neither progress shows, the backups' on the Server tab and the logs' on theirs
        foreach (var (header, progress) in new[] { (Strings.Ui_Backups, "BackupTransfer"), (Strings.Ui_Logs, "LogTransfer") })
        {
            AdminPages.Open(page, header);
            Assert.False(page.FindControl<Control>(progress)!.IsEffectivelyVisible, progress);
        }
        // Package 75: a transfer in progress, so its progress line is checked with the rest of the Server tab
        admin.Transfer.IsRunning = true;
        admin.Transfer.Percent = 42;
        admin.Transfer.ProgressText = TextFormat.Of(Strings.Backup_Uploading, 42);
        // Package 97: the waiting states, checked with the rest: a refresh, a first load, a failed refresh, busy buttons
        admin.UsersAndBans.UsersLoad.IsBusy = true;
        admin.UsersAndBans.BansLoad.HasSucceeded = false;
        admin.UsersAndBans.BansLoad.IsBusy = true;
        admin.Logs.FilesLoad.Error = Strings.Pending_NoAnswer;
        admin.BackupCard.BackupCreate.IsBusy = true;
        admin.Logs.Searching.IsBusy = true;
        Assert.Equal(12, admin.UsersAndBans.Bans.Count); // Package 80: every ban of every status is checked for width below
        Dispatcher.UIThread.RunJobs();
        var tabs = AdminPages.Tabs(page);
        LayoutAssert.FitsHorizontally(main); // Package 154: the list of pages, shown first when stacked
        foreach (var adminPage in AdminPages.All(page))
        {
            AdminPages.Open(page, adminPage);

            var content = tabs.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>().Single(contentPresenter => contentPresenter.Name == "PART_SelectedContentHost");
            var matrix = content.GetVisualDescendants().OfType<ScrollViewer>().SingleOrDefault(scrollViewer => scrollViewer.Name == "LinkCells");
            // scroll every scrolling list through, so the lower cards are checked too
            foreach (var scroller in content.GetVisualDescendants().OfType<ScrollViewer>().Where(scrollViewer => scrollViewer != matrix && scrollViewer.Viewport.Height > 0).ToList())
            {
                for (var scrolled = 0.0; ; scrolled += scroller.Viewport.Height)
                {
                    scroller.Offset = new Vector(0, scrolled);
                    Dispatcher.UIThread.RunJobs();
                    LayoutAssert.FitsHorizontally(page, matrix);
                    if (scrolled + scroller.Viewport.Height >= scroller.Extent.Height) break;
                }
            }
            LayoutAssert.FitsHorizontally(main, matrix);
        }
        // Package 69: the new server settings are part of the checked pages (Betrieb since Package 154)
        AdminPages.Open(page, Strings.Nav_Operation);
        foreach (var name in ServerSectionControls)
            Assert.True(page.FindControl<Control>(name)?.IsEffectivelyVisible, name);
        AdminPages.Open(page, Strings.Ui_Backups);
        // Package 74, 156: every backup with its "..." menu (restore, delete), all checked for width above
        Assert.Equal(3, page.GetVisualDescendants().OfType<Button>().Count(button => button.Classes.Contains("moreActions") && button.IsEffectivelyVisible));
        // Package 75: download per backup, both upload buttons and the progress
        Assert.Equal(3, page.GetVisualDescendants().OfType<TextBlock>().Count(textBlock => textBlock.Text == Strings.Ui_Download && textBlock.IsEffectivelyVisible));
        foreach (var name in BackupControls)
            Assert.True(page.FindControl<Control>(name)?.IsEffectivelyVisible, name);
        Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), textBlock => textBlock.Text == admin.Transfer.ProgressText && textBlock.IsEffectivelyVisible);
        // Package 97: "Backup anlegen" busy (checked above), then the whole card while restoring
        Assert.Contains(page.FindControl<Button>("NewBackupButton")!.GetVisualDescendants().OfType<BusySpinner>(), busySpinner => busySpinner.IsEffectivelyVisible);
        admin.BackupCard.Restoring.IsBusy = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(page.FindControl<Control>("RestoringState")?.IsEffectivelyVisible);
        LayoutAssert.FitsHorizontally(main);
        admin.BackupCard.Restoring.IsBusy = false;
        // Package 85: the stored server mute on four cards, the lift button where the viewer outranks the user (checked for width above)
        AdminPages.Open(page, Strings.Nav_Members);
        Assert.Equal(4, page.GetVisualDescendants().OfType<TextBlock>().Count(textBlock => textBlock.Text == Strings.Ui_ServerMuted && textBlock.IsEffectivelyVisible));
        Assert.Equal(3, page.GetVisualDescendants().OfType<TextBlock>().Count(textBlock => textBlock.Text == Strings.Ui_LiftServerMute && textBlock.IsEffectivelyVisible));
        Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), textBlock => textBlock.Text == Strings.Ui_Loading && textBlock.IsEffectivelyVisible); // Package 97: refreshing
        AdminPages.Open(page, Strings.Ui_Bans);
        Assert.Contains(page.FindControl<ContentControl>("BansLoadState")!.GetVisualDescendants().OfType<BusySpinner>(), busySpinner => busySpinner.IsEffectivelyVisible);
        // Package 81: the Logs tab with the opened file (checked above), then with the hits of a search instead of the files
        AdminPages.Open(page, Strings.Ui_Logs);
        foreach (var name in LogPageControls)
            Assert.True(page.FindControl<Control>(name)?.IsEffectivelyVisible, name);
        Assert.NotEmpty(page.FindControl<ListBox>("LogLines")!.GetRealizedContainers());
        admin.Logs.CloseFileCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        foreach (var name in LogSearchControls)
            Assert.True(page.FindControl<Control>(name)?.IsEffectivelyVisible, name);
        Assert.Contains(page.FindControl<ListBox>("LogFileList")!.GetVisualDescendants().OfType<CheckBox>(), checkBox => checkBox.IsEffectivelyVisible && checkBox.IsChecked == true);
        Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), textBlock => textBlock.Text == Strings.Pending_NoAnswer && textBlock.IsEffectivelyVisible); // Package 97
        LayoutAssert.FitsHorizontally(main);
        admin.Logs.Apply(new OVS.Shared.Protocol.Messages.LogAccess.LogSearchResult(null, admin.Logs.Files.Take(8).Select((logFile, index) =>
            new OVS.Shared.Protocol.Messages.LogAccess.LogHit(logFile.Info.Id, 1000 + index, "2026-09-30 12:00:00.000 [Raidgruppe Nummer 2] Mitspieler3: " + new string('y', 400))).ToList(), true, false));
        Dispatcher.UIThread.RunJobs();
        Assert.True(page.FindControl<Control>("LogHitsText")?.IsEffectivelyVisible);
        Assert.NotEmpty(page.FindControl<ListBox>("LogHitList")!.GetRealizedContainers());
        LayoutAssert.FitsHorizontally(main);
        main.Close();
        return 0;
    });

    /// <summary>Package 81 (AC8): the file list beside the opened file; under narrow one after the other, each on the whole page.</summary>
    [AvaloniaTheory]
    [InlineData(360, true)]
    [InlineData(1100, false)]
    public void Admin_Logs_StackedWhenNarrow(double width, bool stacked)
    {
        var main = OpenAdmin(width, out var page);
        AdminPages.Open(page, Strings.Ui_Logs);
        var list = page.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("logList"));
        var viewer = page.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("logViewer"));
        var lines = page.FindControl<ListBox>("LogLines")!;
        if (stacked)
        {
            // an opened file takes the whole page, closing it brings back the filters and the list above the viewer
            Assert.False(list.IsEffectivelyVisible);
            Assert.False(page.FindControl<Control>("LogSearch")!.IsEffectivelyVisible);
            Assert.True(lines.GetRealizedContainers().Count() >= 5, $"{lines.GetRealizedContainers().Count()} Zeilen sichtbar");
        }
        else
        {
            Assert.True(viewer.TranslatePoint(default, main)!.Value.X >= list.TranslatePoint(default, main)!.Value.X + list.Bounds.Width);
            Assert.Equal(300, list.Bounds.Width, 1);
        }
        // long lines wrap inside the viewer instead of widening it
        Assert.True(lines.Bounds.Width <= viewer.Bounds.Width);
        Assert.Contains(lines.GetVisualDescendants().OfType<TextBlock>(), textBlock => textBlock.IsEffectivelyVisible && textBlock.TextLayout.TextLines.Count > 1);
        var admin = (AdminViewModel)page.DataContext!;
        admin.Logs.CloseFileCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(list.IsEffectivelyVisible && page.FindControl<Control>("LogSearch")!.IsEffectivelyVisible);
        if (stacked) Assert.False(viewer.IsEffectivelyVisible);
        else Assert.True(viewer.IsEffectivelyVisible);
        main.Close();
    }

    [AvaloniaTheory]
    [InlineData(360, true)]
    [InlineData(1100, false)]
    public void Admin_Groups_StackedWhenNarrow(double width, bool stacked)
    {
        var main = OpenAdmin(width, out var page);
        AdminPages.Open(page, Strings.Nav_GroupsRights);
        var list = page.FindControl<ListBox>("GroupRows")!;
        var listCard = list.FindAncestorOfType<Border>()!;
        var editor = page.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("groupEditor"));
        var grid = (Grid)listCard.Parent!;
        var rights = editor.GetVisualDescendants().OfType<CheckBox>().Where(checkBox => checkBox.IsEffectivelyVisible).ToList();
        Assert.True(rights.Count > 2);
        var columns = rights.Select(checkBox => Math.Round(checkBox.TranslatePoint(default, main)!.Value.X)).Distinct().Count();
        if (stacked)
        {
            Assert.True(Vertical(editor, main).Top >= Vertical(listCard, main).Bottom - 1, "Editor nicht unter der Liste");
            Assert.True(listCard.Bounds.Height <= grid.Bounds.Height * 0.4 + 1, $"Liste {listCard.Bounds.Height} von {grid.Bounds.Height}");
            Assert.Equal(1, columns);
        }
        else
        {
            Assert.True(editor.TranslatePoint(default, main)!.Value.X >= listCard.TranslatePoint(default, main)!.Value.X + listCard.Bounds.Width);
            Assert.Equal(240, listCard.Bounds.Width, 1);
            Assert.Equal(2, columns);
        }
        main.Close();
    }

    [AvaloniaFact]
    public void Admin_ServerSettings_RefusedNameShowsTheErrorUntilReload()
    {
        var main = OpenAdmin(1100, out var page);
        try
        {
            AdminPages.Open(page, Strings.Nav_Overview);
            var card = ((AdminViewModel)page.DataContext!).ServerSettings;
            var error = page.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Classes.Contains("danger") && text.DataContext == card);
            Assert.False(error.IsEffectivelyVisible);

            card.Name = "";
            card.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(error.IsEffectivelyVisible);
            Assert.Equal(Strings.Dlg_NameInvalid, error.Text);

            card.Reload();
            Dispatcher.UIThread.RunJobs();
            Assert.False(error.IsEffectivelyVisible);
        }
        finally
        {
            main.Close();
        }
    }

    [AvaloniaFact]
    public void Admin_LinkMatrix_ScrollsTitlesStay()
    {
        var main = OpenAdmin(360, out var page);
        AdminPages.Open(page, Strings.Nav_ChannelLinks);
        var cells = page.FindControl<ScrollViewer>("LinkCells")!;
        Assert.True(cells.Extent.Width > cells.Viewport.Width + 1, $"Matrix {cells.Extent.Width} passt in {cells.Viewport.Width}");
        var title = page.GetVisualDescendants().OfType<TextBlock>().First(textBlock => textBlock.Classes.Contains("linkTitle"));
        var before = title.TranslatePoint(default, main)!.Value;
        var lastCell = page.GetVisualDescendants().OfType<Border>().Last(border => border.Classes.Contains("linkCell"));
        Assert.True(lastCell.TranslatePoint(default, cells)!.Value.X > cells.Viewport.Width);

        cells.Offset = new Vector(cells.Extent.Width, 0);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(before, title.TranslatePoint(default, main)!.Value);
        var left = lastCell.TranslatePoint(default, cells)!.Value.X;
        Assert.True(left >= 0 && left + lastCell.Bounds.Width <= cells.Viewport.Width + 1, $"letzte Zelle bei {left}");
        // title and cells of one row stay on one line
        Assert.Equal(Vertical(title, main).Top + title.Bounds.Height / 2,
            Vertical(page.GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("linkCell")), main).Top + 16, tolerance: 2);
        LayoutAssert.FitsHorizontally(page, cells);
        main.Close();
    }

    // ---- Package 79: chat, start screen, update card and dialogs at every width ----

    /// <summary>Every dialog of <c>UiSmokeTests.ExercisePagesAndDialogs</c>, plus the password and bookmark dialogs.</summary>
    static Task OpenDialogAsync(string name, OverlayHost overlayHost, ClientSettings settings, ServerViewModel server) => name switch
    {
        "Connect" => ConnectDialog.ShowAsync(overlayHost, settings.Bookmarks),
        "TofuMismatch" => TofuDialog.ShowAsync(overlayHost, new OVS.Client.Net.TofuPrompt("voice.example.org", 7000, new string('a', 64), OVS.Client.Net.TofuResult.Mismatch)),
        "TofuUnknown" => TofuDialog.ShowAsync(overlayHost, new OVS.Client.Net.TofuPrompt("voice.example.org", 7000, new string('a', 64), OVS.Client.Net.TofuResult.Unknown)),
        "Ban" => BanDialog.ShowAsync(overlayHost, "Mitspieler mit langem Namen"),
        "BanWithoutIp" => BanDialog.ShowAsync(overlayHost, "Mitspieler mit langem Namen", ipKnown: false),
        "DeleteUser" => DeleteUserDialog.ShowAsync(overlayHost, "Mitspieler mit langem Namen"),
        "Confirm" => ConfirmDialog.ShowAsync(overlayHost, "Channel \"Raidgruppe Nummer 2\" wirklich löschen? Alle darin landen in der Lobby."),
        "Update" => UpdateOfferDialog.ShowAsync(overlayHost, new OVS.Client.Net.Updates.UpdateOffer("280926.0b2c", "deploy-bbbbbbb",
            string.Join("\n", Enumerable.Range(1, 30).Select(index => $"- Neuerung Nummer {index} mit etwas mehr Text")), DateTimeOffset.UtcNow,
            new Uri("https://example.org/a"), new Uri("https://example.org/b"))),
        "KeyBinding" => KeyBindingDialog.ShowAsync(overlayHost, null, _ => Task.FromResult<KeyChord?>(null)),
        "ChannelCreate" => ChannelDialogView.ShowAsync(overlayHost, new ChannelEdit("", ""), ChannelDialogMode.Create),
        "ChannelEdit" => ChannelDialogView.ShowAsync(overlayHost, new ChannelEdit("Raid", "Donnerstags ab 20 Uhr", IsMuted: true, MaxUsers: 8,
            AllowedGroupIds: [server.Mirror.Groups[2].Id]), ChannelDialogMode.Edit, server.Mirror.Groups), // Package 93: with its groups
        "PickChannel" => ChannelPickerDialog.ShowAsync(overlayHost, "Verschieben nach", server.Tree.Channels),
        "AskText" => TextDialog.ShowAsync(overlayHost, Strings.Dialog_RedeemToken, "Token:"),
        "Password" => ServerPasswordDialog.ShowAsync(overlayHost, "Gilde"),
        "ChannelPassword" => ChannelPasswordDialog.ShowAsync(overlayHost, "Raidgruppe am Donnerstagabend"), // Package 94
        "Bookmark" => BookmarkDialog.ShowAsync(overlayHost, settings.Bookmarks[0]),
        "BackupDownload" => BackupDownloadDialog.ShowAsync(overlayHost), // Package 89
        "Leave" => LeaveDialog.ShowAsync(overlayHost), // Package 114: three buttons
        "ConnectionDetails" => ConnectionDetailsDialog.ShowAsync(overlayHost, Measured()), // Package 133
        _ => throw new ArgumentException(name),
    };

    /// <summary>Package 133: connection details with long figures.</summary>
    static ConnectionStatsViewModel Measured()
    {
        var stats = ConnectionStats.CreateBlank();
        stats.Update(TimeSpan.FromMilliseconds(1234), new OVS.Shared.Voice.LossReport(250, 25), new OVS.Shared.Voice.LossReport(4999, 1234),
            new OVS.Client.Audio.Playback.ReceiveStats(100_000, 1000, 500, 12_345, 123.4, 200), 128);
        return stats;
    }

    /// <summary>Three bookmarks, so the start screen and the connect dialog show their lists.</summary>
    void SaveThreeBookmarks()
    {
        var settings = new ClientSettings();
        foreach (var name in new[] { "Gilde", "Raidgruppe am Donnerstagabend", "Freunde" })
    #pragma warning disable CA1308 // test data: host names in lower case, as people type them
        settings.Bookmarks.Add(new Bookmark(name, $"{name.ToLowerInvariant().Replace(' ', '-')}.example.org", 7000, "ich"));
#pragma warning restore CA1308
        settings.Save(dir);
    }

    static Border DialogCard(MainWindow main) => main.Overlay.GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("dialog"));

    /// <summary>At most as wide as before, at least 16 px from every window edge.</summary>
    static void CardInside(MainWindow main, Border card, double maxWidth)
    {
        var at = new Point(LayoutAssert.X(card, main)!.Value, LayoutAssert.Y(card, main)!.Value); // Package 105: not where the pop draws it
        Assert.True(card.Bounds.Width <= maxWidth + 0.5, $"Karte {card.Bounds.Width}");
        Assert.True(at.X >= 16 - 0.5 && at.X + card.Bounds.Width <= main.Bounds.Width - 16 + 0.5, $"Karte bei {at.X}..{at.X + card.Bounds.Width}, Fenster {main.Bounds.Width}");
        Assert.True(at.Y >= 16 - 0.5 && at.Y + card.Bounds.Height <= main.Bounds.Height - 16 + 0.5, $"Karte bei y {at.Y}..{at.Y + card.Bounds.Height}, Fenster {main.Bounds.Height}");
    }

    public static TheoryData<string, string> Dialogs()
    {
        var data = new TheoryData<string, string>();
        foreach (var dialog in new[] { "Connect", "TofuMismatch", "TofuUnknown", "Ban", "BanWithoutIp", "DeleteUser", "Confirm", "Update", "KeyBinding", "ChannelCreate", "ChannelEdit", "PickChannel", "AskText", "Password", "ChannelPassword", "Bookmark", "BackupDownload", "Leave", "ConnectionDetails" })
            foreach (var culture in new[] { "de-DE", "en-US" })
                data.Add(dialog, culture);
        return data;
    }

    [AvaloniaTheory]
    [MemberData(nameof(Dialogs))]
    public void Dialogs_At360_Fit(string dialog, string culture) => TestCulture.With(culture, () =>
    {
        SaveThreeBookmarks();
        var main = Open(1100, FakeServers.Crowded());
        var viewModel = (MainViewModel)main.DataContext!;
        foreach (var width in new[] { 360.0, 480, 1100 })
        {
            Resize(main, width);
            var shown = OpenDialogAsync(dialog, main.Overlay, viewModel.Settings, viewModel.Server!);
            Dispatcher.UIThread.RunJobs();
            Assert.True(main.Overlay.IsOpen);
            CardInside(main, DialogCard(main), 460);
            LayoutAssert.FitsHorizontally(main.Overlay);
            main.Overlay.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.True(shown.IsCompleted);
        }
        main.Close();
        return 0;
    });

    /// <summary>Package 119 (AC5): the user menu and its "Gruppen" submenu stay inside the window, also at 360 px.</summary>
    [AvaloniaTheory]
    [InlineData(360, "de-DE")]
    [InlineData(360, "en-US")]
    [InlineData(1100, "de-DE")]
    [InlineData(1100, "en-US")]
    public void UserContextMenu_At360_Fits(double width, string culture) => TestCulture.With(culture, () =>
    {
        var main = Open(width, FakeServers.Admin());
        if (width < 700)
        {
            OpenDrawer(main); // the channel tree lives in the drawer
            MotionWait.WaitFor(() => Sidebar(main).IsEffectivelyVisible && AtRest(Sidebar(main), main), 5000);
        }
        var row = main.GetVisualDescendants().OfType<Border>().Single(border => border.ContextMenu is not null && border.DataContext is UserViewModel { Nickname: "anna" });
        var menu = row.ContextMenu!;
        menu.Open(row);
        Dispatcher.UIThread.RunJobs();
        var groups = menu.Items.OfType<MenuItem>().Single(menuItem => menuItem.Header as string == Strings.Ui_Groups);
        groups.IsSubMenuOpen = true;
        Dispatcher.UIThread.RunJobs();
        var items = menu.Items.OfType<MenuItem>().Where(menuItem => menuItem.IsVisible).Concat(Avalonia.LogicalTree.LogicalExtensions.GetLogicalChildren(groups).OfType<MenuItem>()).ToList();
        Assert.Equal(2, items.Count(menuItem => menuItem.ToggleType == MenuItemToggleType.CheckBox));
        Assert.All(items, item => Assert.True(item.Bounds.Width > 0, "laid out"));
        foreach (var item in items) FullyInside(main, item);
        groups.IsSubMenuOpen = false;

        // Package 120 (AC7): the "Verschieben" submenu with the channels
        var move = menu.Items.OfType<MenuItem>().Single(menuItem => menuItem.Header as string == Strings.Ui_MoveMenu);
        move.IsSubMenuOpen = true;
        Dispatcher.UIThread.RunJobs();
        var channels = Avalonia.LogicalTree.LogicalExtensions.GetLogicalChildren(move).OfType<MenuItem>().ToList();
        Assert.Equal(["Lobby"], channels.Select(menuItem => menuItem.Header as string));
        Assert.All(channels, item => Assert.True(item.Bounds.Width > 0, "laid out"));
        foreach (var item in channels) FullyInside(main, item);
        menu.Close();
        main.Close();
        return 0;
    });

    [AvaloniaTheory]
    [InlineData("Connect")]
    [InlineData("ChannelEdit")]
    [InlineData("Update")]
    public void Dialog_TallerThanWindow_ContentScrollsButtonsVisible(string dialog)
    {
        SaveThreeBookmarks();
        var main = Open(360, FakeServers.Crowded());
        var viewModel = (MainViewModel)main.DataContext!;
        main.Height = 480;
        Dispatcher.UIThread.RunJobs();
        _ = OpenDialogAsync(dialog, main.Overlay, viewModel.Settings, viewModel.Server!);
        Dispatcher.UIThread.RunJobs();
        var card = DialogCard(main);
        CardInside(main, card, 460);
        var content = card.GetVisualDescendants().OfType<ScrollViewer>().First();
        Assert.True(content.Extent.Height > content.Viewport.Height + 1, $"Inhalt {content.Extent.Height} passt in {content.Viewport.Height}");
        foreach (var button in card.GetVisualDescendants().OfType<Button>().Where(button => button.Content is string && button.FindAncestorOfType<ScrollViewer>() is null))
        {
            var (top, bottom) = Vertical(button, main);
            Assert.True(top >= 0 && bottom <= main.Bounds.Height, $"{button.Content} bei {top}..{bottom}");
        }
        Assert.Equal(2, card.GetVisualDescendants().OfType<Button>().Count(button => button.Content is string && button.FindAncestorOfType<ScrollViewer>() is null));
        // scrolled to the end, the last field is reachable
        content.Offset = new Vector(0, content.Extent.Height);
        Dispatcher.UIThread.RunJobs();
        LayoutAssert.FitsHorizontally(main.Overlay);
        main.Overlay.Close();
        Dispatcher.UIThread.RunJobs();
        main.Close();
    }

    [AvaloniaTheory]
    [InlineData(360, "de-DE")]
    [InlineData(480, "de-DE")]
    [InlineData(1100, "de-DE")]
    [InlineData(360, "en-US")]
    [InlineData(480, "en-US")]
    [InlineData(1100, "en-US")]
    public void UpdateCard_Shown_Fits(double width, string culture) => TestCulture.With(culture, () =>
    {
        var main = Open(width, FakeServers.Admin());
        var viewModel = (MainViewModel)main.DataContext!;
        viewModel.Update.Progress = new OVS.Client.Net.Updates.UpdateProgress("290926.0lh6", OVS.Client.Net.Updates.UpdatePhase.Downloading, 20 * 1024 * 1024, 50 * 1024 * 1024);
        Dispatcher.UIThread.RunJobs();
        var layer = main.FindControl<Panel>("UpdateLayer")!;
        CardInside(main, layer.Children.OfType<Border>().Single(), 420);
        LayoutAssert.FitsHorizontally(layer);
        main.Close();
        return 0;
    });

    [AvaloniaTheory]
    [InlineData(360, "de-DE")]
    [InlineData(480, "de-DE")]
    [InlineData(1100, "de-DE")]
    [InlineData(360, "en-US")]
    [InlineData(480, "en-US")]
    [InlineData(1100, "en-US")]
    public void Chat_Narrow_TabsScrollAndSendVisible(double width, string culture) => TestCulture.With(culture, () =>
    {
        var server = FakeServers.Crowded();
        var main = Open(width, server);
        var viewModel = (MainViewModel)main.DataContext!;
        var text = string.Join(" ", Enumerable.Repeat("Eine recht lange Nachricht, die umbrechen muss.", 12));
        server.Apply(new OVS.Shared.Protocol.Messages.Chat.ChatMessage(OVS.Shared.Protocol.Messages.Chat.ChatTarget.Server, 3, "Mitspieler3", null, null, text, DateTimeOffset.Now));
        server.Apply(new OVS.Shared.Protocol.Messages.Chat.ChatMessage(OVS.Shared.Protocol.Messages.Chat.ChatTarget.Server, 1, "ich", null, null, new string('y', 400), DateTimeOffset.Now));
        server.Apply(new OVS.Shared.Protocol.Messages.Chat.ChatMessage(OVS.Shared.Protocol.Messages.Chat.ChatTarget.Server, 4, "Mitspieler4", null, null, new string('x', 400), DateTimeOffset.Now));
        for (var index = 5; index <= 10; index++) viewModel.Chat!.OpenPrivate($"fp{index}");
        viewModel.Chat!.Selected = viewModel.Chat.Tabs[0];
        Dispatcher.UIThread.RunJobs();

        var chat = main.GetVisualDescendants().OfType<ChatView>().Single();
        var strip = chat.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.TabStrip>().Single();
        var tabs = strip.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.TabStripItem>().ToList();
        Assert.Equal(viewModel.Chat.Tabs.Count, tabs.Count);
        Assert.Single(tabs.Select(tabStripItem => Math.Round(tabStripItem.TranslatePoint(default, main)!.Value.Y)).Distinct()); // one line
        var scroller = strip.FindAncestorOfType<ScrollViewer>()!;
        if (width < 1100) Assert.True(scroller.Extent.Width > scroller.Viewport.Width + 1, "Tab-Leiste scrollt nicht");

        // the last tab, once chosen, is in view
        viewModel.Chat.Selected = viewModel.Chat.Tabs[^1];
        Dispatcher.UIThread.RunJobs();
        var last = tabs[^1];
        var left = last.TranslatePoint(default, scroller)!.Value.X;
        Assert.True(left >= -1 && left + last.Bounds.Width <= scroller.Viewport.Width + 1, $"letzter Tab bei {left}");
        viewModel.Chat.Selected = viewModel.Chat.Tabs[0];
        Dispatcher.UIThread.RunJobs();
        Assert.True(tabs[0].TranslatePoint(default, scroller)!.Value.X >= -1, $"erster Tab bei {tabs[0].TranslatePoint(default, scroller)!.Value.X}");

        var send = chat.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button) == Strings.Ui_Send);
        FullyInside(main, send);
        var history = chat.GetVisualDescendants().OfType<ItemsControl>().Single(itemsControl => itemsControl is not Avalonia.Controls.Primitives.TabStrip);
        var share = main.Classes.Contains("narrow") ? 0.85 : 0.75;
        foreach (var bubble in history.GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("bubble") && border.IsEffectivelyVisible))
            Assert.True(bubble.Bounds.Width <= history.Bounds.Width * share + 1, $"Blase {bubble.Bounds.Width} von {history.Bounds.Width}");
        if (main.Classes.Contains("narrow"))
        {
            var own = history.GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("bubble") && border.Classes.Contains("own") && border.IsEffectivelyVisible);
            Assert.True(own.Bounds.Width > history.Bounds.Width * 0.8, $"eigene Blase nur {own.Bounds.Width} von {history.Bounds.Width}");
            Assert.Equal(12, LayoutAssert.X(history, chat)!.Value, 1);
        }
        LayoutAssert.FitsHorizontally(chat, strip);
        main.Close();
        return 0;
    });

    /// <summary>Package 98 (AC7): the waiting states outside the administration: busy rows, a sent and a failed message, a busy dialog and settings.</summary>
    [AvaloniaTheory]
    [InlineData(360, "de-DE")]
    [InlineData(1100, "de-DE")]
    [InlineData(360, "en-US")]
    [InlineData(1100, "en-US")]
    public void WaitingStates_Busy_Fit(double width, string culture) => TestCulture.With(culture, () =>
    {
        var time = new ManualTimeProvider();
        var viewModel = MainViewModelSetup.Create(dir, action => action());
        var main = new MainWindow { DataContext = viewModel, Width = width, Height = 700 };
        main.Show();
        ServerViewModel? server = null;
        server = FakeServers.Admin(time: time, dialogs: OverlayDialogs.Create(main.Overlay, () => server?.Mirror.Groups).Server);
        MainViewModelSetup.ShowServer(viewModel, server);
        // the connect sequence (at most 700 ms) is over before anything is clicked; the server's clock is a manual one
        MotionWait.WaitFor(() => false, 800);
        MotionWait.WaitFor(() => main.Classes.Contains("compact") == width < 700, 5000);

        // a sent message still waiting and a refused one with its retry, both long
        foreach (var text in new[] { new string('w', 300), "Diese Nachricht kam nicht an, weil zu viele in kurzer Zeit geschickt wurden." })
        {
            viewModel.Chat!.Draft = text;
            _ = viewModel.Chat.SendCommand.ExecuteAsync(null);
        }
        server.Apply(new OVS.Shared.Protocol.Messages.Connection.Error(viewModel.Chat!.General.Entries[^1].Sending!.Pending.RequestId, OVS.Shared.Protocol.ReasonCodes.RateLimited));
        // rows that spin: joining a channel, a server mute
        _ = server.Tree.Channels.Single(channel => channel.Name == "Raid").JoinCommand.ExecuteAsync(null);
        _ = server.Tree.Channels.SelectMany(channel => channel.Users).Single(user => user.Nickname == "anna").ToggleServerMuteCommand.ExecuteAsync(null);
        time.Advance(PendingActionViewModel.ShowAfter);
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(main.GetVisualDescendants().OfType<TextBlock>(), textBlock => textBlock.Text == Strings.Chat_NotSent && textBlock.IsEffectivelyVisible);
        LayoutAssert.FitsHorizontally(main);
        if (width < 700) OpenDrawer(main); // the channel tree lives in the drawer (a missed click fails there, not as "0 spinners")
        else Assert.DoesNotContain("drawer", main.Classes);
        // the drawer slides in (Package 105); on a busy machine that can take longer than its 220 ms
        MotionWait.WaitFor(() => Sidebar(main).GetVisualDescendants().OfType<BusySpinner>().Count(busySpinner => busySpinner.IsEffectivelyVisible) == 2, 5000);
        Assert.Equal(2, Sidebar(main).GetVisualDescendants().OfType<BusySpinner>().Count(busySpinner => busySpinner.IsEffectivelyVisible));
        LayoutAssert.FitsHorizontally(main);
        if (width < 700) Click(main, MenuButton(main));

        // a sending dialog with its busy button and the error of an earlier try
        _ = server.NewChannelCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        main.Overlay.GetVisualDescendants().OfType<TextBox>().First().Text = "Neu";
        var create = main.Overlay.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == Strings.Dlg_Create);
        create.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        time.Advance(TimeSpan.FromSeconds(10));
        create.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        time.Advance(PendingActionViewModel.ShowAfter);
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(main.Overlay.GetVisualDescendants().OfType<BusySpinner>(), busySpinner => busySpinner.IsEffectivelyVisible);
        CardInside(main, DialogCard(main), 460);
        LayoutAssert.FitsHorizontally(main.Overlay);
        main.Overlay.Close();
        Dispatcher.UIThread.RunJobs();

        // the settings: saving while the devices restart, the update check
        var restart = new TaskCompletionSource();
        var check = new TaskCompletionSource<string>();
        viewModel.PageFlow.ShownPage = new SettingsViewModel(viewModel.Settings, [], [], time: time) { ApplySaved = () => restart.Task, CheckNow = () => check.Task };
        viewModel.PageFlow.Page = Page.Settings;
        _ = viewModel.PageFlow.SettingsPage!.SaveAndCloseCommand.ExecuteAsync(null);
        _ = viewModel.PageFlow.SettingsPage!.CheckUpdatesCommand.ExecuteAsync(null);
        time.Advance(PendingActionViewModel.ShowAfter);
        Dispatcher.UIThread.RunJobs();
        var settings = main.GetVisualDescendants().OfType<SettingsView>().Single();
        Assert.True(settings.FindControl<Control>("UpdateCheckBusy")?.IsEffectivelyVisible);
        Assert.Contains(settings.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "UnsavedSave").GetVisualDescendants().OfType<BusySpinner>(), busySpinner => busySpinner.IsEffectivelyVisible);
        foreach (var scroller in settings.GetVisualDescendants().OfType<ScrollViewer>().Where(scrollViewer => scrollViewer.Viewport.Height > 0).ToList())
        {
            scroller.Offset = new Vector(0, scroller.Extent.Height);
            Dispatcher.UIThread.RunJobs();
        }
        LayoutAssert.FitsHorizontally(main);
        restart.SetResult(); // nothing may be left waiting when the test ends
        check.SetResult("");
        Dispatcher.UIThread.RunJobs();
        main.Close();
        return 0;
    });

    [AvaloniaTheory]
    [InlineData(360, "de-DE")]
    [InlineData(480, "de-DE")]
    [InlineData(1100, "de-DE")]
    [InlineData(360, "en-US")]
    [InlineData(480, "en-US")]
    [InlineData(1100, "en-US")]
    public void StartScreen_At360_Fits(double width, string culture) => TestCulture.With(culture, () =>
    {
        SaveThreeBookmarks();
        var main = Open(width, null);
        var viewModel = (MainViewModel)main.DataContext!;
        viewModel.Status = "Verbindung zu raidgruppe-am-donnerstagabend.example.org:7000 fehlgeschlagen: Zeitüberschreitung";
        Dispatcher.UIThread.RunJobs();
        var start = main.GetVisualDescendants().OfType<StackPanel>().Single(stackPanel => stackPanel.Classes.Contains("startScreen"));
        Assert.Equal(width < 700 ? 16 : 32, start.Margin.Left);
        LayoutAssert.FitsHorizontally(main);
        if (width < 700)
        {
            OpenDrawer(main); // the bookmarks live in the drawer
            LayoutAssert.FitsHorizontally(main);
            Assert.Contains(Sidebar(main).GetVisualDescendants().OfType<TextBlock>(), textBlock => textBlock.IsEffectivelyVisible && textBlock.Text == "Freunde");
        }
        main.Close();
        return 0;
    });

    // ---- Package 113: the fitted sidebar covers its footer ----

    const string LongNick = "Hauptmann Langname der Dritte";

    static ServerViewModel ShortChannelsLongNick()
    {
        var server = FakeServers.WithChannels("A", "B");
        var self = server.Mirror.Users[server.Mirror.SelfId];
        server.Apply(new UserUpdated(self with { Nickname = LongNick }));
        return server;
    }

    static TextBlock Footer(MainWindow main, string name) => main.FindControl<TextBlock>(name)!;

    /// <summary>The text block shows its whole text: its untrimmed width fits its bounds.</summary>
    static void Untrimmed(TextBlock text)
    {
        using var layout = new Avalonia.Media.TextFormatting.TextLayout(text.Text, new Avalonia.Media.Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch),
            text.FontSize, null);
        Assert.True(layout.WidthIncludingTrailingWhitespace <= text.Bounds.Width + 0.5, $"{text.Name} {text.Text}: {layout.WidthIncludingTrailingWhitespace} in {text.Bounds.Width}");
    }

    [AvaloniaFact]
    public void Connected_LongNickname_FooterNotTrimmed()
    {
        var main = Open(1100, ShortChannelsLongNick());
        MotionWait.WaitFor(() => false, 200);
        Assert.Equal(LongNick, Footer(main, "SelfName").Text);
        Untrimmed(Footer(main, "SelfName"));
        Untrimmed(Footer(main, "TalkHintLine"));
        Assert.True(Sidebar(main).Bounds.Width > SidebarWidth.Minimum, "wider than the rows alone need");
        main.Close();
    }

    [AvaloniaFact]
    public void SendButton_Sending_WidthStays()
    {
        var main = Open(1100, FakeServers.WithChannels("A", "B"));
        var viewModel = (MainViewModel)main.DataContext!;
        MotionWait.WaitFor(() => false, 200);
        var before = Sidebar(main).Bounds.Width;
        viewModel.TransmitText = Strings.Transmit_Linked;
        MotionWait.WaitFor(() => false, 200);
        Assert.Equal(before, Sidebar(main).Bounds.Width, 1);
        main.Close();
    }

    /// <summary>
    /// The case from the request: short channel names and a short nickname gave the 240 px minimum, and the hint
    /// "Sprachaktivierung" was cut. Every transmit mode refits, so each hint shows whole.
    /// </summary>
    [AvaloniaFact]
    public void Settings_TransmitModeChanged_Refits()
    {
        var main = Open(1100, FakeServers.WithChannels("A", "B"));
        var viewModel = (MainViewModel)main.DataContext!;
        viewModel.ApplySettings(new ClientSettings { Mode = OVS.Client.Audio.Transmit.TransmitMode.VoiceActivation });
        MotionWait.WaitFor(() => false, 200);
        Assert.Equal("ich", Footer(main, "SelfName").Text);
        Assert.Equal(Strings.TalkHint_VoiceActivation, Footer(main, "TalkHintLine").Text);
        Untrimmed(Footer(main, "TalkHintLine"));
        Assert.True(Sidebar(main).Bounds.Width > SidebarWidth.Minimum, $"{Sidebar(main).Bounds.Width}: the footer, not the rows, sets the width");

        viewModel.ApplySettings(new ClientSettings { KeyBindings = [] }); // push-to-talk without a key: the hint button
        MotionWait.WaitFor(() => false, 200);
        var hint = main.FindControl<Button>("PttHint")!;
        Assert.True(hint.IsEffectivelyVisible);
        Untrimmed(hint.GetVisualDescendants().OfType<TextBlock>().Single());
        Assert.True(hint.Bounds.Width >= hint.DesiredSize.Width - 0.5, "the whole button shows");

        viewModel.ApplySettings(new ClientSettings { KeyBindings = [new OVS.Client.Input.KeyBinding(KeyAction.PushToTalk, new KeyChord(0x05))] });
        MotionWait.WaitFor(() => false, 200);
        Untrimmed(Footer(main, "TalkHintLine"));
        main.Close();
    }

    [AvaloniaFact]
    public void SidebarMax_NarrowWindow_StillWins()
    {
        var main = Open(760, ShortChannelsLongNick());
        MotionWait.WaitFor(() => false, 200);
        var max = Responsive.SidebarMax(main.FindControl<Grid>("Shell")!.Bounds.Width, main.GetVisualDescendants().OfType<GridSplitter>().Single().Width,
            0);
        Assert.True(Sidebar(main).Bounds.Width <= max + 1);
        LayoutAssert.FitsHorizontally(main);
        main.Close();
    }

    [AvaloniaFact]
    public void SidebarDrag_TowardsFooter_StopsAbove()
    {
        var main = Open(1100, ShortChannelsLongNick());
        MotionWait.WaitFor(() => false, 200);
        var fitted = Sidebar(main).Bounds.Width;
        var splitter = main.GetVisualDescendants().OfType<GridSplitter>().Single();
        SidebarColumn(main).Width = new GridLength(150);
        splitter.RaiseEvent(new VectorEventArgs { RoutedEvent = Avalonia.Controls.Primitives.Thumb.DragCompletedEvent });
        Dispatcher.UIThread.RunJobs();
        Assert.True(Sidebar(main).Bounds.Width >= fitted - 1, $"{Sidebar(main).Bounds.Width} < {fitted}");
        Untrimmed(Footer(main, "SelfName"));
        main.Close();
    }

    sealed record SoundRowParts(TextBlock Name, Slider Slider);

    sealed record VerticalExtent(double Top, double Bottom);
}
