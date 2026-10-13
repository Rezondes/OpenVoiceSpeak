using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OVS.Client.Animations;
using OVS.Client.Audio.Transmit;
using OVS.Client.Controls;
using OVS.Client.Dialogs;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Client.Views;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Users;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Animations;

/// <summary>Package 108: speaking, mute, slots, busy and level states animate while they last, and only then.</summary>
[Collection(MotionCollection.Name)]
public sealed class LiveMotionTests() : TempDirectoryTest("ovs-live-", quiet: true)
{
    MainWindow? opening; // the window being opened, for a look during its build-up

    static void Frame()
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Renders frames for that long and calls <paramref name="look"/> after each one (a frame may come late).</summary>
    static void Watch(int milliseconds, Action look)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < milliseconds)
        {
            Frame();
            look();
            Thread.Sleep(1);
        }
    }

    /// <param name="buildUp">Looks at every frame while the tree builds up after connecting.</param>
    MainWindow Open(out MainViewModel viewModel, out ServerViewModel server, DisplayMode display = DisplayMode.Animated, Action? buildUp = null,
        TransmitMode mode = TransmitMode.PushToTalk)
    {
        new ClientSettings { Display = display, Mode = mode }.Save(dir);
        viewModel = MainViewModelSetup.Create(dir, action => Dispatcher.UIThread.Post(action));
        var main = opening = new MainWindow { DataContext = viewModel, Width = 1100, Height = 700 };
        main.Show();
        MotionWait.Settle(200);
        Motion.Apply(main, display);
        MainViewModelSetup.ShowServer(viewModel, server = FakeServers.Admin());
        var look = buildUp ?? (() => { });
        Watch(700, look); // the tree builds up
        // under load that takes longer: watched on until it is over, no test acts while it still moves
        MotionWait.Until(() =>
        {
            look();
            return MotionWait.IsConnected(main);
        });
        return main;
    }

    /// <summary>
    /// Renders frames and calls <paramref name="look"/> after each one until <paramref name="done"/> holds and at least
    /// <paramref name="atLeast"/> ms have passed (at most 5 s): under load a frame may come late, a fixed time sees too few.
    /// </summary>
    static void WatchUntil(Func<bool> done, Action look, int atLeast = 0) => MotionWait.Until(() =>
    {
        look();
        return done();
    }, atLeast);

    static double Turned(Visual visual) => visual.RenderTransform?.Value.M12 ?? 0;
    static byte GlowAlpha(Border ring) => ring.BoxShadow.Count == 0 ? (byte)0 : ring.BoxShadow[0].Color.A;

    static Border OwnRing(MainWindow main) =>
        main.GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("ring") && border.DataContext is UserViewModel { IsSelf: true });

    static PathIcon StateIcon(MainWindow main, uint session, string cls) =>
        main.GetVisualDescendants().OfType<PathIcon>().First(pathIcon => LiveMotion.GetPop(pathIcon) && pathIcon.Classes.Contains(cls) && pathIcon.DataContext is UserViewModel user && user.SessionId == session);

    static IEnumerable<PathIcon> Ghosts(MainWindow main) =>
        main.GetVisualDescendants().OfType<PathIcon>().Where(pathIcon => pathIcon.Classes.Contains(LiveMotion.GhostClass));

    static TextBlock SlotCounter(MainWindow main, string channel) =>
        main.GetVisualDescendants().OfType<TextBlock>().First(textBlock => textBlock.Classes.Contains("channelCount") && textBlock.DataContext is ChannelViewModel channelViewModel && channelViewModel.Name == channel);

    static UserInfo Anna(bool serverMuted) =>
        new(2, "fp2", "anna", FakeServers.Raid, true, true, serverMuted, Permission.None, [WellKnownGroups.Guest], CanBeModeratedByMe: true);

    static (Window Window, Button Button, BusySpinner Spinner, PathIcon Icon) BusyButton(bool animated)
    {
        var icon = new PathIcon { Width = 14, Height = 14, Data = Geometry.Parse("M0,0 L10,10") };
        var spinner = new BusySpinner { IsVisible = false };
        var button = new Button { Content = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6, Children = { icon, spinner, new TextBlock { Text = "Speichern" } } } };
        var window = new Window { Width = 400, Height = 200, Content = new StackPanel { Margin = new Thickness(20), Children = { button } } };
        window.Show();
        Motion.Apply(window, animated ? DisplayMode.Animated : DisplayMode.Simplified);
        MotionWait.Settle(100);
        return (window, button, spinner, icon);
    }

    static (Window Window, ProgressBar Meter) Meter(bool animated)
    {
        var meter = new ProgressBar { Minimum = -60, Maximum = -10, Height = 8 };
        var window = new Window { Width = 400, Height = 200, Content = new StackPanel { Margin = new Thickness(20), Children = { meter } } };
        window.Show();
        Motion.Apply(window, animated ? DisplayMode.Animated : DisplayMode.Simplified);
        LiveMotion.SetLevel(meter, -60);
        MotionWait.Settle(100);
        MotionWait.Until(() => meter.Value == -60); // from where the meter starts down to the bottom
        return (window, meter);
    }

    [AvaloniaFact]
    public void Speaking_RingBreathesWhileSpeaking_StopsAfter()
    {
        var main = Open(out var viewModel, out var server);
        var ring = OwnRing(main);
        Assert.Equal(0, GlowAlpha(ring));

        server.Tree.SetSelfTransmitting(0); // own channel
        double low = 2, high = 0;
        // a whole 900 ms loop at least, and on until both ends were seen: under load a frame can land anywhere in it
        WatchUntil(() => high > 1.03 && low < 1.02, () =>
        {
            low = Math.Min(low, MotionWait.Scale(ring.Child!));
            high = Math.Max(high, MotionWait.Scale(ring.Child!));
        }, atLeast: 1100);
        Assert.Contains("speaking", ring.Classes);
        Assert.True(high > 1.03 && high <= 1.061, $"the ring breathes up to 1.06 ({high})"); // Package 122: the avatar inside it, the glow stays still
        Assert.True(low < 1.02, $"and back ({low})");
        MotionWait.Eventually(() =>
        {
            Assert.True(GlowAlpha(ring) > 0x80, "and glows");
            Assert.Equal(Color.Parse("#22C55E").ToUInt32() & 0xFFFFFF, ring.BoxShadow[0].Color.ToUInt32() & 0xFFFFFF);
        });

        // every step of the fade as it happens: under load one frame can outlast it
        var glows = MotionWait.Record(ring, visual => GlowAlpha((Border)visual));
        server.Tree.SetSelfTransmitting(null);
        MotionWait.Eventually(() => Assert.Equal((1d, (byte)0), (MotionWait.Scale(ring.Child!), GlowAlpha(ring)))); // the loop stops with the speaking
        Assert.True(glows.Any(a => a is > 0 and < 0x99), "the glow fades out");

        server.Tree.SetSelfTransmitting(OVS.Shared.Voice.VoiceHeader.TargetLinked);
        MotionWait.Eventually(() =>
        {
            Assert.Contains("link", ring.Classes);
            Assert.Equal(Color.Parse("#A78BFA").ToUInt32() & 0xFFFFFF, ring.BoxShadow[0].Color.ToUInt32() & 0xFFFFFF); // in the link colour
        });
        server.Tree.SetSelfTransmitting(null);

        // the push-to-talk indicator pulses while sending
        var transmit = main.GetVisualDescendants().OfType<TextBlock>().First(textBlock => textBlock.Classes.Contains("transmit"));
        viewModel.TransmitText = "Sendet";
        double dim = 1;
        WatchUntil(() => dim < 0.8, () => dim = Math.Min(dim, transmit.Opacity));
        Assert.True(dim < 0.8, $"the indicator pulses ({dim})");
        viewModel.TransmitText = "";
        MotionWait.Eventually(() => Assert.Equal(1, transmit.Opacity));
        main.Close();
    }

    [AvaloniaFact]
    public void MuteIcons_PopInOut()
    {
        bool popped = false;
        var main = Open(out _, out var server, buildUp: () => popped |= opening is { } mainWindow && (Ghosts(mainWindow).Any()
            || mainWindow.GetVisualDescendants().OfType<PathIcon>().Any(pathIcon => (LiveMotion.GetPop(pathIcon) || LiveMotion.GetTurn(pathIcon)) && (pathIcon.Opacity < 1 || MotionWait.Scale(pathIcon) != 1 || Turned(pathIcon) != 0))));
        Assert.False(popped, "nothing pops while the tree is first built");
        var serverMuted = StateIcon(main, 2, "danger");
        Assert.True(serverMuted.IsEffectivelyVisible);

        // the copy taken the moment it is put in the overlay, and every value of it as it happens: under load one frame
        // can outlast the whole pop
        var overlay = OverlayLayer.GetOverlayLayer(main)!;
        var copies = new List<(List<double> Opacity, List<double> Scale)>();
        overlay.Children.CollectionChanged += (_, args) =>
        {
            foreach (var added in args.NewItems?.OfType<PathIcon>() ?? [])
                if (added.Classes.Contains(LiveMotion.GhostClass)) copies.Add((MotionWait.Record(added, visual => visual.Opacity), MotionWait.Record(added, MotionWait.Scale)));
        };
        server.Apply(new UserUpdated(Anna(serverMuted: false)));
        MotionWait.Until(() => copies.Count > 0 && !Ghosts(main).Any());
        Assert.False(serverMuted.IsVisible);
        Assert.True(copies.Any(c => c.Opacity.Any(o => o < 0.9) && c.Scale.Any(s => s < 0.95)), "a copy pops out where the icon was");
        Assert.Empty(Ghosts(main));

        var fades = MotionWait.Record(serverMuted, visual => visual.Opacity);
        var scales = MotionWait.Record(serverMuted, MotionWait.Scale);
        server.Apply(new UserUpdated(Anna(serverMuted: true)));
        MotionWait.Eventually(() =>
        {
            Assert.True(fades.Any(o => o < 0.9) && scales.Any(s => s < 0.95), $"the icon pops in ({fades.DefaultIfEmpty(1).Min()}, {scales.DefaultIfEmpty(1).Min()})");
            Assert.Equal((1d, 1d), (serverMuted.Opacity, MotionWait.Scale(serverMuted)));
        });

        // my own mute button flips its icon with a short rotation
        var micOff = main.GetVisualDescendants().OfType<PathIcon>().Where(LiveMotion.GetTurn).ElementAt(1);
        Assert.False(micOff.IsVisible);
        var turns = MotionWait.Record(micOff, visual => Math.Abs(Turned(visual)));
        server.SelfState.SelfMuted = true;
        MotionWait.Eventually(() =>
        {
            Assert.True(micOff.IsVisible);
            Assert.True(turns.Any(t => t > 0.1), $"the icon turns in ({turns.DefaultIfEmpty(0).Max()})");
            Assert.Equal((0d, 1d), (Turned(micOff), micOff.Opacity), new ToleranceComparer());
        });

        var deafened = main.GetVisualDescendants().OfType<PathIcon>().Where(LiveMotion.GetTurn).ElementAt(3);
        turns = MotionWait.Record(deafened, visual => Math.Abs(Turned(visual)));
        server.SelfState.SelfDeafened = true;
        MotionWait.Eventually(() =>
        {
            Assert.True(deafened.IsVisible);
            Assert.True(turns.Any(t => t > 0.1), $"the deafen icon turns in too ({turns.DefaultIfEmpty(0).Max()})");
            Assert.Equal((0d, 1d), (Turned(deafened), deafened.Opacity), new ToleranceComparer());
        });
        main.Close();
    }

    [AvaloniaFact]
    public void PopOut_OnlyWhereTheIconCanBeSeen()
    {
        var main = Open(out _, out var server);
        Assert.True(StateIcon(main, 2, "danger").IsEffectivelyVisible);

        // a dialog is open: no copy over it
        _ = ConfirmDialog.ShowAsync(main.Overlay, "Weg damit?");
        MotionWait.Settle(300);
        server.Apply(new UserUpdated(Anna(serverMuted: false)));
        bool ghost = false;
        Watch(300, () => ghost |= Ghosts(main).Any());
        Assert.False(ghost, "no copy pops over the dialog");
        main.Overlay.Close();
        server.Apply(new UserUpdated(Anna(serverMuted: true)));
        MotionWait.Settle(400);

        // compact with the drawer closed: the tree cannot be seen, its icons keep their last place
        main.Width = 360;
        MotionWait.Settle(400);
        MotionWait.Eventually(() => Assert.False(main.FindControl<Border>("Sidebar")!.IsEffectivelyVisible));
        server.Apply(new UserUpdated(Anna(serverMuted: false)));
        Watch(300, () => ghost |= Ghosts(main).Any());
        Assert.False(ghost, "no copy pops over the chat");
        main.Close();
    }

    [AvaloniaFact]
    public void SlotCounter_TicksAndPulsesWhenFull()
    {
        var main = Open(out _, out var server);
        var counter = SlotCounter(main, "Raid");
        Assert.Equal("1", counter.Text);

        server.Apply(new ChannelUpdated(new ChannelInfo(FakeServers.Raid, "Raid", "", 1, MaxUsers: 3)));
        MotionWait.Settle(400);
        Assert.Equal("1/3", counter.Text);

        MotionWait.Until(() => (MotionWait.OffsetY(counter), MotionWait.Scale(counter), counter.Opacity) == (0d, 1d, 1d)); // the tick to "1/3" is over
        // every value as it happens: under load one frame can outlast the whole tick
        var rises = MotionWait.Record(counter, MotionWait.OffsetY);
        var pulses = MotionWait.Record(counter, MotionWait.Scale);
        bool Still() => (MotionWait.OffsetY(counter), MotionWait.Scale(counter), counter.Opacity) == (0d, 1d, 1d);
        server.Apply(new UserJoined(new UserInfo(3, "fp3", "bob", FakeServers.Raid, false, false, false, Permission.None, [WellKnownGroups.Guest])));
        MotionWait.Until(() => rises.Any(y => y > 1) && Still(), atLeast: 400);
        Assert.Equal("2/3", counter.Text);
        Assert.True(rises.Any(y => y > 1), $"a higher number comes up from below ({rises.DefaultIfEmpty(0).Max()})");
        Assert.Equal(1, pulses.DefaultIfEmpty(1).Max(), 3); // not full yet: no pulse

        pulses.Clear();
        server.Apply(new UserJoined(new UserInfo(4, "fp4", "cleo", FakeServers.Raid, false, false, false, Permission.None, [WellKnownGroups.Guest])));
        MotionWait.Eventually(() =>
        {
            Assert.Equal("3/3", counter.Text);
            Assert.True(pulses.Any(s => s > 1.1), $"a full channel pulses its counter ({pulses.DefaultIfEmpty(1).Max()})");
            Assert.Equal((0d, 1d, 1d), (MotionWait.OffsetY(counter), MotionWait.Scale(counter), counter.Opacity));
        });

        // once only: it stays full without pulsing again
        server.Apply(new ChannelUpdated(new ChannelInfo(FakeServers.Raid, "Raid", "Voll", 1, MaxUsers: 3)));
        double big = 1;
        Watch(400, () => big = Math.Max(big, MotionWait.Scale(counter)));
        Assert.Equal(1, big, 3);
        main.Close();
    }

    [AvaloniaFact]
    public void LevelMeter_AttackRelease()
    {
        var (window, meter) = Meter(animated: true);
        Assert.Equal(-60, meter.Value);

        LiveMotion.SetLevel(meter, -10);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (meter.Value < -15 && watch.ElapsedMilliseconds < 2000) MotionWait.Settle(1); // 90 % of the way up
        long attack = watch.ElapsedMilliseconds;
        MotionWait.Eventually(() => Assert.Equal(-10, meter.Value));

        LiveMotion.SetLevel(meter, -60);
        bool between = false;
        watch.Restart();
        while (meter.Value > -55 && watch.ElapsedMilliseconds < 2000) // 90 % of the way down
        {
            between |= meter.Value is < -10.5 and > -59.5;
            MotionWait.Settle(1);
        }
        long release = watch.ElapsedMilliseconds;
        Assert.True(between, "the meter moves smoothly instead of stepping");
        Assert.True(release > attack, $"it rises faster than it falls ({attack} ms, {release} ms)");
        Assert.InRange(release, 150, 1000); // about 300 ms
        MotionWait.Eventually(() => Assert.Equal(-60, meter.Value));

        LiveMotion.SetLevel(meter, -90); // below the scale: it rests at the bottom and stops
        MotionWait.Settle(200);
        Assert.Equal(-60, meter.Value);
        window.Close();
    }

    [AvaloniaFact]
    public void LevelMeter_SettingsOpenAtTheLevel()
    {
        var main = Open(out var viewModel, out _, mode: TransmitMode.VoiceActivation);
        viewModel.PageFlow.OpenSettings();
        MotionWait.Settle(500);
        MotionWait.Until(() => !main.FindControl<Border>("PageGhost")!.IsVisible); // the page has come in
        var meter = main.GetVisualDescendants().OfType<ProgressBar>().Single(progressBar => progressBar.Minimum == -60 && progressBar.Maximum == -10);
        Assert.True(meter.IsEffectivelyVisible);
        Assert.Equal(-60, meter.Value);
        viewModel.PageFlow.ClosePage();
        MotionWait.Settle(600); // the hidden meter loses its model
        MotionWait.Until(() => !main.FindControl<Border>("PageGhost")!.IsVisible);

        viewModel.PageFlow.OpenSettings(); // the new model's level is -60 as well
        double high = -60;
        Watch(400, () => high = Math.Max(high, meter.Value));
        Assert.True(high <= -59.9, $"the meter starts at the level, no sweep from full ({high})");

        viewModel.PageFlow.SettingsPage!.InputLevelDb = -30;
        MotionWait.Settle(300);
        LiveMotion.SetLevel(meter, double.NaN); // no level: it keeps what it shows
        MotionWait.Eventually(() => Assert.Equal(-30, meter.Value, 1));
        main.Close();
    }

    [AvaloniaFact]
    public void Busy_ShimmerCrossFadesBack()
    {
        var (window, button, spinner, icon) = BusyButton(animated: true);
        var presenter = button.Presenter!;

        spinner.IsVisible = true;
        icon.IsVisible = false;
        Frame();
        var arc = spinner.GetVisualDescendants().OfType<Arc>().Single(); // a hidden spinner has no content yet
        var bands = new HashSet<double>();
        double faded = 1, sweepLow = 360, sweepHigh = 0;
        // a whole 1.4 s loop at least, and on until all of it was seen: under load a fixed time sees too few frames
        WatchUntil(() => bands.Count > 3 && faded < 1 && sweepHigh - sweepLow > 60, () =>
        {
            if (presenter.OpacityMask is ILinearGradientBrush mask) bands.Add(Math.Round(mask.StartPoint.Point.X, 2));
            faded = Math.Min(faded, spinner.Opacity);
            sweepLow = Math.Min(sweepLow, arc.SweepAngle);
            sweepHigh = Math.Max(sweepHigh, arc.SweepAngle);
        }, atLeast: 1400);
        Assert.Contains(BusySpinner.BusyClass, button.Classes);
        Assert.True(bands.Count > 3, $"a light band passes over the busy button ({bands.Count})");
        Assert.True(faded < 1, "the spinner fades in");
        Assert.True(sweepHigh - sweepLow > 60, "the arc grows and shrinks while it turns");

        var backs = MotionWait.Record(presenter.Child!, visual => visual.Opacity); // as it happens: under load one frame can outlast the fade
        spinner.IsVisible = false;
        icon.IsVisible = true;
        MotionWait.Eventually(() =>
        {
            Assert.DoesNotContain(BusySpinner.BusyClass, button.Classes);
            Assert.True(backs.Any(o => o < 1), "the content fades back");
            Assert.Null(presenter.OpacityMask); // the shimmer ends with the busy state
            Assert.Equal(1, presenter.Child!.Opacity);
        });
        window.Close();
    }

    [AvaloniaFact]
    public void Idle_NoRunningLoops()
    {
        var main = Open(out var viewModel, out var server);
        server.Tree.SetSelfTransmitting(0); // a loop that ran ends with its state
        viewModel.TransmitText = "Sendet";
        MotionWait.Settle(300);
        server.Tree.SetSelfTransmitting(null);
        viewModel.TransmitText = "";
        _ = viewModel.PageFlow.OpenAdminAsync();
        MotionWait.Settle(300);
        viewModel.PageFlow.AdminPage!.GroupList.GroupReorder.Start(); // waits for an answer that never comes: a spinner in the administration
        viewModel.PageFlow.AdminPage.GroupList.GroupSave.Start();
        MotionWait.Settle(400);
        MotionWait.Eventually(() => Assert.Contains(main.GetVisualDescendants().OfType<BusySpinner>(),
            busySpinner => busySpinner.Classes.Contains(BusySpinner.TurningClass) && busySpinner.FindAncestorOfType<AdminView>() is not null));
        viewModel.PageFlow.ClosePage();
        MotionWait.Settle(800);

        // nobody speaks, nothing is busy: nothing on the screen changes from frame to frame
        static string State(Visual visual) => $"{visual.RenderTransform?.Value}|{visual.Opacity}|{visual.OpacityMask}|{(visual as Arc)?.SweepAngle}|{(visual as Border)?.BoxShadow}";
        // under load the page change and the fades take longer: first until nothing has changed for 300 ms (a loop that
        // never stops does not get there, and fails below)
        string Screen() => string.Join("\n", main.GetVisualDescendants().Select(State));
        MotionWait.Until(() =>
        {
            string was = Screen();
            MotionWait.Settle(300);
            return Screen() == was;
        });
        var visuals = main.GetVisualDescendants().ToList();
        Assert.Contains(visuals, visual => visual is BusySpinner { IsVisible: false }); // hidden spinners do not turn
        Assert.Contains(visuals, visual => visual is BusySpinner { IsVisible: true, IsEffectivelyVisible: false }); // nor those in a hidden page
        Assert.DoesNotContain(visuals, visual => visual is BusySpinner busySpinner && busySpinner.Classes.Contains(BusySpinner.TurningClass));
        var before = visuals.Select(State).ToList();
        var changed = new HashSet<string>();
        Watch(1000, () =>
        {
            for (int index = 0; index < visuals.Count; index++)
                if (State(visuals[index]) != before[index]) changed.Add($"{visuals[index].GetType().Name} {string.Join(".", (visuals[index] as StyledElement)?.Classes ?? [])}");
        });
        Assert.Empty(changed);
        main.Close();
    }

    [AvaloniaFact]
    public void Simplified_AsToday()
    {
        var main = Open(out var viewModel, out var server, DisplayMode.Simplified);
        var ring = OwnRing(main);
        server.Tree.SetSelfTransmitting(0);
        var transmit = main.GetVisualDescendants().OfType<TextBlock>().First(textBlock => textBlock.Classes.Contains("transmit"));
        viewModel.TransmitText = "Sendet";
        double high = 1, dim = 1;
        Watch(600, () =>
        {
            high = Math.Max(high, MotionWait.Scale(ring.Child!));
            dim = Math.Min(dim, transmit.Opacity);
        });
        Assert.Contains("speaking", ring.Classes);
        Assert.Equal((1d, (byte)0, 1d), (high, GlowAlpha(ring), dim)); // the ring only changes its colour

        var serverMuted = StateIcon(main, 2, "danger");
        server.Apply(new UserUpdated(Anna(serverMuted: false)));
        bool ghost = false;
        Watch(200, () => ghost |= Ghosts(main).Any());
        server.Apply(new UserUpdated(Anna(serverMuted: true)));
        server.SelfState.SelfMuted = true;
        var micOff = main.GetVisualDescendants().OfType<PathIcon>().Where(LiveMotion.GetTurn).ElementAt(1);
        double faint = 1, turned = 0;
        Watch(200, () =>
        {
            faint = Math.Min(faint, serverMuted.Opacity);
            turned = Math.Max(turned, Math.Abs(Turned(micOff)));
        });
        Assert.False(ghost);
        Assert.Equal((1d, 0d), (faint, turned)); // icons switch

        var counter = SlotCounter(main, "Raid");
        server.Apply(new ChannelUpdated(new ChannelInfo(FakeServers.Raid, "Raid", "", 1, MaxUsers: 1)));
        double moved = 0, big = 1;
        Watch(200, () =>
        {
            moved = Math.Max(moved, Math.Abs(MotionWait.OffsetY(counter)));
            big = Math.Max(big, MotionWait.Scale(counter));
        });
        Assert.Equal("1/1", counter.Text);
        Assert.Equal((0d, 1d), (moved, big));
        main.Close();

        var (window, meter) = Meter(animated: false);
        LiveMotion.SetLevel(meter, -20);
        Assert.Equal(-20, meter.Value); // the level at once

        var (busyWindow, button, spinner, _) = BusyButton(animated: false);
        spinner.IsVisible = true;
        double spinnerFaded = 1;
        bool masked = false;
        Watch(300, () =>
        {
            spinnerFaded = Math.Min(spinnerFaded, spinner.Opacity);
            masked |= button.Presenter!.OpacityMask is not null;
        });
        Assert.Equal((1d, false), (spinnerFaded, masked));
        window.Close();
        busyWindow.Close();
    }

    sealed class ToleranceComparer : IEqualityComparer<(double, double)>
    {
        public bool Equals((double, double) x, (double, double) y) => Math.Abs(x.Item1 - y.Item1) < 0.001 && Math.Abs(x.Item2 - y.Item2) < 0.001;
        public int GetHashCode((double, double) obj) => 0;
    }
}
