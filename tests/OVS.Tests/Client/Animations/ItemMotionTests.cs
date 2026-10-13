using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OVS.Client.Animations;
using OVS.Client.Collections;
using OVS.Client.Models;
using OVS.Client.Services;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Client.Views;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.LogAccess;
using OVS.Shared.Protocol.Messages.Moderation;
using OVS.Shared.Protocol.Messages.ServerBackups;
using OVS.Shared.Protocol.Messages.Users;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Animations;

/// <summary>Package 101: new entries slide in, first fills and rebuilds do not replay it.</summary>
[Collection(MotionCollection.Name)]
public sealed class ItemMotionTests() : TempDirectoryTest("ovs-items-", quiet: true)
{
    static void Frame()
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    static (Window Window, ItemsControl List) Host(ObservableCollection<KeyedRow> rows, bool animated = true, bool flip = false)
    {
        var list = new ItemsControl
        {
            ItemsSource = rows,
            ItemTemplate = new FuncDataTemplate<KeyedRow>((row, _) => new Border { Height = 30, Child = new TextBlock { Text = row?.Key } }),
        };
        ItemMotion.SetEnter(list, true);
        ItemMotion.SetFlip(list, flip);
        var window = new Window { Width = 400, Height = 800, Content = list };
        window.Show();
        Motion.Apply(window, animated ? DisplayMode.Animated : DisplayMode.Simplified);
        MotionWait.Settle(300); // the first fill is over
        return (window, list);
    }

    /// <summary>
    /// Every vertical offset any row of the list takes from now on, with the item it showed: not only what a frame
    /// happens to see, under load one frame can outlast the whole glide.
    /// </summary>
    static List<(object? Item, double Y)> WatchOffsets(ItemsControl items)
    {
        var seen = new List<(object? Item, double Y)>();
        void Watch(Control row)
        {
            void Track(AvaloniaObject transform)
            {
                // the animator keeps the offset in a TranslateTransform, on its own or inside a TransformGroup
                if (transform is Avalonia.Media.TransformGroup group)
                    foreach (var child in group.Children) Track(child);
                if (transform is Avalonia.Media.TranslateTransform translate) seen.Add((row.DataContext, translate.Y)); // set before it came
                transform.PropertyChanged += (_, args) =>
                {
                    if (args.Property == Avalonia.Media.TranslateTransform.YProperty && args.NewValue is double y) seen.Add((row.DataContext, y));
                };
            }
            if (row.RenderTransform is AvaloniaObject now) Track(now);
            row.PropertyChanged += (_, args) =>
            {
                if (args.Property == Visual.RenderTransformProperty && args.NewValue is AvaloniaObject transform) Track(transform);
            };
        }
        foreach (var row in items.GetRealizedContainers()) Watch(row);
        items.ContainerPrepared += (_, args) => Watch(args.Container);
        return seen;
    }

    static double Most(List<(object? Item, double Y)> seen, Func<object?, bool> item) =>
        seen.Where(s => item(s.Item)).Select(s => s.Y).DefaultIfEmpty(0).Max();

    static double Least(List<(object? Item, double Y)> seen, Func<object?, bool> item) =>
        seen.Where(s => item(s.Item)).Select(s => s.Y).DefaultIfEmpty(0).Min();

    [AvaloniaFact]
    public void AdminRowAdded_SlidesIn()
    {
        var rows = new ObservableCollection<KeyedRow> { new("a"), new("b") };
        var (_, list) = Host(rows);
        rows.Insert(1, new KeyedRow("neu"));
        Frame();
        var row = KeyedList.Container(list, "neu");
        Assert.True(row.Opacity < 1, $"fades in ({row.Opacity})");
        Assert.True(MotionWait.OffsetY(row) > 0, "rises from below");
        Assert.True(row.Bounds.Height < 30, $"opens its height ({row.Bounds.Height})");
        MotionWait.Eventually(() =>
        {
            Assert.Equal(1, row.Opacity, 2);
            Assert.Equal(0, MotionWait.OffsetY(row), 2);
            Assert.Equal(30, row.Bounds.Height, 1);
        });
    }

    [AvaloniaFact]
    public void EntriesBelow_MoveSmoothly()
    {
        var rows = new ObservableCollection<KeyedRow> { new("a"), new("b") };
        var (window, list) = Host(rows);
        var below = KeyedList.Container(list, "b");
        double before = below.TranslatePoint(default, window)!.Value.Y;
        // every place it takes on the way, not one frame's: under load one frame can outlast the whole slide
        var tops = MotionWait.Record(below, visual => visual.TranslatePoint(default, window)!.Value.Y);
        rows.Insert(1, new KeyedRow("neu"));
        MotionWait.Until(() => !KeyedList.Container(list, "neu").Classes.Contains("entering"));
        MotionWait.Eventually(() => Assert.Equal(before + 30, below.TranslatePoint(default, window)!.Value.Y, 1));
        double after = below.TranslatePoint(default, window)!.Value.Y;
        Assert.True(tops.Any(during => during > before && during < after), $"{before} < one of {string.Join(" ", tops.Distinct())} < {after}");
    }

    [AvaloniaFact]
    public void FirstFillAndRebuild_NoReplay()
    {
        var rows = new ObservableCollection<KeyedRow> { new("a"), new("b") };
        var list = new ItemsControl
        {
            ItemsSource = rows,
            ItemTemplate = new FuncDataTemplate<KeyedRow>((row, _) => new Border { Height = 30 }),
        };
        ItemMotion.SetEnter(list, true);
        var window = new Window { Width = 400, Height = 600, Content = list };
        window.Show();
        Motion.Apply(window, DisplayMode.Animated);
        Frame();
        Assert.True(list.Opacity < 1, "the first fill fades in as a whole");
        Assert.All(list.GetRealizedContainers(), control => Assert.DoesNotContain("entering", control.Classes));
        MotionWait.Eventually(() => Assert.Equal(1, list.Opacity, 2));

        // the administration rebuilds its lists with new objects
        rows.Clear();
        rows.Add(new KeyedRow("a", 1));
        rows.Add(new KeyedRow("b", 1));
        Frame();
        Assert.All(list.GetRealizedContainers(), control => Assert.DoesNotContain("entering", control.Classes));
        Assert.Equal(1, list.Opacity, 2);

        rows.Add(new KeyedRow("c"));
        Frame();
        Assert.Contains("entering", KeyedList.Container(list, "c").Classes);
    }

    [AvaloniaFact]
    public void EmptyListGetsABatch_FadesAsAWhole()
    {
        var rows = new ObservableCollection<KeyedRow>();
        var (_, list) = Host(rows);
        foreach (var key in new[] { "a", "b", "c" }) rows.Add(new KeyedRow(key));
        Frame();
        Assert.True(list.Opacity < 1);
        Assert.All(list.GetRealizedContainers(), control => Assert.DoesNotContain("entering", control.Classes));
    }

    [AvaloniaFact]
    public void HiddenList_EntriesDoNotSlideInWhenShown()
    {
        var rows = new ObservableCollection<KeyedRow> { new("a") };
        var (_, list) = Host(rows);
        list.IsVisible = false;
        rows.Add(new KeyedRow("b"));
        MotionWait.Settle(50);
        list.IsVisible = true;
        Frame();
        Assert.DoesNotContain("entering", KeyedList.Container(list, "b").Classes);
    }

    [AvaloniaFact]
    public void ManyItems_StaggerCapped()
    {
        var rows = new ObservableCollection<KeyedRow> { new("start") };
        var (_, list) = Host(rows);
        for (int index = 0; index < 12; index++) rows.Add(new KeyedRow($"r{index}"));
        // looked at after every frame until all are in, not at one moment: under load one frame can outlast the stagger
        bool staggered = false;
        MotionWait.Until(() =>
        {
            double first = KeyedList.Container(list, "r0").Opacity, eighth = KeyedList.Container(list, "r7").Opacity, last = KeyedList.Container(list, "r11").Opacity;
            staggered |= first > eighth;
            Assert.Equal(eighth, last, 2); // everything after the 8th step comes with it: never longer than 8 steps plus Motion.Normal
            return rows.All(keyedRow => !KeyedList.Container(list, keyedRow.Key).Classes.Contains("entering"));
        });
        Assert.True(staggered, "staggered: the first one fades in before the eighth");
        Assert.All(rows.Select(keyedRow => KeyedList.Container(list, keyedRow.Key)), control => Assert.Equal(1, control.Opacity, 2));
    }

    [AvaloniaFact]
    public void Simplified_AppearsAtOnce()
    {
        var rows = new ObservableCollection<KeyedRow> { new("a") };
        var (_, list) = Host(rows, animated: false);
        rows.Add(new KeyedRow("b"));
        Frame();
        var row = KeyedList.Container(list, "b");
        Assert.DoesNotContain("entering", row.Classes);
        Assert.Equal(1, row.Opacity);
        Assert.Equal(30, row.Bounds.Height, 1);
    }

    // ---- the channel tree ----

    MainWindow Connected(out ServerViewModel server)
    {
        var viewModel = MainViewModelSetup.Create(dir, action => Dispatcher.UIThread.Post(action));
        var main = new MainWindow { DataContext = viewModel, Width = 1100, Height = 700 };
        main.Show();
        MainViewModelSetup.ShowServer(viewModel, server = FakeServers.Admin());
        MotionWait.Settle(700); // connecting builds the tree up (Package 106)
        MotionWait.Connected(main); // under load that takes longer: no test acts while it still builds up
        return main;
    }

    static Control UserContainer(MainWindow main, string nickname) =>
        main.GetVisualDescendants().OfType<ContentPresenter>().Single(contentPresenter => contentPresenter.DataContext is UserViewModel user && user.Nickname == nickname);

    [AvaloniaFact]
    public void NewUser_SlidesInAndFlashes()
    {
        var main = Connected(out var server);
        server.Apply(new UserJoined(new UserInfo(3, "fp3", "bert", FakeServers.Lobby, false, false, false, Permission.None, [WellKnownGroups.Guest])));
        Frame();
        var bert = UserContainer(main, "bert");
        Assert.Contains("entering", bert.Classes);
        Assert.True(bert.Opacity < 1);
        // the accent flash after sliding in, recorded as it happens: under load one frame can outlast the whole flash
        var row = bert.GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("row"));
        bool flashedAfterSlide = false, lit = false;
        bert.Classes.CollectionChanged += (_, _) =>
            flashedAfterSlide |= bert.Classes.Contains("fresh") && !bert.Classes.Contains("entering") && bert.Opacity == 1;
        row.PropertyChanged += (_, args) => lit |= args.Property == Border.BackgroundProperty && bert.Classes.Contains("fresh")
            && (row.Background as Avalonia.Media.ISolidColorBrush)?.Color != Avalonia.Media.Colors.Transparent;
        MotionWait.Eventually(() => Assert.Equal(1, bert.Opacity, 2));
        MotionWait.Until(() => flashedAfterSlide && !bert.Classes.Contains("fresh"));
        Assert.True(flashedAfterSlide, "the accent flash comes after sliding in");
        Assert.True(lit, "the row lights up in the accent colour");
        Assert.DoesNotContain("fresh", bert.Classes);
        main.Close();
    }

    [AvaloniaFact]
    public void NewChannel_SlidesIn_RowsBelowMoveSmoothly()
    {
        var main = Connected(out var server);
        var raid = Rows.ChannelRow(main, "Raid");
        double before = raid.TranslatePoint(default, main)!.Value.Y;
        // every place Raid takes on the way (its entry in the list moves down), not one frame's
        var tops = MotionWait.Record(ChannelContainer(main, "Raid"), _ => raid.TranslatePoint(default, main)!.Value.Y);
        server.Apply(new ChannelAdded(new ChannelInfo(Guid.NewGuid(), "Archiv", "", 0)));
        MotionWait.Until(() => main.FindControl<ItemsControl>("ChannelItems")!.GetRealizedContainers()
            .Any(control => control.DataContext is ChannelViewModel { Name: "Archiv" } && !control.Classes.Contains("entering")));
        MotionWait.Settle(50); // lays out its last height
        double after = raid.TranslatePoint(default, main)!.Value.Y;
        Assert.True(after > before, "the new channel sits above Raid");
        Assert.True(tops.Any(during => during > before && during < after), $"{before} < one of {string.Join(" ", tops.Distinct())} < {after}");
        main.Close();
    }

    /// <summary>The bookmarks are a new list on every change: only the new bookmark slides in.</summary>
    [AvaloniaFact]
    public void Bookmarks_OnlyTheNewOneSlidesIn()
    {
        var settings = new ClientSettings();
        settings.Bookmarks.Add(new Bookmark("Alt", "alt.example.org", 7000, "ich"));
        settings.Save(dir);
        var viewModel = MainViewModelSetup.Create(dir, action => Dispatcher.UIThread.Post(action));
        var main = new MainWindow { DataContext = viewModel, Width = 1100, Height = 700 };
        main.Show();
        MotionWait.Settle(400);

        var changed = viewModel.Settings;
        changed.Bookmarks.Add(new Bookmark("Neu", "neu.example.org", 7000, "ich"));
        viewModel.ApplySettings(changed);
        Frame();
        var list = main.GetVisualDescendants().OfType<ItemsControl>().Single(itemsControl => itemsControl.ItemsSource is IReadOnlyList<BookmarkItem>);
        Control Tile(string name) => list.GetRealizedContainers().Single(control => control.DataContext is BookmarkItem bookmarkItem && bookmarkItem.Name == name);
        Assert.Contains("entering", Tile("Neu").Classes);
        Assert.DoesNotContain("entering", Tile("Alt").Classes);
        main.Close();
    }

    /// <summary>The administration rebuilds its entries as new objects: the same data must give the same key.</summary>
    [Fact]
    public void RebuiltAdminEntries_KeepTheirKey()
    {
        var now = DateTimeOffset.UtcNow;
        var ban = new BanInfo(Guid.NewGuid(), "fpX", "Xaver", null, "Spam", "mod", null);
        object[] Keys() =>
        [
            new KnownUserViewModel(new KnownUserInfo("fpA", "anna", []), false, [], [], new KnownUserActions(Ban: _ => Task.CompletedTask, Unban: _ => Task.CompletedTask, Delete: _ => Task.CompletedTask, LiftMute: _ => Task.CompletedTask), new PendingActionViewModel(TimeProvider.System)).MotionKey,
            new BanViewModel(ban, now, false, () => Task.CompletedTask, new PendingActionViewModel(TimeProvider.System)).MotionKey,
            new BackupViewModel(new BackupInfo("b1.zip", now, 1, "1.0"), _ => Task.CompletedTask, _ => Task.CompletedTask).MotionKey,
            new LogFileViewModel(new LogFileInfo("server-1", LogKind.Server, null, null, now, now, 1)).MotionKey,
            new LinkRowViewModel(1, FakeServers.Lobby, "Lobby", false, []).MotionKey,
            new GroupEditViewModel(WellKnownGroups.Guest, "Gast", Permission.Speak, Permission.All).MotionKey,
            new BookmarkItem(new Bookmark("A", "a.example.org", 7000, "ich"), null).MotionKey,
        ];
        Assert.Equal(Keys(), Keys());
    }

    // ---- Package 102: leaving ----

    [AvaloniaFact]
    public void DeletedChannel_CollapsesThenGone()
    {
        var main = Connected(out var server);
        Assert.Equal(Motion.Normal, server.Leave.Delay); // the window set the animated display's length
        var raid = server.Tree.Channels.Single(channel => channel.Name == "Raid");
        var row = main.FindControl<ItemsControl>("ChannelItems")!.ContainerFromItem(raid)!;
        double height = row.Bounds.Height;
        // how far it folds while it is still in the list, recorded as it happens: the list lets it go after Motion.Normal
        // of real time, and under load one frame can take as long
        double least = height, faintest = 1;
        row.PropertyChanged += (_, _) =>
        {
            if (!server.Tree.Channels.Contains(raid)) return;
            least = Math.Min(least, row.Bounds.Height);
            faintest = Math.Min(faintest, row.Opacity);
        };
        server.Apply(new ChannelRemoved(FakeServers.Raid));
        Dispatcher.UIThread.RunJobs(); // the fold starts on the UI thread
        Assert.Contains(raid, server.Tree.Channels); // still there, folding away
        Assert.Contains("leaving", row.Classes);
        Assert.False(row.IsHitTestVisible);
        MotionWait.Until(() => !server.Tree.Channels.Contains(raid));
        Assert.True(least < height && faintest < 1, $"{least} of {height}, {faintest}");
        Assert.DoesNotContain(raid, server.Tree.Channels);
        main.Close();
    }

    [AvaloniaFact]
    public void AdminRowRemoved_Leaves_AndComesBackWhole()
    {
        var rows = new ObservableCollection<KeyedRow> { new("a"), new("b"), new("c") };
        var (window, list) = Host(rows);
        var leave = new LeaveTimer(TimeProvider.System, () => action => Dispatcher.UIThread.Post(action)) { Delay = Motion.Normal };
        var below = KeyedList.Container(list, "c");
        double before = below.TranslatePoint(default, window)!.Value.Y;
        // what the rows do while b folds, recorded as it happens: the list lets it go after Motion.Normal of real time,
        // and under load one frame can take as long
        var control = KeyedList.Container(list, "b");
        var faint = MotionWait.Record(control, visual => visual.Opacity);
        var drift = MotionWait.Record(control, MotionWait.OffsetX);
        var tops = new List<double>(); // c's place after every layout pass, whichever container shows it
        list.LayoutUpdated += (_, _) =>
        {
            if (list.GetRealizedContainers().FirstOrDefault(otherControl => list.ItemFromContainer(otherControl) is KeyedRow { Key: "c" }) is { } c)
                tops.Add(c.TranslatePoint(default, window)!.Value.Y);
        };
        bool Folds() => faint.Any(o => o < 1) && drift.Any(x => x < 0);
        CollectionSync.Sync(rows, [new KeyedRow("a", 1), new KeyedRow("c", 1)], leave);
        MotionWait.Until(() => (Folds() && tops.Any(top => top < before)) || !rows.Any(keyedRow => keyedRow.Key == "b"));
        Assert.True(Folds(), "fades and drifts to the left");
        Assert.True(tops.Any(during => during < before), "the rows below move up while it folds");

        CollectionSync.Sync(rows, [new KeyedRow("a", 2), new KeyedRow("b", 2), new KeyedRow("c", 2)], leave); // back meanwhile
        MotionWait.Eventually(() =>
        {
            var back = KeyedList.Container(list, "b");
            Assert.Equal(1, back.Opacity, 2);
            Assert.Equal(30, back.Bounds.Height, 1);
            Assert.True(back.IsHitTestVisible);
            Assert.Equal(3, rows.Count);
        });
    }

    [AvaloniaFact]
    public void Simplified_RemovedAtOnce()
    {
        var main = Connected(out var server);
        Motion.Apply(main, DisplayMode.Simplified);
        ((MainViewModel)main.DataContext!).Appearance = ((MainViewModel)main.DataContext!).Appearance with { Display = DisplayMode.Simplified };
        Assert.Equal(TimeSpan.Zero, server.Leave.Delay);
        server.Apply(new ChannelRemoved(FakeServers.Raid));
        Assert.DoesNotContain(server.Tree.Channels, channel => channel.Name == "Raid");
        main.Close();
    }

    // ---- Package 103: moves ----

    static Control ChannelContainer(MainWindow main, string name) =>
        main.FindControl<ItemsControl>("ChannelItems")!.GetRealizedContainers().Single(control => control.DataContext is ChannelViewModel ch && ch.Name == name);

    static IEnumerable<Border> Ghosts(Visual main, string kind) =>
        Avalonia.Controls.Primitives.OverlayLayer.GetOverlayLayer(main)!.Children.OfType<Border>().Where(border => border.Classes.Contains(kind));

    static UserInfo Anna(Guid channel) => new(2, "fp2", "anna", channel, true, true, true, Permission.None, [WellKnownGroups.Guest], CanBeModeratedByMe: true);

    [AvaloniaFact]
    public void ServerReorder_RowsGlideFromOldOffset()
    {
        var main = Connected(out var server);
        var raid = ChannelContainer(main, "Raid");
        double before = raid.TranslatePoint(default, main)!.Value.Y;
        var seen = WatchOffsets(main.FindControl<ItemsControl>("ChannelItems")!);
        server.Apply(new ChannelUpdated(new ChannelInfo(FakeServers.Raid, "Raid", "", -1))); // another admin puts Raid first
        Frame();
        raid = ChannelContainer(main, "Raid"); // a moved entry gets a new row
        double now = raid.TranslatePoint(default, main)!.Value.Y - MotionWait.OffsetY(raid);
        Assert.True(now < before, "Raid is first now");
        for (int index = 0; index < 5; index++) Frame();
        double most = Math.Max(MotionWait.OffsetY(raid), Most(seen, item => item is ChannelViewModel { Name: "Raid" }));
        Assert.True(most > 0.5 * (before - now), $"it starts where it was: {most} of {before - now}");
        MotionWait.Eventually(() => Assert.Equal(0, MotionWait.OffsetY(raid), 2));
        main.Close();
    }

    /// <summary>The row of a user in the list they came into (not the one folding away in the old channel).</summary>
    static Control Arrived(MainWindow main, string nickname) =>
        main.GetVisualDescendants().OfType<ContentPresenter>().Single(contentPresenter => contentPresenter.DataContext is UserViewModel user && user.Nickname == nickname && !contentPresenter.Classes.Contains("leaving"));

    /// <summary>Follows a flying ghost until it lands; returns where it was last seen (overlay coordinates).</summary>
    static double LastTop(Border ghost, out double first)
    {
        first = Canvas.GetTop(ghost);
        double last = first;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (ghost.Parent is not null && watch.ElapsedMilliseconds < 2000)
        {
            last = Canvas.GetTop(ghost);
            Frame();
            Thread.Sleep(1);
        }
        return Canvas.GetTop(ghost);
    }

    [AvaloniaTheory]
    [InlineData("anna", true)] // anna moves up from Raid to the Lobby
    [InlineData("ich", false)] // I move down from the Lobby to Raid, while my old row above still folds
    public void UserSwitch_GhostFliesOldToNew_LandsOnTheRow(string nickname, bool up)
    {
        var main = Connected(out var server);
        var overlay = Avalonia.Controls.Primitives.OverlayLayer.GetOverlayLayer(main)!;
        double oldTop = UserContainer(main, nickname).TranslatePoint(default, overlay)!.Value.Y;
        // the picture taken the moment it is put in the overlay: where it starts, and how the new row waits for it then.
        // Frames may come late under load, a later look could find the flight over
        Border? ghost = null;
        double? start = null, waiting = null;
        int pictures = 0;
        overlay.Children.CollectionChanged += (_, args) =>
        {
            foreach (var added in args.NewItems?.OfType<Border>() ?? [])
                if (added.Classes.Contains(FlyGhost.GhostClass))
                {
                    pictures++;
                    ghost ??= added;
                    start ??= Canvas.GetTop(added);
                    waiting ??= Arrived(main, nickname).Opacity;
                }
        };
        server.Apply(new UserUpdated(nickname == "anna" ? Anna(FakeServers.Lobby)
            : new UserInfo(1, "fp1", "ich", FakeServers.Raid, false, false, false, Permission.All, [WellKnownGroups.Admin])));
        MotionWait.Until(() => ghost is not null);
        Assert.Equal(1, pictures);
        Assert.True(waiting == 0, $"the new row waits for its picture ({waiting})");
        double landed = LastTop(ghost!, out _);
        Assert.True(start is { } s && Math.Abs(s - oldTop) < 1, $"starts on the old row: {start} vs {oldTop}");
        Assert.Empty(Ghosts(main, FlyGhost.GhostClass));
        Assert.Equal(1, Arrived(main, nickname).Opacity, 2); // shown in the frame the picture went: no gap
        Assert.DoesNotContain("fresh", Arrived(main, nickname).Classes); // and no flash, the flight showed where it went
        MotionWait.Eventually(() =>
        {
            var row = Arrived(main, nickname);
            Assert.Equal(row.TranslatePoint(default, overlay)!.Value.Y, landed, 1); // landed exactly on the new row
            Assert.Equal(1, row.Opacity, 2); // shows once the picture has landed
        });
        Assert.Equal(up, landed < oldTop);
        main.Close();
    }

    [AvaloniaFact]
    public void OwnSwitch_CurrentHighlightSlides()
    {
        var main = Connected(out var server);
        var overlay = Avalonia.Controls.Primitives.OverlayLayer.GetOverlayLayer(main)!;
        var raidRow = ChannelContainer(main, "Raid").GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("row"));
        // the highlight as it is put in the overlay, and Raid's row while it waits for it, recorded as it happens: under
        // load one frame can outlast the whole flight
        Border? highlight = null;
        int highlights = 0;
        overlay.Children.CollectionChanged += (_, args) =>
        {
            foreach (var added in args.NewItems?.OfType<Border>() ?? [])
                if (added.Classes.Contains(FlyGhost.HighlightClass))
                {
                    highlights++;
                    highlight ??= added;
                }
        };
        bool arriving = false;
        Avalonia.Media.Color? waited = null; // the row's colour while it waits, as last seen before the hand-over
        void Look()
        {
            if (!raidRow.Classes.Contains(FlyGhost.ArrivingClass)) return;
            arriving = true;
            waited = (raidRow.Background as Avalonia.Media.ISolidColorBrush)?.Color;
        }
        raidRow.Classes.CollectionChanged += (_, _) => Look();
        raidRow.PropertyChanged += (_, args) =>
        {
            if (args.Property == Border.BackgroundProperty) Look();
        };
        server.Apply(new UserUpdated(new UserInfo(1, "fp1", "ich", FakeServers.Raid, false, false, false, Permission.All, [WellKnownGroups.Admin])));
        MotionWait.Until(() => highlight is not null);
        Assert.Equal(1, highlights);
        double landed = LastTop(highlight!, out _);
        Assert.Empty(Ghosts(main, FlyGhost.HighlightClass));
        Assert.True(arriving, "Raid's row waits for the highlight");
        Assert.Equal(Avalonia.Media.Colors.Transparent, waited); // its own colour stays away until the hand-over
        Assert.DoesNotContain(FlyGhost.ArrivingClass, raidRow.Classes); // handed over before the ghost went: no gap
        // lands on Raid although my old row above folded meanwhile
        MotionWait.Eventually(() => Assert.Equal(raidRow.TranslatePoint(default, overlay)!.Value.Y, landed, 1));
        Assert.DoesNotContain(FlyGhost.ArrivingClass, raidRow.Classes);
        main.Close();
    }

    [AvaloniaFact]
    public void GroupReorder_Glides()
    {
        var rows = new ObservableCollection<KeyedRow> { new("Gast"), new("Moderator"), new("Admin") };
        var (window, list) = Host(rows, flip: true);
        var seen = WatchOffsets(list);
        rows.Move(2, 0);
        for (int index = 0; index < 5; index++) Frame();
        double admin = Most(seen, item => item is KeyedRow { Key: "Admin" });
        double gast = Least(seen, item => item is KeyedRow { Key: "Gast" });
        Assert.True(admin > 30, $"Admin glides up from 60 px below ({admin})");
        Assert.True(gast < -15, $"the others make room, gliding as well ({gast})");
        MotionWait.Eventually(() => Assert.Equal(0, MotionWait.OffsetY(KeyedList.Container(list, "Admin")), 2));
    }

    /// <summary>
    /// Package 122: a list notes its places only around changes. A row that grew meanwhile (no change of the list) moved
    /// the ones below; a later reorder must glide from where they are now, not from where they were noted.
    /// </summary>
    [AvaloniaFact]
    public void ReflowWhileIdle_ThenReorder_OnlyMovedRowsGlide()
    {
        var rows = new ObservableCollection<KeyedRow> { new("a"), new("b"), new("c"), new("d") };
        var (_, list) = Host(rows, flip: true);
        MotionWait.Until(() => !ItemMotion.TracksPlaces(list));
        Assert.False(ItemMotion.TracksPlaces(list));
        KeyedList.Container(list, "a").GetVisualDescendants().OfType<Border>().First().Height = 60; // b, c and d move down by 30
        MotionWait.Until(() => KeyedList.Container(list, "b").Bounds.Y == 60);
        Assert.False(ItemMotion.TracksPlaces(list));

        var seen = WatchOffsets(list);
        rows.Move(3, 2); // c and d swap, b stays where it is
        for (int index = 0; index < 5; index++) Frame();
        Assert.True(Most(seen, item => item is KeyedRow { Key: "d" }) > 15, "d glides up from below");
        Assert.Equal(0, seen.Where(s => s.Item is KeyedRow { Key: "b" }).Select(s => Math.Abs(s.Y)).DefaultIfEmpty(0).Max(), 1);
        MotionWait.Eventually(() => Assert.Equal(0, MotionWait.OffsetY(KeyedList.Container(list, "d")), 2));
    }

    [AvaloniaFact]
    public void OffscreenEnd_NoGhost()
    {
        var viewModel = MainViewModelSetup.Create(dir, action => Dispatcher.UIThread.Post(action));
        var main = new MainWindow { DataContext = viewModel, Width = 1100, Height = 400 };
        main.Show();
        var server = FakeServers.Crowded();
        MainViewModelSetup.ShowServer(viewModel, server);
        MotionWait.Settle(400);
        MotionWait.Connected(main);
        // every picture put in the overlay counts, not only one a frame finds there: under load a flight is over within a frame
        int pictures = 0;
        Avalonia.Controls.Primitives.OverlayLayer.GetOverlayLayer(main)!.Children.CollectionChanged += (_, args) =>
            pictures += args.NewItems?.OfType<Border>().Count(border => border.Classes.Contains(FlyGhost.GhostClass)) ?? 0;
        var lobby = server.Tree.Channels.First();
        var far = server.Tree.Channels.Last().Users.First(); // in the last channel, scrolled out of view
        server.Apply(new UserUpdated(server.Mirror.Users[far.SessionId] with { ChannelId = lobby.Id }));
        MotionWait.Settle(60);
        Assert.Equal(0, pictures);

        // and the other way round: from the Lobby into a channel scrolled out of view
        var mine = server.Tree.Channels.First().Users.First(user => !user.IsSelf);
        server.Apply(new UserUpdated(server.Mirror.Users[mine.SessionId] with { ChannelId = server.Tree.Channels.Last().Id }));
        MotionWait.Settle(60);
        Assert.Equal(0, pictures);
        main.Close();
    }

    /// <summary>The group list of the administration really glides (the attribute is there).</summary>
    [AvaloniaFact]
    public async Task AdminGroupList_Glides()
    {
        var viewModel = MainViewModelSetup.Create(dir, action => Dispatcher.UIThread.Post(action));
        var main = new MainWindow { DataContext = viewModel, Width = 1100, Height = 700 };
        main.Show();
        MainViewModelSetup.ShowServer(viewModel, FakeServers.Admin());
        await viewModel.PageFlow.OpenAdminAsync();
        MotionWait.Settle(400);
        MotionWait.Until(() => !main.FindControl<Border>("PageGhost")!.IsVisible); // the page has come in: the list stands still
        var groups = main.GetVisualDescendants().OfType<ListBox>().Single(listBox => listBox.Name == "GroupRows");
        Assert.True(ItemMotion.GetFlip(groups));
        var admin = viewModel.PageFlow.AdminPage!;
        admin.GroupList.SelectedGroup = admin.GroupList.Groups.Single(groupEdit => groupEdit.Name == "Admin");
        var seen = WatchOffsets(groups);
        await HangGuard.Bound(admin.GroupList.MoveGroupUpCommand.ExecuteAsync(null));
        for (int index = 0; index < 5; index++) Frame();
        Assert.True(Most(seen, item => item is GroupEditViewModel { Name: "Admin" }) > 0, "Admin glides up from below");
        main.Close();
    }

    [AvaloniaFact]
    public void Simplified_Jumps()
    {
        var main = Connected(out var server);
        var viewModel = (MainViewModel)main.DataContext!;
        viewModel.Appearance = viewModel.Appearance with { Display = DisplayMode.Simplified };
        server.Apply(new ChannelUpdated(new ChannelInfo(FakeServers.Raid, "Raid", "", -1)));
        server.Apply(new UserUpdated(Anna(FakeServers.Lobby)));
        Frame();
        Assert.Equal(0, MotionWait.OffsetY(ChannelContainer(main, "Raid")));
        MotionWait.Settle(60);
        Assert.Empty(Ghosts(main, FlyGhost.GhostClass));
        main.Close();
    }

    /// <summary>A row back after its fold has finished is whole again (a finished fold keeps its last values).</summary>
    [AvaloniaFact]
    public void BackAfterTheFoldFinished_IsWhole()
    {
        var rows = new ObservableCollection<KeyedRow> { new("a"), new("b") };
        var (_, list) = Host(rows);
        var leave = new LeaveTimer(TimeProvider.System, () => action => Dispatcher.UIThread.Post(action)) { Delay = TimeSpan.FromSeconds(5) }; // stays long after its fold
        CollectionSync.Sync(rows, [new KeyedRow("a", 1)], leave);
        MotionWait.Eventually(() => Assert.Equal(0, KeyedList.Container(list, "b").Opacity, 2)); // folded
        CollectionSync.Sync(rows, [new KeyedRow("a", 2), new KeyedRow("b", 2)], leave);
        MotionWait.Eventually(() =>
        {
            var control = KeyedList.Container(list, "b");
            Assert.Equal(1, control.Opacity, 2);
            Assert.Equal(30, control.Bounds.Height, 1);
            Assert.Equal(0, MotionWait.OffsetX(control));
        });
    }
}
