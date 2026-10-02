using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Client.Views;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client;

/// <summary>Package 106: connecting builds the server view up, disconnecting folds it away.</summary>
public sealed class ConnectMotionTests : IDisposable
{
    readonly string dir = Directory.CreateTempSubdirectory("ovs-connect-").FullName;

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

    MainWindow Open(out MainViewModel vm, DisplayMode display = DisplayMode.Animated)
    {
        new ClientSettings { Display = display }.Save(dir);
        vm = new MainViewModel(dir, a => Dispatcher.UIThread.Post(a), useAudioDevices: false);
        var main = new MainWindow { DataContext = vm, Width = 1100, Height = 700 };
        main.Show();
        Settle(200);
        Motion.Apply(main, display);
        return main;
    }

    static T Named<T>(MainWindow main, string name) where T : Control => main.FindControl<T>(name)!;
    static double OffsetX(Visual v) => v.RenderTransform?.Value.M31 ?? 0;
    static double OffsetY(Visual v) => v.RenderTransform?.Value.M32 ?? 0;

    [AvaloniaFact]
    public void Connecting_PulsesAndFailShakes()
    {
        var main = Open(out var vm);
        var logo = main.GetVisualDescendants().OfType<LogoMark>().First(l => l.Bounds.Width >= 64);
        var bar = main.GetVisualDescendants().OfType<ProgressBar>().First(p => p.Classes.Contains("connect"));
        vm.IsConnecting = true;
        // looked at after every frame until it shows: under load a single look can land where the loop is at rest
        bool breathes = false;
        MotionWait.Until(() => breathes |= (logo.RenderTransform?.Value.M11 ?? 1) > 1.001 || logo.Opacity < 0.999);
        Assert.Contains("connecting", logo.Classes);
        Assert.True(breathes, "the logo breathes");
        MotionWait.Eventually(() => Assert.Equal(3, bar.Bounds.Height, 1)); // a slim line

        // every swing and every step of the fade as they happen: under load one frame can outlast the shake
        var card = Named<StackPanel>(main, "StartCard");
        var status = Named<TextBlock>(main, "StatusText");
        var swings = MotionWait.Record(card, OffsetX);
        var fades = MotionWait.Record(status, v => v.Opacity);
        vm.Status = "Verbindung fehlgeschlagen";
        vm.IsConnecting = false; // failed: no server
        MotionWait.Until(() => swings.Any(x => Math.Abs(x) > 2) && (OffsetX(card), status.Opacity) == (0d, 1d), atLeast: 450);
        double shook = swings.Select(x => Math.Abs(x)).DefaultIfEmpty(0).Max(), faded = fades.DefaultIfEmpty(1).Min();
        Assert.True(shook > 2, $"the start card shakes ({shook})");
        Assert.DoesNotContain("connecting", logo.Classes);
        Assert.Equal(1, logo.RenderTransform?.Value.M11 ?? 1, 3); // the breathing stops with the connecting
        Assert.True(faded < 1, "the reason fades in");
        Assert.Equal((0d, 1d), (OffsetX(card), status.Opacity));
        main.Close();
    }

    [AvaloniaFact]
    public void Connect_SequenceWithin700ms()
    {
        var main = Open(out var vm);
        var tree = Named<ItemsControl>(main, "ChannelItems");
        bool pageLeft = false, bookmarksLeft = false, chatCameUp = false, nameFaded = false, builtUp = false;
        // every value they take, not only the ones a frame happens to see: on a busy machine the first frame after the
        // connect can take longer than a whole fade (the crowded tree builds up in it)
        Named<Border>(main, "PageGhost").PropertyChanged += (_, e) => pageLeft |= e.Property == Visual.IsVisibleProperty && e.NewValue is true;
        Named<Border>(main, "TreeGhost").PropertyChanged += (_, e) => bookmarksLeft |= e.Property == Visual.IsVisibleProperty && e.NewValue is true;
        Named<Panel>(main, "PageHost").PropertyChanged += (_, e) => chatCameUp |= OffsetY(Named<Panel>(main, "PageHost")) > 0;
        Named<Border>(main, "ServerHeader").PropertyChanged += (_, e) => nameFaded |= e.Property == Visual.OpacityProperty && e.NewValue is double o && o < 1;
        var entered = new HashSet<Control>(); // every row that came in entering
        tree.ContainerPrepared += (_, e) =>
        {
            void Seen()
            {
                if (e.Container.Classes.Contains("entering")) entered.Add(e.Container);
            }
            Seen();
            e.Container.Classes.CollectionChanged += (_, _) => Seen();
        };
        bool Done() => pageLeft && bookmarksLeft && chatCameUp && nameFaded && entered.Count > 1 && MotionWait.IsConnected(main);
        vm.Server = FakeServers.Crowded();
        // all done within 700 ms. The bound is wall clock time, but an animation can only end on a frame, and its clock
        // starts with the first frame after it began: on a loaded machine frames come late. So it runs until the sequence
        // is over and allows four times the longest gap between two frames on top (on an idle machine a few ms)
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var gap = TimeSpan.Zero;
        while (!Done() && watch.ElapsedMilliseconds < 5000)
        {
            var frameStart = watch.Elapsed;
            Frame();
            Thread.Sleep(1);
            if (watch.Elapsed - frameStart > gap) gap = watch.Elapsed - frameStart;
        }
        var took = watch.Elapsed;
        builtUp = entered.Count > 1;
        Assert.True(pageLeft, "the home page fades away as a picture");
        Assert.True(bookmarksLeft, "and so do the bookmarks");
        Assert.True(chatCameUp, "the chat comes up from below");
        Assert.True(nameFaded, "the server's name fades in");
        Assert.True(builtUp, "the tree builds up channel by channel");
        Assert.True(took <= TimeSpan.FromMilliseconds(700) + 4 * gap, $"over after {took.TotalMilliseconds:F0} ms (longest frame gap {gap.TotalMilliseconds:F0} ms)");
        Assert.False(Named<Border>(main, "PageGhost").IsVisible);
        Assert.False(Named<Border>(main, "TreeGhost").IsVisible);
        Assert.Equal((0d, 1d), (OffsetY(Named<Panel>(main, "PageHost")), Named<Border>(main, "ServerHeader").Opacity));
        Assert.All(tree.GetRealizedContainers(), c => Assert.Equal(1, c.Opacity, 2));
        Assert.DoesNotContain(tree.GetRealizedContainers(), c => c.Classes.Contains("entering"));
        main.Close();
    }

    [AvaloniaFact]
    public void Disconnect_FoldsAwayHomeWithReason()
    {
        var main = Open(out var vm);
        vm.Server = FakeServers.Admin();
        Settle(700);
        MotionWait.Connected(main);
        vm.Status = "Verbindung verloren";
        vm.Server = null;
        Frame();
        Assert.True(Named<Border>(main, "PageGhost").IsVisible && Named<Border>(main, "TreeGhost").IsVisible, "the server folds away as a picture");
        var picture = (Image)Named<Border>(main, "PageGhost").Child!;
        var drops = MotionWait.Record(picture, OffsetY); // as it happens: under load one frame can outlast the fold
        MotionWait.Eventually(() =>
        {
            Assert.False(Named<Border>(main, "PageGhost").IsVisible);
            Assert.True(Named<TextBlock>(main, "StatusText").IsEffectivelyVisible);
        });
        Assert.True(drops.Any(y => y > 0), "it goes down");
        Assert.Equal("Verbindung verloren", Named<TextBlock>(main, "StatusText").Text);
        main.Close();
    }

    /// <summary>A bookmark clicked while connected: disconnect and connect in one go, never a blank frame.</summary>
    [AvaloniaFact]
    public void ServerSwitch_NoBlankFrame()
    {
        var main = Open(out var vm);
        vm.Server = FakeServers.Admin();
        Settle(700);
        MotionWait.Connected(main);
        vm.Server = null;
        vm.Server = FakeServers.Crowded();
        Assert.True(Named<Border>(main, "PageGhost").IsVisible); // the old server's picture covers the change at once
        Frame();
        MotionWait.Eventually(() =>
        {
            Assert.False(Named<Border>(main, "PageGhost").IsVisible);
            Assert.True(Named<ItemsControl>(main, "ChannelItems").IsEffectivelyVisible);
        });
        main.Close();
    }

    [AvaloniaFact]
    public void Simplified_Instant()
    {
        var main = Open(out var vm, DisplayMode.Simplified);
        vm.IsConnecting = true;
        Frame();
        Assert.Equal(1, main.GetVisualDescendants().OfType<LogoMark>().First(l => l.Bounds.Width >= 64).RenderTransform?.Value.M11 ?? 1);
        vm.IsConnecting = false;
        vm.Server = FakeServers.Admin();
        Frame();
        Assert.False(Named<Border>(main, "PageGhost").IsVisible);
        Assert.Equal((0d, 1d), (OffsetY(Named<Panel>(main, "PageHost")), Named<Border>(main, "ServerHeader").Opacity));
        Assert.DoesNotContain(Named<ItemsControl>(main, "ChannelItems").GetRealizedContainers(), c => c.Classes.Contains("entering"));
        main.Close();
    }

    /// <summary>Connected while the drawer is closed (compact): the tree does not build up later when it opens.</summary>
    [AvaloniaFact]
    public void ConnectedWithDrawerClosed_NoLateBuildUp()
    {
        var main = Open(out var vm);
        main.Width = 360;
        Settle(100);
        vm.Server = FakeServers.Admin();
        Settle(800); // longer than the build-up waits for a tree to show (500 ms)
        MotionWait.Connected(main);
        var menu = main.GetVisualDescendants().OfType<Button>().Single(b => b.IsEffectivelyVisible
            && Avalonia.Automation.AutomationProperties.GetName(b) == OVS.Client.Localization.Strings.Ui_ShowChannels);
        menu.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Frame();
        Frame();
        Assert.DoesNotContain(Named<ItemsControl>(main, "ChannelItems").GetRealizedContainers(), c => c.Classes.Contains("entering"));
        main.Close();
    }
}
