using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OVS.Client.Localization;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Client.Views;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client;

/// <summary>Package 105: dialogs, popups and the drawer open and close with movement in the animated display.</summary>
public sealed class OverlayMotionTests : IDisposable
{
    readonly string dir = Directory.CreateTempSubdirectory("ovs-overlay-").FullName;

    public void Dispose()
    {
        Motion.IsAnimated = true;
        try
        {
            Directory.Delete(dir, true);
        }
        catch (IOException)
        {
        }
    }

    static void Settle(int milliseconds)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < milliseconds)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(1);
        }
    }

    static void Frame()
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    MainWindow Open(out MainViewModel vm, DisplayMode display = DisplayMode.Animated, double width = 1100)
    {
        new ClientSettings { Display = display }.Save(dir);
        vm = new MainViewModel(dir, a => Dispatcher.UIThread.Post(a), useAudioDevices: false);
        var main = new MainWindow { DataContext = vm, Width = width, Height = 700 };
        main.Show();
        vm.Server = FakeServers.Admin();
        Settle(300);
        Motion.Apply(main, display);
        return main;
    }

    static Border Scrim(OverlayHost overlay) => (Border)overlay.Children[0];
    static Border Card(OverlayHost overlay) => (Border)overlay.Children[1];
    static double Scale(Visual v) => v.RenderTransform?.Value.M11 ?? 1;
    static double OffsetX(Visual v) => v.RenderTransform?.Value.M31 ?? 0;

    static void ClickMenu(MainWindow main) =>
        main.GetVisualDescendants().OfType<Button>().Single(b => b.IsEffectivelyVisible && AutomationProperties.GetName(b) == Strings.Ui_ShowChannels)
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact]
    public void Open_ScrimFadesCardPops_CloseReverses()
    {
        var main = Open(out _);
        var overlay = main.Overlay;
        var answer = SimpleDialogs.Confirm(overlay, "Weg damit?");
        double scrimLeast = 1, cardLeast = 1, scaleLeast = 1;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 350) // the smallest values seen while it opens (a frame may come late)
        {
            Frame();
            (scrimLeast, cardLeast, scaleLeast) = (Math.Min(scrimLeast, Scrim(overlay).Opacity), Math.Min(cardLeast, Card(overlay).Opacity), Math.Min(scaleLeast, Scale(Card(overlay))));
            Thread.Sleep(1);
        }
        Assert.True(scrimLeast < 0.9, "the scrim fades in");
        Assert.True(cardLeast < 0.9 && scaleLeast < 0.99, $"the card pops: {cardLeast}, {scaleLeast}");
        Assert.Equal((1d, 1d), (Card(overlay).Opacity, Scrim(overlay).Opacity));
        Assert.Equal(1, Scale(Card(overlay)), 2);

        overlay.Close();
        Frame();
        Assert.True(answer.IsCompleted); // the answer is there at once
        Assert.False(overlay.IsOpen); // and keys and clicks are gone at once
        Assert.True(overlay.IsVisible && !overlay.IsHitTestVisible, "it plays back, without catching clicks");
        Settle(350);
        Assert.False(overlay.IsVisible);
        main.Close();
    }

    [AvaloniaFact]
    public async Task Queued_StartsWhenTheLastHasGone_NeverTwoCards()
    {
        var main = Open(out _);
        var overlay = main.Overlay;
        var first = SimpleDialogs.Confirm(overlay, "Erste Frage?");
        var second = SimpleDialogs.Confirm(overlay, "Zweite Frage?"); // waits its turn
        Settle(350);
        Assert.Contains(Card(overlay).GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Erste Frage?");
        overlay.Close();
        Frame();
        await first;
        Frame();
        Assert.False(overlay.IsOpen); // the waiting one comes once the first has gone
        Assert.Contains(Card(overlay).GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Erste Frage?");
        Settle(350);
        Assert.True(overlay.IsOpen);
        Assert.Contains(Card(overlay).GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Zweite Frage?");
        Assert.DoesNotContain(Card(overlay).GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Erste Frage?");
        Assert.Single(overlay.Children.OfType<Border>(), b => b.Classes.Contains("dialog"));
        // one asked while the last folds away: the fold ends at once, never two cards
        overlay.Close();
        await second;
        Frame();
        var third = SimpleDialogs.Confirm(overlay, "Dritte Frage?");
        Frame();
        Assert.True(overlay.IsOpen);
        Assert.Contains(Card(overlay).GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Dritte Frage?");
        overlay.Close();
        await third;
        main.Close();
    }

    [AvaloniaFact]
    public void Refused_ShakesCard()
    {
        var main = Open(out var vm);
        var overlay = main.Overlay;
        _ = SimpleDialogs.Connect(overlay, vm.Settings, null); // nothing filled in
        Settle(350);
        main.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Settle(60);
        Assert.NotEqual(0, OffsetX(Card(overlay)), 1);
        Assert.True(overlay.IsOpen); // still asking
        Settle(450);
        Assert.Equal(0, OffsetX(Card(overlay)), 2);
        overlay.Close();
        main.Close();
    }

    [AvaloniaFact]
    public void Popups_FadeAndSlide()
    {
        var combo = new ComboBox { ItemsSource = new[] { "Hell", "Dunkel" }, Width = 200 };
        var window = new Window { Width = 400, Height = 300, Content = combo };
        window.Show();
        Motion.Apply(window, DisplayMode.Animated);
        Dispatcher.UIThread.RunJobs();
        combo.IsDropDownOpen = true;
        Frame();
        var popup = combo.GetVisualDescendants().OfType<Popup>().First();
        var child = popup.Child!;
        Assert.True(child.Opacity < 1, "fades in");
        Assert.True((child.RenderTransform?.Value.M32 ?? 0) < 0, "slides down out of the box");
        Settle(350);
        Assert.Equal(1, child.Opacity, 2);
        combo.IsDropDownOpen = false;
        window.Close();
    }

    [AvaloniaFact]
    public void Drawer_SlidesInAndOut()
    {
        var main = Open(out _, width: 360);
        var sidebar = main.FindControl<Border>("Sidebar")!;
        var scrim = main.FindControl<Border>("DrawerScrim")!;
        ClickMenu(main);
        Frame();
        Assert.True(sidebar.IsEffectivelyVisible);
        Assert.True(OffsetX(sidebar) < 0 && scrim.Opacity < 1, "it slides in from the left with its scrim");
        Settle(350);
        Assert.Equal((0d, 1d), (OffsetX(sidebar), scrim.Opacity));

        main.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Frame();
        Assert.False(sidebar.IsHitTestVisible); // closed at once for clicks
        Assert.True(sidebar.IsEffectivelyVisible && OffsetX(sidebar) <= 0, "it slides out");
        Settle(350);
        Assert.False(sidebar.IsEffectivelyVisible);
        main.Close();
    }

    [AvaloniaFact]
    public void Simplified_Instant()
    {
        var main = Open(out _, DisplayMode.Simplified, width: 360);
        var overlay = main.Overlay;
        _ = SimpleDialogs.Confirm(overlay, "Weg damit?");
        Frame();
        Assert.Equal((1d, 1d), (Card(overlay).Opacity, Scale(Card(overlay))));
        overlay.Close();
        Frame();
        Assert.False(overlay.IsVisible);

        var sidebar = main.FindControl<Border>("Sidebar")!;
        ClickMenu(main);
        Frame();
        Assert.True(sidebar.IsEffectivelyVisible);
        Assert.Equal(0, OffsetX(sidebar));
        main.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Frame();
        Assert.False(sidebar.IsEffectivelyVisible);
        main.Close();
    }

    /// <summary>A finished close leaves nothing behind: the next dialog shows whole (regression, found by review).</summary>
    [AvaloniaFact]
    public async Task NextDialog_AfterAFullClose_IsWhole()
    {
        var main = Open(out _);
        var overlay = main.Overlay;
        var first = SimpleDialogs.Confirm(overlay, "Erste Frage?");
        Settle(350);
        overlay.Close();
        await first;
        Settle(350); // fully gone
        Assert.False(overlay.IsVisible);
        var second = SimpleDialogs.Confirm(overlay, "Zweite Frage?");
        Settle(350);
        Assert.Equal((1d, 1d), (Card(overlay).Opacity, Scrim(overlay).Opacity));
        Assert.Equal(1, Scale(Card(overlay)), 2);
        Assert.True(Card(overlay).IsEnabled && overlay.IsHitTestVisible);
        overlay.Close();
        await second;
        main.Close();
    }

    /// <summary>The drawer slides in whole a second time, and wide again it stands in its column (regression).</summary>
    [AvaloniaFact]
    public void Drawer_SecondTimeAndWide_AreWhole()
    {
        var main = Open(out _, width: 360);
        var sidebar = main.FindControl<Border>("Sidebar")!;
        var scrim = main.FindControl<Border>("DrawerScrim")!;
        for (int round = 0; round < 2; round++)
        {
            ClickMenu(main);
            Settle(350);
            Assert.Equal((0d, 1d), (OffsetX(sidebar), scrim.Opacity));
            main.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Settle(350);
            Assert.False(sidebar.IsEffectivelyVisible);
        }
        main.Width = 1100;
        Settle(100);
        Assert.True(sidebar.IsEffectivelyVisible);
        Assert.Equal(0, OffsetX(sidebar));
        Assert.True(sidebar.IsHitTestVisible);

        // going wide while it still slides away stops the slide
        main.Width = 360;
        Settle(100);
        ClickMenu(main);
        Settle(350);
        main.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Frame();
        main.Width = 1100;
        Settle(350);
        Assert.Equal(0, OffsetX(sidebar));
        Assert.True(sidebar.IsEffectivelyVisible);
        main.Close();
    }
}
