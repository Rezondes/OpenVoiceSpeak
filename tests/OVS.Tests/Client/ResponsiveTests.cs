using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
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

        button.Width = 50; // fits now
        Dispatcher.UIThread.RunJobs();
        LayoutAssert.FitsHorizontally(window);
        window.Close();
    }
}
