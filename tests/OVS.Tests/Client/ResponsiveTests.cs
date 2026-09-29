using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OVS.Client.Input;
using OVS.Client.Localization;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Client.Views;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client;

/// <summary>Package 68 (A93 to A95): the main window adapts to every width from 360 px.</summary>
public sealed class ResponsiveTests : IDisposable
{
    readonly string dir = Directory.CreateTempSubdirectory("ovs-resp-").FullName;

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

    MainWindow Open(double width, ServerViewModel? server, out MainViewModel vm)
    {
        vm = new MainViewModel(dir, a => a(), useAudioDevices: false);
        var main = new MainWindow { DataContext = vm, Width = width, Height = 700 };
        main.Show();
        vm.Server = server;
        Dispatcher.UIThread.RunJobs();
        return main;
    }

    static void Resize(Window main, double width)
    {
        main.Width = width;
        Dispatcher.UIThread.RunJobs();
    }

    static Border Sidebar(MainWindow main) => main.FindControl<Border>("Sidebar")!;
    static ColumnDefinition SidebarColumn(MainWindow main) => ((Grid)Sidebar(main).Parent!).ColumnDefinitions[0];

    /// <summary>The header button whose text (or automation name) is the given resource text.</summary>
    static Button HeaderButton(MainWindow main, string text) => main.GetVisualDescendants().OfType<Button>()
        .Single(b => b.IsEffectivelyVisible && AutomationProperties.GetName(b) == text);

    static void FullyInside(MainWindow main, Control control)
    {
        var left = control.TranslatePoint(default, main)!.Value.X;
        Assert.True(left >= 0 && left + control.Bounds.Width <= main.Bounds.Width + 1,
            $"{AutomationProperties.GetName(control)} at {left}..{left + control.Bounds.Width}, window {main.Bounds.Width}");
    }

    static void Click(Window main, Control control)
    {
        var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), main)!.Value;
        main.MouseDown(center, MouseButton.Left);
        main.MouseUp(center, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    static Button MenuButton(MainWindow main) => main.GetVisualDescendants().OfType<Button>()
        .Single(b => b.IsEffectivelyVisible && AutomationProperties.GetName(b) == Strings.Ui_ShowChannels);

    /// <summary>Bug from the screenshot: at 776 px a wide sidebar pushed Ping, Verwaltung and Trennen out of the window.</summary>
    [AvaloniaFact]
    public void SidebarDraggedWide_HeaderButtonsStayVisible()
    {
        var main = Open(776, FakeServers.Admin(), out _);
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

    [AvaloniaFact]
    public void Classes_FollowWidth()
    {
        var main = Open(1100, FakeServers.Admin(), out _);
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
        var main = Open(360, FakeServers.Admin(), out var vm);
        Assert.False(Sidebar(main).IsEffectivelyVisible);
        var scrim = main.FindControl<Border>("DrawerScrim")!;

        Click(main, MenuButton(main));
        Assert.True(Sidebar(main).IsEffectivelyVisible);
        Assert.True(scrim.IsEffectivelyVisible);
        Assert.True(Sidebar(main).Bounds.Width <= 312, $"{Sidebar(main).Bounds.Width}");
        Assert.Equal(0, Sidebar(main).TranslatePoint(default, main)!.Value.X - main.GetVisualDescendants().OfType<Grid>().First().TranslatePoint(default, main)!.Value.X, 1);

        // double click on Raid joins it and closes the drawer
        var raid = main.GetVisualDescendants().OfType<Border>()
            .Single(b => b.Classes.Contains("row") && b.DataContext is ChannelViewModel { Name: "Raid" });
        var point = raid.TranslatePoint(new Point(raid.Bounds.Width / 2, raid.Bounds.Height / 2), main)!.Value;
        main.MouseDown(point, MouseButton.Left);
        main.MouseUp(point, MouseButton.Left);
        main.MouseDown(point, MouseButton.Left);
        main.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.False(Sidebar(main).IsEffectivelyVisible);

        Click(main, MenuButton(main));
        main.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(Sidebar(main).IsEffectivelyVisible);
        Assert.True(vm.IsHomePage);

        Click(main, MenuButton(main));
        var outside = new Point(main.Bounds.Width - 10, main.Bounds.Height / 2);
        main.MouseDown(outside, MouseButton.Left);
        main.MouseUp(outside, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.False(Sidebar(main).IsEffectivelyVisible);

        Click(main, MenuButton(main));
        vm.OpenSettings();
        Dispatcher.UIThread.RunJobs();
        Assert.False(Sidebar(main).IsEffectivelyVisible);
        vm.ClosePage();

        // wide again: the sidebar is back in its column
        Click(main, MenuButton(main));
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
        var main = Open(360, server, out var vm);
        vm.PingText = "23 ms";
        Dispatcher.UIThread.RunJobs();

        var buttons = new List<string> { Strings.Ui_AdminMenu, Strings.Ui_Disconnect };
        if (!admin) buttons.Add(Strings.Ui_RedeemTokenMenu);
        foreach (var text in buttons)
        {
            var button = HeaderButton(main, text);
            Assert.Equal(text, ToolTip.GetTip(button));
            Assert.DoesNotContain(button.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == text);
            FullyInside(main, button);
        }
        var ping = main.FindControl<Border>("PingChip")!;
        Assert.DoesNotContain(ping.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "23 ms");
        LayoutAssert.FitsHorizontally(main);

        Resize(main, 1100); // wide: the texts are back
        Assert.Contains(HeaderButton(main, Strings.Ui_Disconnect).GetVisualDescendants().OfType<TextBlock>(),
            t => t.IsEffectivelyVisible && t.Text == Strings.Ui_Disconnect);
        main.Close();
    }

    [AvaloniaFact]
    public void WindowShrinks_SidebarGivesWay()
    {
        var main = Open(1100, FakeServers.Admin(), out _);
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
    public void StartScreen_SameRules()
    {
        var settings = new ClientSettings();
        settings.Bookmarks.Add(new Bookmark("Gilde", "gilde.example.org", 7000, "ich"));
        settings.Save(dir);
        var main = Open(360, null, out _);
        Assert.Contains("compact", main.Classes);
        Assert.False(Sidebar(main).IsEffectivelyVisible);
        LayoutAssert.FitsHorizontally(main);

        Click(main, MenuButton(main));
        Assert.True(Sidebar(main).IsEffectivelyVisible);
        Assert.Contains(Sidebar(main).GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "Gilde");
        main.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(Sidebar(main).IsEffectivelyVisible);

        Resize(main, 1100);
        Assert.DoesNotContain("compact", main.Classes);
        Assert.True(Sidebar(main).IsEffectivelyVisible);
        Assert.DoesNotContain(main.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && AutomationProperties.GetName(b) == Strings.Ui_ShowChannels);
        main.Close();
    }

    // ---- Package 77: the settings page at every width ----

    /// <summary>Settings with the worst case: a key row next to the push-to-talk hint (a key, but none for push-to-talk).</summary>
    MainWindow OpenSettings(double width)
    {
        var settings = new ClientSettings { KeyBindings = [new OVS.Client.Input.KeyBinding(KeyAction.ToggleMute, new KeyChord(0x70))] };
        settings.Save(dir);
        var main = Open(width, null, out var vm);
        vm.OpenSettings();
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.SettingsPage!.ShowPttHint);
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
    public void Settings_FitAt360_480_600_1100(double width, string culture) => TestCulture.With(culture, () =>
    {
        var main = OpenSettings(width);
        var page = main.GetVisualDescendants().OfType<SettingsView>().Single();
        var scroller = page.GetVisualDescendants().OfType<ScrollViewer>().First();
        foreach (var section in page.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("section")).ToList())
        {
            section.BringIntoView();
            Dispatcher.UIThread.RunJobs();
            LayoutAssert.FitsHorizontally(page);
        }
        scroller.Offset = new Vector(0, scroller.Extent.Height);
        Dispatcher.UIThread.RunJobs();
        LayoutAssert.FitsHorizontally(main);
        foreach (var row in page.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("soundRow")))
        {
            var name = row.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("label"));
            Assert.True(name.Bounds.Width > 60, $"Name {name.Text}: {name.Bounds.Width}");
        }

        // the button bar stays in view
        foreach (var text in new[] { Strings.Dlg_Cancel, Strings.Dlg_Save })
        {
            var button = page.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == text);
            Assert.True(button.IsEffectivelyVisible);
            var y = button.TranslatePoint(default, main)!.Value.Y;
            Assert.True(y >= 0 && y + button.Bounds.Height <= main.Bounds.Height + 1, $"{text} at {y}");
        }
        main.Close();
        return 0;
    });

    static (TextBlock Name, Slider Slider) FirstSoundRow(MainWindow main)
    {
        var row = main.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("soundRow"));
        return (row.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("label")),
            row.GetVisualDescendants().OfType<Slider>().Single());
    }

    static (double Top, double Bottom) Vertical(Control control, Visual root)
    {
        var y = control.TranslatePoint(default, root)!.Value.Y;
        return (y, y + control.Bounds.Height);
    }

    [AvaloniaTheory]
    [InlineData(360)]
    [InlineData(600)] // not narrow yet, but too tight for name and controls side by side
    public void Settings_SoundRow_TwoLinesWhenNarrow_NameVisible(double width)
    {
        var main = OpenSettings(width);
        var (name, slider) = FirstSoundRow(main);
        Assert.True(name.Bounds.Width > 0);
        Assert.False(name.TextLayout.TextLines.Any(l => l.HasCollapsed), "Name abgeschnitten");
        Assert.True(Vertical(slider, main).Top >= Vertical(name, main).Bottom, "Regler nicht unter dem Namen");
        main.Close();
    }

    [AvaloniaFact]
    public void Settings_Wide_LayoutUnchanged()
    {
        var main = OpenSettings(1100);
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
        var main = Open(width, server, out var vm);
        _ = vm.OpenAdminAsync();
        Dispatcher.UIThread.RunJobs();
        var groups = server.Mirror.Groups.Select(g => g.Id).ToList();
        server.Apply(new OVS.Shared.Protocol.UserList("r", Enumerable.Range(1, 20)
            .Select(i => new OVS.Shared.Protocol.KnownUserInfo($"fp{i}", $"Mitspieler{i}", i % 3 == 0 ? groups : [groups[0]])).ToList()));
        server.Apply(new OVS.Shared.Protocol.BanList("r",
        [
            new(Guid.NewGuid(), "fpA", "Störenfried", "10.0.0.1", "Hat wiederholt den Raid-Channel mit Musik beschallt und Warnungen ignoriert", "ich", null),
            new(Guid.NewGuid(), "fpB", "Spammer", null, "Werbung", "ich", DateTimeOffset.Now.AddDays(3)),
            new(Guid.NewGuid(), "fpC", "Troll", null, "", "ich", null),
        ]));
        Dispatcher.UIThread.RunJobs();
        page = main.GetVisualDescendants().OfType<AdminView>().Single();
        return main;
    }

    static TabControl AdminTabs(AdminView page) => page.FindControl<TabControl>("Tabs")!;

    static void SelectTab(AdminView page, int index)
    {
        AdminTabs(page).SelectedIndex = index;
        Dispatcher.UIThread.RunJobs();
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
    public void Admin_EveryTabFits(double width, string culture) => TestCulture.With(culture, () =>
    {
        var main = OpenAdmin(width, out var page);
        var tabs = AdminTabs(page);
        var strip = tabs.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "PART_TabStrip");
        for (var i = 0; i < tabs.ItemCount; i++)
        {
            SelectTab(page, i);
            // the chosen tab is in view of the (scrolling) tab strip
            var header = (TabItem)tabs.ContainerFromIndex(i)!;
            var x = header.TranslatePoint(default, strip)!.Value.X;
            Assert.True(x >= -1 && x + header.Bounds.Width <= strip.Viewport.Width + 1, $"Tab {header.Header} at {x}, strip {strip.Viewport.Width}");

            var content = tabs.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>().Single(p => p.Name == "PART_SelectedContentHost");
            var matrix = content.GetVisualDescendants().OfType<ScrollViewer>().SingleOrDefault(s => s.Name == "LinkCells");
            // scroll every scrolling list through, so the lower cards are checked too
            foreach (var scroller in content.GetVisualDescendants().OfType<ScrollViewer>().Where(s => s != matrix && s.Viewport.Height > 0).ToList())
            {
                for (var y = 0.0; ; y += scroller.Viewport.Height)
                {
                    scroller.Offset = new Vector(0, y);
                    Dispatcher.UIThread.RunJobs();
                    LayoutAssert.FitsHorizontally(page, matrix);
                    if (y + scroller.Viewport.Height >= scroller.Extent.Height) break;
                }
            }
            LayoutAssert.FitsHorizontally(main, matrix);
        }
        main.Close();
        return 0;
    });

    [AvaloniaTheory]
    [InlineData(360, true)]
    [InlineData(1100, false)]
    public void Admin_Groups_StackedWhenNarrow(double width, bool stacked)
    {
        var main = OpenAdmin(width, out var page);
        SelectTab(page, 0);
        var list = page.FindControl<ListBox>("GroupList")!;
        var listCard = list.FindAncestorOfType<Border>()!;
        var editor = page.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("groupEditor"));
        var grid = (Grid)listCard.Parent!;
        var rights = editor.GetVisualDescendants().OfType<CheckBox>().Where(c => c.IsEffectivelyVisible).ToList();
        Assert.True(rights.Count > 2);
        var columns = rights.Select(c => Math.Round(c.TranslatePoint(default, main)!.Value.X)).Distinct().Count();
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
    public void Admin_LinkMatrix_ScrollsTitlesStay()
    {
        var main = OpenAdmin(360, out var page);
        SelectTab(page, 3);
        var cells = page.FindControl<ScrollViewer>("LinkCells")!;
        Assert.True(cells.Extent.Width > cells.Viewport.Width + 1, $"Matrix {cells.Extent.Width} passt in {cells.Viewport.Width}");
        var title = page.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("linkTitle"));
        var before = title.TranslatePoint(default, main)!.Value;
        var lastCell = page.GetVisualDescendants().OfType<Border>().Last(b => b.Classes.Contains("linkCell"));
        Assert.True(lastCell.TranslatePoint(default, cells)!.Value.X > cells.Viewport.Width);

        cells.Offset = new Vector(cells.Extent.Width, 0);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(before, title.TranslatePoint(default, main)!.Value);
        var x = lastCell.TranslatePoint(default, cells)!.Value.X;
        Assert.True(x >= 0 && x + lastCell.Bounds.Width <= cells.Viewport.Width + 1, $"letzte Zelle bei {x}");
        // title and cells of one row stay on one line
        Assert.Equal(Vertical(title, main).Top + title.Bounds.Height / 2,
            Vertical(page.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("linkCell")), main).Top + 16, tolerance: 2);
        LayoutAssert.FitsHorizontally(page, cells);
        main.Close();
    }
}

/// <summary>Package 68: the layout check itself finds what runs off the visible area.</summary>
public sealed class LayoutAssertTests
{
    [AvaloniaFact]
    public void DetectsClippedButton()
    {
        var button = new Button { Content = "Datei wählen", Width = 120 };
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Children = { new Border { Width = 300 }, button } };
        var window = new Window { Width = 360, Height = 200, Content = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var error = Assert.ThrowsAny<Exception>(() => LayoutAssert.FitsHorizontally(window));
        Assert.Contains("Button", error.Message);
        Assert.Contains("Datei wählen", error.Message);

        button.Width = 50; // inside now, but its text is cut off
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("needs", Assert.ThrowsAny<Exception>(() => LayoutAssert.FitsHorizontally(window)).Message);

        button.Content = "OK";
        Dispatcher.UIThread.RunJobs();
        LayoutAssert.FitsHorizontally(window);
        window.Close();
    }
}
