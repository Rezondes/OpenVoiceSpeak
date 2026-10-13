using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OVS.Client.Animations;
using OVS.Client.Controls;
using OVS.Client.Dialogs;
using OVS.Client.Localization;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Client.Views;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Animations;

/// <summary>Package 105: dialogs, popups and the drawer open and close with movement in the animated display.</summary>
[Collection(MotionCollection.Name)]
public sealed class OverlayMotionTests() : TempDirectoryTest("ovs-overlay-", quiet: true)
{
    static void Frame()
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    MainWindow Open(out MainViewModel viewModel, DisplayMode display = DisplayMode.Animated, double width = 1100)
    {
        new ClientSettings { Display = display }.Save(dir);
        viewModel = MainViewModelSetup.Create(dir, action => Dispatcher.UIThread.Post(action));
        var main = new MainWindow { DataContext = viewModel, Width = width, Height = 700 };
        main.Show();
        MainViewModelSetup.ShowServer(viewModel, FakeServers.Admin());
        MotionWait.Settle(700); // connecting builds the tree up (Package 106)
        Motion.Apply(main, display);
        MotionWait.Connected(main); // under load that takes longer: no test acts while it still moves
        return main;
    }

    static Border Scrim(OverlayHost overlay) => (Border)overlay.Children[0];
    static Border Card(OverlayHost overlay) => (Border)overlay.Children[1];

    static void ClickMenu(MainWindow main) =>
        main.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible && AutomationProperties.GetName(button) == Strings.Ui_ShowChannels)
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact]
    public void Open_ScrimFadesCardPops_CloseReverses()
    {
        var main = Open(out _);
        var overlay = main.Overlay;
        // the smallest values seen while it opens, every value as it happens: under load one frame can outlast the pop
        var scrims = MotionWait.Record(Scrim(overlay), visual => visual.Opacity);
        var cards = MotionWait.Record(Card(overlay), visual => visual.Opacity);
        var scales = MotionWait.Record(Card(overlay), MotionWait.Scale);
        var answer = ConfirmDialog.ShowAsync(overlay, "Weg damit?");
        MotionWait.Eventually(() =>
        {
            Assert.Contains(cards, value => value < 1); // it has begun
            Assert.Equal((1d, 1d), (Card(overlay).Opacity, Scrim(overlay).Opacity));
            Assert.Equal(1, MotionWait.Scale(Card(overlay)), 2);
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
        var first = ConfirmDialog.ShowAsync(overlay, "Erste Frage?");
        var second = ConfirmDialog.ShowAsync(overlay, "Zweite Frage?"); // waits its turn
        MotionWait.Settle(350);
        Assert.Contains(Card(overlay).GetVisualDescendants().OfType<TextBlock>(), textBlock => textBlock.Text == "Erste Frage?");
        // what the card holds from now on, in order: under load one frame can outlast the whole fold
        var held = new List<object?>();
        Card(overlay).PropertyChanged += (_, args) =>
        {
            if (args.Property == Decorator.ChildProperty) held.Add(args.NewValue);
        };
        overlay.Close();
        Frame(); // ends the first: its fold has had no frame yet
        await first;
        Assert.False(overlay.IsOpen); // closed at once
        Assert.Contains(Card(overlay).GetVisualDescendants().OfType<TextBlock>(), textBlock => textBlock.Text == "Erste Frage?"); // still folding away
        MotionWait.Eventually(() =>
        {
            Assert.True(overlay.IsOpen);
            Assert.Contains(Card(overlay).GetVisualDescendants().OfType<TextBlock>(), textBlock => textBlock.Text == "Zweite Frage?");
            Assert.DoesNotContain(Card(overlay).GetVisualDescendants().OfType<TextBlock>(), textBlock => textBlock.Text == "Erste Frage?");
        });
        // the waiting one came only once the first had gone
        Assert.Equal(2, held.Count);
        Assert.Null(held[0]);
        Assert.Same(Card(overlay).Child, held[1]);
        Assert.Single(overlay.Children.OfType<Border>(), border => border.Classes.Contains("dialog"));
        // one asked while the last folds away: the fold ends at once, never two cards
        overlay.Close();
        await second;
        Frame();
        var third = ConfirmDialog.ShowAsync(overlay, "Dritte Frage?");
        Frame();
        Assert.True(overlay.IsOpen);
        Assert.Contains(Card(overlay).GetVisualDescendants().OfType<TextBlock>(), textBlock => textBlock.Text == "Dritte Frage?");
        overlay.Close();
        await third;
        main.Close();
    }

    [AvaloniaFact]
    public void DialogCard_Refused_Shakes()
    {
        var main = Open(out var viewModel);
        var overlay = main.Overlay;
        _ = ConnectDialog.ShowAsync(overlay, viewModel.Bookmarks.Saved, null); // nothing filled in
        MotionWait.Settle(350);
        MotionWait.Eventually(() => Assert.Equal(1, Card(overlay).Opacity, 2)); // it has popped up: the shake is the only movement
        var swings = MotionWait.Record(Card(overlay), MotionWait.OffsetX); // every swing as it happens: under load one frame can outlast the shake
        main.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        // a shake swings through 0, and ends there
        MotionWait.Until(() => swings.Any(value => Math.Abs(value) > 2) && Math.Abs(MotionWait.OffsetX(Card(overlay))) < 0.01, atLeast: 450);
        double swing = swings.Select(value => Math.Abs(value)).DefaultIfEmpty(0).Max();
        Assert.True(swing > 2, $"the card shakes ({swing})");
        Assert.True(overlay.IsOpen); // still asking
        Assert.Equal(0, MotionWait.OffsetX(Card(overlay)), 2);
        overlay.Close();
        main.Close();
    }

    [AvaloniaFact]
    public void Popups_Opened_FadeAndSlide()
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
        var fades = MotionWait.Record(child, visual => visual.Opacity);
        var slides = MotionWait.Record(child, MotionWait.OffsetY);
        combo.IsDropDownOpen = true;
        MotionWait.Eventually(() =>
        {
            Assert.True(fades.Any(fade => fade < 1), "fades in");
            Assert.Equal(1, child.Opacity, 2);
        });
        Assert.True(slides.Any(slide => slide < 0), "slides down out of the box");

        // Package 109: closed at once, a picture of it fades where it was. Looked at the moment it is put in the overlay
        // (it is gone, and its picture let go, a frame later under load)
        var overlay = OverlayLayer.GetOverlayLayer(window)!;
        Image? ghost = null;
        int pictures = 0, shown = 0, all = 0;
        List<double>? faded = null;
        overlay.Children.CollectionChanged += (_, args) =>
        {
            foreach (var added in args.NewItems?.OfType<Image>() ?? [])
            {
                if (!added.Classes.Contains(PopupMotion.GhostClass)) continue;
                pictures++;
                ghost ??= added;
                faded ??= MotionWait.Record(added, visual => visual.Opacity);
                using var png = new MemoryStream();
                ((Avalonia.Media.Imaging.RenderTargetBitmap)added.Source!).Save(png);
                png.Position = 0;
                using var picture = SkiaSharp.SKBitmap.Decode(png);
                (shown, all) = (picture.Pixels.Count(skColor => skColor.Alpha > 0), picture.Pixels.Length);
            }
        };
        combo.IsDropDownOpen = false;
        Frame();
        Assert.False(popup.IsOpen);
        MotionWait.Until(() => ghost is not null);
        Assert.Equal(1, pictures);
        Assert.True(shown > all / 4, "a picture of the list, not an empty frame");
        MotionWait.Until(() => ghost!.Parent is null);
        Assert.True(faded!.Any(value => value < 0.9), $"it fades ({string.Join(" ", faded!)})");
        Assert.Empty(overlay.Children.OfType<Image>()); // and goes

        Motion.Apply(window, DisplayMode.Simplified);
        combo.IsDropDownOpen = true;
        MotionWait.Settle(100);
        combo.IsDropDownOpen = false;
        Frame();
        Assert.Empty(overlay.Children.OfType<Image>()); // the simplified display takes it away at once
        Assert.Equal(1, pictures); // and puts no picture of it in the overlay at all
        window.Close();
    }

    /// <summary>Package 109: a menu that closes for a dialog (an entry that asks) leaves no picture above the dialog.</summary>
    [AvaloniaFact]
    public void Popup_ClosedForADialog_NoPictureOverIt()
    {
        var main = Open(out _);
        var row = main.GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("row") && border.ContextMenu is not null);
        row.ContextMenu!.Open(row);
        MotionWait.Settle(300);
        // every picture put in the overlay counts, not only one a frame finds there: under load its fade is over within a frame
        int pictures = 0;
        OverlayLayer.GetOverlayLayer(main)!.Children.CollectionChanged += (_, args) =>
            pictures += args.NewItems?.OfType<Image>().Count(image => image.Classes.Contains(PopupMotion.GhostClass)) ?? 0;
        row.ContextMenu.Close();
        _ = ConfirmDialog.ShowAsync(main.Overlay, "Weg damit?");
        MotionWait.Settle(200);
        Assert.True(main.Overlay.IsOpen);
        Assert.Equal(0, pictures);
        main.Overlay.Close();
        main.Close();
    }

    [AvaloniaFact]
    public void Drawer_OpenedAndClosed_SlidesInAndOut()
    {
        var main = Open(out _, width: 360);
        var sidebar = main.FindControl<Border>("Sidebar")!;
        var scrim = main.FindControl<Border>("DrawerScrim")!;
        // every value as it happens: under load one frame can outlast the whole slide
        var slides = MotionWait.Record(sidebar, MotionWait.OffsetX);
        var fades = MotionWait.Record(scrim, visual => visual.Opacity);
        ClickMenu(main);
        Frame();
        Assert.True(sidebar.IsEffectivelyVisible);
        MotionWait.Eventually(() =>
        {
            Assert.True(slides.Any(slide => slide < 0) && fades.Any(fade => fade < 1), "it slides in from the left with its scrim");
            Assert.Equal((0d, 1d), (MotionWait.OffsetX(sidebar), scrim.Opacity));
        });

        slides.Clear();
        main.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Frame();
        Assert.False(sidebar.IsHitTestVisible); // closed at once for clicks
        Assert.True(sidebar.IsEffectivelyVisible, "it slides out");
        MotionWait.Eventually(() => Assert.False(sidebar.IsEffectivelyVisible));
        Assert.True(slides.Any(slide => slide < 0), "it slides out to the left");
        main.Close();
    }

    [AvaloniaFact]
    public void Motion_SimplifiedDisplay_Instant()
    {
        var main = Open(out _, DisplayMode.Simplified, width: 360);
        var overlay = main.Overlay;
        _ = ConfirmDialog.ShowAsync(overlay, "Weg damit?");
        Frame();
        Assert.Equal((1d, 1d), (Card(overlay).Opacity, MotionWait.Scale(Card(overlay))));
        overlay.Close();
        Frame();
        Assert.False(overlay.IsVisible);

        var sidebar = main.FindControl<Border>("Sidebar")!;
        ClickMenu(main);
        Frame();
        Assert.True(sidebar.IsEffectivelyVisible);
        Assert.Equal(0, MotionWait.OffsetX(sidebar));
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
        var first = ConfirmDialog.ShowAsync(overlay, "Erste Frage?");
        MotionWait.Settle(350);
        overlay.Close();
        await first;
        MotionWait.Eventually(() => Assert.False(overlay.IsVisible)); // fully gone
        var second = ConfirmDialog.ShowAsync(overlay, "Zweite Frage?");
        MotionWait.Settle(350);
        MotionWait.Eventually(() =>
        {
            Assert.Equal((1d, 1d), (Card(overlay).Opacity, Scrim(overlay).Opacity));
            Assert.Equal(1, MotionWait.Scale(Card(overlay)), 2);
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
            MotionWait.Settle(350);
            MotionWait.Eventually(() => Assert.Equal((0d, 1d), (MotionWait.OffsetX(sidebar), scrim.Opacity)));
            main.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            MotionWait.Settle(350);
            MotionWait.Eventually(() => Assert.False(sidebar.IsEffectivelyVisible));
        }
        main.Width = 1100;
        MotionWait.Settle(100);
        MotionWait.Eventually(() =>
        {
            Assert.True(sidebar.IsEffectivelyVisible);
            Assert.Equal(0, MotionWait.OffsetX(sidebar));
            Assert.True(sidebar.IsHitTestVisible);
        });

        // going wide while it still slides away stops the slide
        main.Width = 360;
        MotionWait.Settle(100);
        MotionWait.Until(() => !sidebar.IsEffectivelyVisible); // narrow again: the drawer is closed
        ClickMenu(main);
        MotionWait.Settle(350);
        MotionWait.Eventually(() => Assert.Equal((0d, 1d), (MotionWait.OffsetX(sidebar), scrim.Opacity))); // fully in
        main.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Frame();
        main.Width = 1100;
        MotionWait.Settle(350);
        MotionWait.Eventually(() =>
        {
            Assert.Equal(0, MotionWait.OffsetX(sidebar));
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
        using var watchOpen = Popup.IsOpenProperty.Changed.AddClassHandler<Popup>((_, args) =>
        {
            if (args.GetNewValue<bool>()) opened++;
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
            for (Visual? visual = content; visual is not null; visual = visual.GetVisualParent())
            {
                shown *= visual.Opacity;
                if (Math.Abs(MotionWait.OffsetY(visual)) > Math.Abs(slid)) slid = MotionWait.OffsetY(visual);
            }
            seen.Add(Math.Round(shown, 3));
            Thread.Sleep(1);
        }
        return seen;
    }

    static void Rises(List<double> seen)
    {
        Assert.Equal(1, seen[^1], 2);
        for (int index = 1; index < seen.Count; index++)
            Assert.True(seen[index] >= seen[index - 1] - 0.001, $"dips at frame {index}: {string.Join(" ", seen)}");
    }

    /// <summary>Package 115: a context menu keeps its fade and slide (Package 105) and shows no blink either.</summary>
    [AvaloniaFact]
    public void ContextMenu_Opened_FadesInOnce()
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
