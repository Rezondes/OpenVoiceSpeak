using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OVS.Client.Animations;
using OVS.Client.Controls;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Client.Views;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Animations;

/// <summary>Package 106: connecting builds the server view up, disconnecting folds it away.</summary>
[Collection(MotionCollection.Name)]
public sealed class ConnectMotionTests() : TempDirectoryTest("ovs-connect-", quiet: true)
{
    static void Frame()
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    MainWindow Open(out MainViewModel viewModel, DisplayMode display = DisplayMode.Animated)
    {
        new ClientSettings { Display = display }.Save(dir);
        viewModel = MainViewModelSetup.Create(dir, action => Dispatcher.UIThread.Post(action));
        var main = new MainWindow { DataContext = viewModel, Width = 1100, Height = 700 };
        main.Show();
        MotionWait.Settle(200);
        Motion.Apply(main, display);
        return main;
    }

    static T Named<T>(MainWindow main, string name) where T : Control => main.FindControl<T>(name)!;
    static double OffsetX(Visual visual) => visual.RenderTransform?.Value.M31 ?? 0;
    static double OffsetY(Visual visual) => visual.RenderTransform?.Value.M32 ?? 0;

    [AvaloniaFact]
    public void Connecting_PulsesAndFailShakes()
    {
        var main = Open(out var viewModel);
        var logo = main.GetVisualDescendants().OfType<LogoMark>().First(logoMark => logoMark.Bounds.Width >= 64);
        var bar = main.GetVisualDescendants().OfType<ProgressBar>().First(progressBar => progressBar.Classes.Contains("connect"));
        viewModel.Connection.IsConnecting = true;
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
        var fades = MotionWait.Record(status, visual => visual.Opacity);
        viewModel.Status = "Verbindung fehlgeschlagen";
        viewModel.Connection.IsConnecting = false; // failed: no server
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
        var main = Open(out var viewModel);
        var tree = Named<ItemsControl>(main, "ChannelItems");
        bool pageLeft = false, bookmarksLeft = false, chatCameUp = false, nameFaded = false, builtUp = false;
        // every value they take, not only the ones a frame happens to see: on a busy machine the first frame after the
        // connect can take longer than a whole fade (the crowded tree builds up in it)
        Named<Border>(main, "PageGhost").PropertyChanged += (_, args) => pageLeft |= args.Property == Visual.IsVisibleProperty && args.NewValue is true;
        Named<Border>(main, "TreeGhost").PropertyChanged += (_, args) => bookmarksLeft |= args.Property == Visual.IsVisibleProperty && args.NewValue is true;
        Named<Panel>(main, "PageHost").PropertyChanged += (_, args) => chatCameUp |= OffsetY(Named<Panel>(main, "PageHost")) > 0;
        Named<Border>(main, "ServerHeader").PropertyChanged += (_, args) => nameFaded |= args.Property == Visual.OpacityProperty && args.NewValue is double o && o < 1;
        var entered = new HashSet<Control>(); // every row that came in entering
        tree.ContainerPrepared += (_, args) =>
        {
            void Seen()
            {
                if (args.Container.Classes.Contains("entering")) entered.Add(args.Container);
            }
            Seen();
            args.Container.Classes.CollectionChanged += (_, _) => Seen();
        };
        bool Done() => pageLeft && bookmarksLeft && chatCameUp && nameFaded && entered.Count > 1 && MotionWait.IsConnected(main);
        MainViewModelSetup.ShowServer(viewModel, FakeServers.Crowded());
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
        Assert.All(tree.GetRealizedContainers(), control => Assert.Equal(1, control.Opacity, 2));
        Assert.DoesNotContain(tree.GetRealizedContainers(), control => control.Classes.Contains("entering"));
        main.Close();
    }

    [AvaloniaFact]
    public void Disconnect_FoldsAwayHomeWithReason()
    {
        var main = Open(out var viewModel);
        MainViewModelSetup.ShowServer(viewModel, FakeServers.Admin());
        MotionWait.Settle(700);
        MotionWait.Connected(main);
        viewModel.Status = "Verbindung verloren";
        MainViewModelSetup.ShowServer(viewModel, null);
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
        var main = Open(out var viewModel);
        MainViewModelSetup.ShowServer(viewModel, FakeServers.Admin());
        MotionWait.Settle(700);
        MotionWait.Connected(main);
        MainViewModelSetup.ShowServer(viewModel, null);
        MainViewModelSetup.ShowServer(viewModel, FakeServers.Crowded());
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
        var main = Open(out var viewModel, DisplayMode.Simplified);
        viewModel.Connection.IsConnecting = true;
        Frame();
        Assert.Equal(1, main.GetVisualDescendants().OfType<LogoMark>().First(logoMark => logoMark.Bounds.Width >= 64).RenderTransform?.Value.M11 ?? 1);
        viewModel.Connection.IsConnecting = false;
        MainViewModelSetup.ShowServer(viewModel, FakeServers.Admin());
        Frame();
        Assert.False(Named<Border>(main, "PageGhost").IsVisible);
        Assert.Equal((0d, 1d), (OffsetY(Named<Panel>(main, "PageHost")), Named<Border>(main, "ServerHeader").Opacity));
        Assert.DoesNotContain(Named<ItemsControl>(main, "ChannelItems").GetRealizedContainers(), control => control.Classes.Contains("entering"));
        main.Close();
    }

    /// <summary>Connected while the drawer is closed (compact): the tree does not build up later when it opens.</summary>
    [AvaloniaFact]
    public void ConnectedWithDrawerClosed_NoLateBuildUp()
    {
        var main = Open(out var viewModel);
        main.Width = 360;
        MotionWait.Settle(100);
        MainViewModelSetup.ShowServer(viewModel, FakeServers.Admin());
        MotionWait.Settle(800); // longer than the build-up waits for a tree to show (500 ms)
        MotionWait.Connected(main);
        var menu = main.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible
            && Avalonia.Automation.AutomationProperties.GetName(button) == OVS.Client.Localization.Strings.Ui_ShowChannels);
        menu.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Frame();
        Frame();
        Assert.DoesNotContain(Named<ItemsControl>(main, "ChannelItems").GetRealizedContainers(), control => control.Classes.Contains("entering"));
        main.Close();
    }
}
