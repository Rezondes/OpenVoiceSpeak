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
        Settle(700); // connecting builds the tree up (Package 106)
        Motion.Apply(main, display);
        MotionWait.Connected(main); // under load that takes longer: no test acts while it still moves
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
        // the smallest values seen while it opens, every value as it happens: under load one frame can outlast the pop
        var scrims = MotionWait.Record(Scrim(overlay), v => v.Opacity);
        var cards = MotionWait.Record(Card(overlay), v => v.Opacity);
        var scales = MotionWait.Record(Card(overlay), Scale);
        var answer = SimpleDialogs.Confirm(overlay, "Weg damit?");
        MotionWait.Eventually(() =>
        {
            Assert.Contains(cards, o => o < 1); // it has begun
            Assert.Equal((1d, 1d), (Card(overlay).Opacity, Scrim(overlay).Opacity));
            Assert.Equal(1, Scale(Card(overlay)), 2);
        });
        double scrimLeast = scrims.DefaultIfEmpty(1).Min(), cardLeast = cards.DefaultIfEmpty(1).Min(), scaleLeast = scales.DefaultIfEmpty(1).Min();
        Assert.True(scrimLeast < 0.9, "the scrim fades in");
        Assert.True(cardLeast < 0.9 && scaleLeast < 0.99, $"the card pops: {cardLeast}, {scaleLeast}");

        overlay.Close();
        Frame();
        Assert.True(answer.IsCompleted); // the answer is there at once
        Assert.False(overlay.IsOpen); // and keys and clicks are gone at once
        Assert.True(overlay.IsVisible && !overlay.IsHitTestVisible, "it plays back, without catching clicks");
        MotionWait.Eventually(() => Assert.False(overlay.IsVisible));
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
        // what the card holds from now on, in order: under load one frame can outlast the whole fold
        var held = new List<object?>();
        Card(overlay).PropertyChanged += (_, e) =>
        {
            if (e.Property == Decorator.ChildProperty) held.Add(e.NewValue);
        };
        overlay.Close();
        Frame(); // ends the first: its fold has had no frame yet
        await first;
        Assert.False(overlay.IsOpen); // closed at once
        Assert.Contains(Card(overlay).GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Erste Frage?"); // still folding away
        MotionWait.Eventually(() =>
        {
            Assert.True(overlay.IsOpen);
            Assert.Contains(Card(overlay).GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Zweite Frage?");
            Assert.DoesNotContain(Card(overlay).GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Erste Frage?");
        });
        // the waiting one came only once the first had gone
        Assert.Equal(2, held.Count);
        Assert.Null(held[0]);
        Assert.Same(Card(overlay).Child, held[1]);
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
        MotionWait.Eventually(() => Assert.Equal(1, Card(overlay).Opacity, 2)); // it has popped up: the shake is the only movement
        var swings = MotionWait.Record(Card(overlay), OffsetX); // every swing as it happens: under load one frame can outlast the shake
        main.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        // a shake swings through 0, and ends there
        MotionWait.Until(() => swings.Any(x => Math.Abs(x) > 2) && Math.Abs(OffsetX(Card(overlay))) < 0.01, atLeast: 450);
        double swing = swings.Select(x => Math.Abs(x)).DefaultIfEmpty(0).Max();
        Assert.True(swing > 2, $"the card shakes ({swing})");
        Assert.True(overlay.IsOpen); // still asking
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
        Frame();
        var popup = combo.GetVisualDescendants().OfType<Popup>().First();
        var child = popup.Child!;
        // every value as it happens: under load one frame can outlast the whole fade
        var fades = MotionWait.Record(child, v => v.Opacity);
        var slides = MotionWait.Record(child, v => v.RenderTransform?.Value.M32 ?? 0);
        combo.IsDropDownOpen = true;
        MotionWait.Eventually(() =>
        {
            Assert.True(fades.Any(o => o < 1), "fades in");
            Assert.Equal(1, child.Opacity, 2);
        });
        Assert.True(slides.Any(y => y < 0), "slides down out of the box");

        // Package 109: closed at once, a picture of it fades where it was. Looked at the moment it is put in the overlay
        // (it is gone, and its picture let go, a frame later under load)
        var overlay = OverlayLayer.GetOverlayLayer(window)!;
        Image? ghost = null;
        int pictures = 0, shown = 0, all = 0;
        List<double>? faded = null;
        overlay.Children.CollectionChanged += (_, e) =>
        {
            foreach (var added in e.NewItems?.OfType<Image>() ?? [])
            {
                if (!added.Classes.Contains(Motion.PopupGhostClass)) continue;
                pictures++;
                ghost ??= added;
                faded ??= MotionWait.Record(added, v => v.Opacity);
                using var png = new MemoryStream();
                ((Avalonia.Media.Imaging.RenderTargetBitmap)added.Source!).Save(png);
                png.Position = 0;
                using var picture = SkiaSharp.SKBitmap.Decode(png);
                (shown, all) = (picture.Pixels.Count(p => p.Alpha > 0), picture.Pixels.Length);
            }
        };
        combo.IsDropDownOpen = false;
        Frame();
        Assert.False(popup.IsOpen);
        MotionWait.Until(() => ghost is not null);
        Assert.Equal(1, pictures);
        Assert.True(shown > all / 4, "a picture of the list, not an empty frame");
        MotionWait.Until(() => ghost!.Parent is null);
        Assert.True(faded!.Any(o => o < 0.9), $"it fades ({string.Join(" ", faded!)})");
        Assert.Empty(overlay.Children.OfType<Image>()); // and goes

        Motion.Apply(window, DisplayMode.Simplified);
        combo.IsDropDownOpen = true;
        Settle(100);
        combo.IsDropDownOpen = false;
        Frame();
        Assert.Empty(overlay.Children.OfType<Image>()); // the simplified display takes it away at once
        Assert.Equal(1, pictures); // and puts no picture of it in the overlay at all
        window.Close();
    }

    /// <summary>Package 109: a menu that closes for a dialog (an entry that asks) leaves no picture above the dialog.</summary>
    [AvaloniaFact]
    public void PopupClosedForADialog_NoPictureOverIt()
    {
        var main = Open(out _);
        var row = main.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("row") && b.ContextMenu is not null);
        row.ContextMenu!.Open(row);
        Settle(300);
        // every picture put in the overlay counts, not only one a frame finds there: under load its fade is over within a frame
        int pictures = 0;
        OverlayLayer.GetOverlayLayer(main)!.Children.CollectionChanged += (_, e) =>
            pictures += e.NewItems?.OfType<Image>().Count(i => i.Classes.Contains(Motion.PopupGhostClass)) ?? 0;
        row.ContextMenu.Close();
        _ = SimpleDialogs.Confirm(main.Overlay, "Weg damit?");
        Settle(200);
        Assert.True(main.Overlay.IsOpen);
        Assert.Equal(0, pictures);
        main.Overlay.Close();
        main.Close();
    }

    [AvaloniaFact]
    public void Drawer_SlidesInAndOut()
    {
        var main = Open(out _, width: 360);
        var sidebar = main.FindControl<Border>("Sidebar")!;
        var scrim = main.FindControl<Border>("DrawerScrim")!;
        // every value as it happens: under load one frame can outlast the whole slide
        var slides = MotionWait.Record(sidebar, OffsetX);
        var fades = MotionWait.Record(scrim, v => v.Opacity);
        ClickMenu(main);
        Frame();
        Assert.True(sidebar.IsEffectivelyVisible);
        MotionWait.Eventually(() =>
        {
            Assert.True(slides.Any(x => x < 0) && fades.Any(o => o < 1), "it slides in from the left with its scrim");
            Assert.Equal((0d, 1d), (OffsetX(sidebar), scrim.Opacity));
        });

        slides.Clear();
        main.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Frame();
        Assert.False(sidebar.IsHitTestVisible); // closed at once for clicks
        Assert.True(sidebar.IsEffectivelyVisible, "it slides out");
        MotionWait.Eventually(() => Assert.False(sidebar.IsEffectivelyVisible));
        Assert.True(slides.Any(x => x < 0), "it slides out to the left");
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
        MotionWait.Eventually(() => Assert.False(overlay.IsVisible)); // fully gone
        var second = SimpleDialogs.Confirm(overlay, "Zweite Frage?");
        Settle(350);
        MotionWait.Eventually(() =>
        {
            Assert.Equal((1d, 1d), (Card(overlay).Opacity, Scrim(overlay).Opacity));
            Assert.Equal(1, Scale(Card(overlay)), 2);
            Assert.True(Card(overlay).IsEnabled && overlay.IsHitTestVisible);
        });
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
            MotionWait.Eventually(() => Assert.Equal((0d, 1d), (OffsetX(sidebar), scrim.Opacity)));
            main.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Settle(350);
            MotionWait.Eventually(() => Assert.False(sidebar.IsEffectivelyVisible));
        }
        main.Width = 1100;
        Settle(100);
        MotionWait.Eventually(() =>
        {
            Assert.True(sidebar.IsEffectivelyVisible);
            Assert.Equal(0, OffsetX(sidebar));
            Assert.True(sidebar.IsHitTestVisible);
        });

        // going wide while it still slides away stops the slide
        main.Width = 360;
        Settle(100);
        MotionWait.Until(() => !sidebar.IsEffectivelyVisible); // narrow again: the drawer is closed
        ClickMenu(main);
        Settle(350);
        MotionWait.Eventually(() => Assert.Equal((0d, 1d), (OffsetX(sidebar), scrim.Opacity))); // fully in
        main.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Frame();
        main.Width = 1100;
        Settle(350);
        MotionWait.Eventually(() =>
        {
            Assert.Equal(0, OffsetX(sidebar));
            Assert.True(sidebar.IsEffectivelyVisible);
        });
        main.Close();
    }

    /// <summary>
    /// Package 115: a tooltip fades in once and stays; it used to fade in, then blink a second time. Sampled every frame:
    /// what shows of it (its own opacity times that of everything between it and its popup) only rises.
    /// </summary>
    [AvaloniaFact]
    public void ToolTip_FadesInOnce_NoSecondBlink()
    {
        var tip = new ToolTip { Content = "Hallo" };
        var button = new Button { Content = "Knopf", [ToolTip.TipProperty] = tip };
        var window = new Window { Width = 400, Height = 300, Content = button };
        window.Show();
        Motion.Apply(window, DisplayMode.Animated);
        Dispatcher.UIThread.RunJobs();
        int opened = 0;
        using var watchOpen = Popup.IsOpenProperty.Changed.AddClassHandler<Popup>((_, e) =>
        {
            if (e.GetNewValue<bool>()) opened++;
        });
        ToolTip.SetIsOpen(button, true);
        var seen = Shown(tip, out double slid);
        Assert.Equal(1, opened);
        Assert.True(seen[0] < 1, "it fades in");
        Assert.True(slid != 0, "and slides out of its anchor (Package 105)");
        Rises(seen);
        window.Close();
    }

    /// <summary>
    /// What shows of a popup content every frame for 700 ms and until it shows fully (under load the fade takes more
    /// frames' time), and the largest slide seen.
    /// </summary>
    static List<double> Shown(Visual content, out double slid)
    {
        var seen = new List<double>();
        slid = 0;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 700 || seen[^1] < 0.999 && watch.ElapsedMilliseconds < 5000)
        {
            Frame();
            double shown = 1;
            for (Visual? v = content; v is not null; v = v.GetVisualParent())
            {
                shown *= v.Opacity;
                if (Math.Abs(v.RenderTransform?.Value.M32 ?? 0) > Math.Abs(slid)) slid = v.RenderTransform!.Value.M32;
            }
            seen.Add(Math.Round(shown, 3));
            Thread.Sleep(1);
        }
        return seen;
    }

    static void Rises(List<double> seen)
    {
        Assert.Equal(1, seen[^1], 2);
        for (int i = 1; i < seen.Count; i++)
            Assert.True(seen[i] >= seen[i - 1] - 0.001, $"dips at frame {i}: {string.Join(" ", seen)}");
    }

    /// <summary>Package 115: a context menu keeps its fade and slide (Package 105) and shows no blink either.</summary>
    [AvaloniaFact]
    public void ContextMenu_FadesInOnce()
    {
        var item = new MenuItem { Header = "Beitreten" };
        var menu = new ContextMenu { Items = { item } };
        var button = new Button { Content = "Knopf", ContextMenu = menu };
        var window = new Window { Width = 400, Height = 300, Content = button };
        window.Show();
        Motion.Apply(window, DisplayMode.Animated);
        Dispatcher.UIThread.RunJobs();
        menu.Open(button);
        var seen = Shown(item, out double slid);
        Assert.True(seen[0] < 1, "it fades in");
        Assert.True(slid != 0, "and slides");
        Rises(seen);
        window.Close();
    }
}
