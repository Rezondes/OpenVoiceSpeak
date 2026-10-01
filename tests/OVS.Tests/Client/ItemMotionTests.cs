using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Client.Views;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client;

/// <summary>Package 101: new entries slide in, first fills and rebuilds do not replay it.</summary>
public sealed class ItemMotionTests : IDisposable
{
    readonly string dir = Directory.CreateTempSubdirectory("ovs-items-").FullName;

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

    /// <summary>An entry of a rebuilt list: new objects, the same key.</summary>
    sealed record Row(string Key, int Version = 0) : IMotionKey
    {
        public object MotionKey => Key;
    }

    /// <summary>The animation clock runs on real time.</summary>
    static void Settle(int milliseconds)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < milliseconds)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(1); // leaves the CPU to the timing tests running beside
        }
    }

    static void Frame()
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    static (Window Window, ItemsControl List) Host(ObservableCollection<Row> rows, bool animated = true, bool flip = false)
    {
        var list = new ItemsControl
        {
            ItemsSource = rows,
            ItemTemplate = new FuncDataTemplate<Row>((row, _) => new Border { Height = 30, Child = new TextBlock { Text = row?.Key } }),
        };
        ItemMotion.SetEnter(list, true);
        ItemMotion.SetFlip(list, flip);
        var window = new Window { Width = 400, Height = 800, Content = list };
        window.Show();
        Motion.Apply(window, animated ? DisplayMode.Animated : DisplayMode.Simplified);
        Settle(300); // the first fill is over
        return (window, list);
    }

    static Control Container(ItemsControl list, string key) =>
        list.GetRealizedContainers().Single(c => c.DataContext is Row row && row.Key == key);

    static double OffsetY(Visual visual) => visual.RenderTransform?.Value.M32 ?? 0;

    [AvaloniaFact]
    public void AdminRowAdded_SlidesIn()
    {
        var rows = new ObservableCollection<Row> { new("a"), new("b") };
        var (_, list) = Host(rows);
        rows.Insert(1, new Row("neu"));
        Frame();
        var row = Container(list, "neu");
        Assert.True(row.Opacity < 1, $"fades in ({row.Opacity})");
        Assert.True(OffsetY(row) > 0, "rises from below");
        Assert.True(row.Bounds.Height < 30, $"opens its height ({row.Bounds.Height})");
        Settle(400);
        Assert.Equal(1, row.Opacity, 2);
        Assert.Equal(0, OffsetY(row), 2);
        Assert.Equal(30, row.Bounds.Height, 1);
    }

    [AvaloniaFact]
    public void EntriesBelow_MoveSmoothly()
    {
        var rows = new ObservableCollection<Row> { new("a"), new("b") };
        var (window, list) = Host(rows);
        var below = Container(list, "b");
        double before = below.TranslatePoint(default, window)!.Value.Y;
        rows.Insert(1, new Row("neu"));
        Settle(80);
        double during = below.TranslatePoint(default, window)!.Value.Y;
        Settle(400);
        double after = below.TranslatePoint(default, window)!.Value.Y;
        Assert.Equal(before + 30, after, 1);
        Assert.True(during > before && during < after, $"{before} < {during} < {after}");
    }

    [AvaloniaFact]
    public void FirstFillAndRebuild_NoReplay()
    {
        var rows = new ObservableCollection<Row> { new("a"), new("b") };
        var list = new ItemsControl
        {
            ItemsSource = rows,
            ItemTemplate = new FuncDataTemplate<Row>((row, _) => new Border { Height = 30 }),
        };
        ItemMotion.SetEnter(list, true);
        var window = new Window { Width = 400, Height = 600, Content = list };
        window.Show();
        Motion.Apply(window, DisplayMode.Animated);
        Frame();
        Assert.True(list.Opacity < 1, "the first fill fades in as a whole");
        Assert.All(list.GetRealizedContainers(), c => Assert.DoesNotContain("entering", c.Classes));
        Settle(300);
        Assert.Equal(1, list.Opacity, 2);

        // the administration rebuilds its lists with new objects
        rows.Clear();
        rows.Add(new Row("a", 1));
        rows.Add(new Row("b", 1));
        Frame();
        Assert.All(list.GetRealizedContainers(), c => Assert.DoesNotContain("entering", c.Classes));
        Assert.Equal(1, list.Opacity, 2);

        rows.Add(new Row("c"));
        Frame();
        Assert.Contains("entering", Container(list, "c").Classes);
    }

    [AvaloniaFact]
    public void EmptyListGetsABatch_FadesAsAWhole()
    {
        var rows = new ObservableCollection<Row>();
        var (_, list) = Host(rows);
        foreach (var key in new[] { "a", "b", "c" }) rows.Add(new Row(key));
        Frame();
        Assert.True(list.Opacity < 1);
        Assert.All(list.GetRealizedContainers(), c => Assert.DoesNotContain("entering", c.Classes));
    }

    [AvaloniaFact]
    public void HiddenList_EntriesDoNotSlideInWhenShown()
    {
        var rows = new ObservableCollection<Row> { new("a") };
        var (_, list) = Host(rows);
        list.IsVisible = false;
        rows.Add(new Row("b"));
        Settle(50);
        list.IsVisible = true;
        Frame();
        Assert.DoesNotContain("entering", Container(list, "b").Classes);
    }

    [AvaloniaFact]
    public void ManyItems_StaggerCapped()
    {
        var rows = new ObservableCollection<Row> { new("start") };
        var (_, list) = Host(rows);
        for (int i = 0; i < 12; i++) rows.Add(new Row($"r{i}"));
        Settle(110);
        double first = Container(list, "r0").Opacity, eighth = Container(list, "r7").Opacity, last = Container(list, "r11").Opacity;
        Assert.True(first > eighth, $"staggered: {first} > {eighth}");
        Assert.Equal(eighth, last, 2); // everything after the 8th step comes with it
        Settle(8 * ItemMotion.StaggerMs + 300); // never longer than 8 steps plus Motion.Normal
        Assert.All(rows.Select(r => Container(list, r.Key)), c => Assert.Equal(1, c.Opacity, 2));
    }

    [AvaloniaFact]
    public void Simplified_AppearsAtOnce()
    {
        var rows = new ObservableCollection<Row> { new("a") };
        var (_, list) = Host(rows, animated: false);
        rows.Add(new Row("b"));
        Frame();
        var row = Container(list, "b");
        Assert.DoesNotContain("entering", row.Classes);
        Assert.Equal(1, row.Opacity);
        Assert.Equal(30, row.Bounds.Height, 1);
    }

    // ---- the channel tree ----

    MainWindow Connected(out ServerViewModel server)
    {
        var vm = new MainViewModel(dir, a => Dispatcher.UIThread.Post(a), useAudioDevices: false);
        var main = new MainWindow { DataContext = vm, Width = 1100, Height = 700 };
        main.Show();
        vm.Server = server = FakeServers.Admin();
        Settle(700); // connecting builds the tree up (Package 106)
        return main;
    }

    static Control UserContainer(MainWindow main, string nickname) =>
        main.GetVisualDescendants().OfType<ContentPresenter>().Single(c => c.DataContext is UserViewModel u && u.Nickname == nickname);

    [AvaloniaFact]
    public void NewUser_SlidesInAndFlashes()
    {
        var main = Connected(out var server);
        server.Apply(new UserJoined(new UserInfo(3, "fp3", "bert", FakeServers.Lobby, false, false, false, Permission.None, [WellKnownGroups.Guest])));
        Frame();
        var bert = UserContainer(main, "bert");
        Assert.Contains("entering", bert.Classes);
        Assert.True(bert.Opacity < 1);
        Settle(400);
        Assert.Equal(1, bert.Opacity, 2);
        Assert.Contains("fresh", bert.Classes); // the accent flash after sliding in
        var row = bert.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("row"));
        Settle(100);
        Assert.NotEqual(Avalonia.Media.Colors.Transparent, (row.Background as Avalonia.Media.ISolidColorBrush)?.Color);
        Settle(ItemMotion.FlashMs);
        Assert.DoesNotContain("fresh", bert.Classes);
        main.Close();
    }

    [AvaloniaFact]
    public void NewChannel_SlidesIn_RowsBelowMoveSmoothly()
    {
        var main = Connected(out var server);
        var raid = main.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("row") && b.DataContext is ChannelViewModel { Name: "Raid" });
        double before = raid.TranslatePoint(default, main)!.Value.Y;
        server.Apply(new ChannelAdded(new ChannelInfo(Guid.NewGuid(), "Archiv", "", 0)));
        Settle(80);
        double during = raid.TranslatePoint(default, main)!.Value.Y;
        Settle(500);
        double after = raid.TranslatePoint(default, main)!.Value.Y;
        Assert.True(after > before, "the new channel sits above Raid");
        Assert.True(during > before && during < after, $"{before} < {during} < {after}");
        main.Close();
    }

    /// <summary>The bookmarks are a new list on every change: only the new bookmark slides in.</summary>
    [AvaloniaFact]
    public void Bookmarks_OnlyTheNewOneSlidesIn()
    {
        var settings = new ClientSettings();
        settings.Bookmarks.Add(new Bookmark("Alt", "alt.example.org", 7000, "ich"));
        settings.Save(dir);
        var vm = new MainViewModel(dir, a => Dispatcher.UIThread.Post(a), useAudioDevices: false);
        var main = new MainWindow { DataContext = vm, Width = 1100, Height = 700 };
        main.Show();
        Settle(400);

        var changed = vm.Settings;
        changed.Bookmarks.Add(new Bookmark("Neu", "neu.example.org", 7000, "ich"));
        vm.ApplySettings(changed);
        Frame();
        var list = main.GetVisualDescendants().OfType<ItemsControl>().Single(l => l.ItemsSource is IReadOnlyList<BookmarkItem>);
        Control Tile(string name) => list.GetRealizedContainers().Single(c => c.DataContext is BookmarkItem b && b.Name == name);
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
            new KnownUserViewModel(new KnownUserInfo("fpA", "anna", []), false, [], []).MotionKey,
            new BanViewModel(ban, now, false, () => Task.CompletedTask).MotionKey,
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
        var raid = server.Channels.Single(c => c.Name == "Raid");
        var row = main.FindControl<ItemsControl>("ChannelItems")!.ContainerFromItem(raid)!;
        double height = row.Bounds.Height;
        server.Apply(new ChannelRemoved(FakeServers.Raid));
        Settle(100);
        Assert.Contains(raid, server.Channels); // still there, folding away
        Assert.Contains("leaving", row.Classes);
        Assert.False(row.IsHitTestVisible);
        Assert.True(row.Bounds.Height < height && row.Opacity < 1, $"{row.Bounds.Height} of {height}, {row.Opacity}");
        Settle(400);
        Assert.DoesNotContain(raid, server.Channels);
        main.Close();
    }

    [AvaloniaFact]
    public void AdminRowRemoved_Leaves_AndComesBackWhole()
    {
        var rows = new ObservableCollection<Row> { new("a"), new("b"), new("c") };
        var (window, list) = Host(rows);
        var leave = new LeaveTimer(TimeProvider.System, () => a => Dispatcher.UIThread.Post(a)) { Delay = Motion.Normal };
        var below = Container(list, "c");
        double before = below.TranslatePoint(default, window)!.Value.Y;
        CollectionSync.Sync(rows, [new Row("a", 1), new Row("c", 1)], leave);
        Settle(100);
        var b = Container(list, "b");
        Assert.True(b.Opacity < 1 && OffsetX(b) < 0, "fades and drifts to the left");
        double during = Container(list, "c").TranslatePoint(default, window)!.Value.Y; // a rebuilt row: a new container
        Assert.True(during < before, "the rows below move up while it folds");

        CollectionSync.Sync(rows, [new Row("a", 2), new Row("b", 2), new Row("c", 2)], leave); // back meanwhile
        Settle(400);
        var back = Container(list, "b");
        Assert.Equal(1, back.Opacity, 2);
        Assert.Equal(30, back.Bounds.Height, 1);
        Assert.True(back.IsHitTestVisible);
        Assert.Equal(3, rows.Count);
    }

    [AvaloniaFact]
    public void Simplified_RemovedAtOnce()
    {
        var main = Connected(out var server);
        Motion.Apply(main, DisplayMode.Simplified);
        ((MainViewModel)main.DataContext!).Appearance = ((MainViewModel)main.DataContext!).Appearance with { Display = DisplayMode.Simplified };
        Assert.Equal(TimeSpan.Zero, server.Leave.Delay);
        server.Apply(new ChannelRemoved(FakeServers.Raid));
        Assert.DoesNotContain(server.Channels, c => c.Name == "Raid");
        main.Close();
    }

    static double OffsetX(Visual visual) => visual.RenderTransform?.Value.M31 ?? 0;

    // ---- Package 103: moves ----

    static Control ChannelContainer(MainWindow main, string name) =>
        main.FindControl<ItemsControl>("ChannelItems")!.GetRealizedContainers().Single(c => c.DataContext is ChannelViewModel ch && ch.Name == name);

    static IEnumerable<Border> Ghosts(Visual main, string kind) =>
        Avalonia.Controls.Primitives.OverlayLayer.GetOverlayLayer(main)!.Children.OfType<Border>().Where(b => b.Classes.Contains(kind));

    static UserInfo Anna(Guid channel) => new(2, "fp2", "anna", channel, true, true, true, Permission.None, [WellKnownGroups.Guest], CanBeModeratedByMe: true);

    [AvaloniaFact]
    public void ServerReorder_RowsGlideFromOldOffset()
    {
        var main = Connected(out var server);
        var raid = ChannelContainer(main, "Raid");
        double before = raid.TranslatePoint(default, main)!.Value.Y;
        server.Apply(new ChannelUpdated(new ChannelInfo(FakeServers.Raid, "Raid", "", -1))); // another admin puts Raid first
        Frame();
        raid = ChannelContainer(main, "Raid"); // a moved entry gets a new row
        double now = raid.TranslatePoint(default, main)!.Value.Y - OffsetY(raid);
        Assert.True(now < before, "Raid is first now");
        Assert.True(OffsetY(raid) > 0.75 * (before - now), $"it starts where it was: {OffsetY(raid)} of {before - now}"); // a frame may have passed
        Settle(500);
        Assert.Equal(0, OffsetY(raid), 2);
        main.Close();
    }

    /// <summary>The row of a user in the list they came into (not the one folding away in the old channel).</summary>
    static Control Arrived(MainWindow main, string nickname) =>
        main.GetVisualDescendants().OfType<ContentPresenter>().Single(c => c.DataContext is UserViewModel u && u.Nickname == nickname && !c.Classes.Contains("leaving"));

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
        server.Apply(new UserUpdated(nickname == "anna" ? Anna(FakeServers.Lobby)
            : new UserInfo(1, "fp1", "ich", FakeServers.Raid, false, false, false, Permission.All, [WellKnownGroups.Admin])));
        Frame();
        Frame();
        var ghost = Assert.Single(Ghosts(main, FlyGhost.GhostClass));
        Assert.Equal(0, Arrived(main, nickname).Opacity); // the new row waits for its picture
        double landed = LastTop(ghost, out double start);
        Assert.True(Math.Abs(start - oldTop) < 10, $"starts on the old row: {start} vs {oldTop} (two frames have passed)");
        Assert.Empty(Ghosts(main, FlyGhost.GhostClass));
        Settle(250);
        var row = Arrived(main, nickname);
        Assert.Equal(row.TranslatePoint(default, overlay)!.Value.Y, landed, 1); // landed exactly on the new row
        Assert.Equal(up, landed < oldTop);
        Assert.Equal(1, row.Opacity, 2); // shows once the picture has landed
        main.Close();
    }

    [AvaloniaFact]
    public void OwnSwitch_CurrentHighlightSlides()
    {
        var main = Connected(out var server);
        server.Apply(new UserUpdated(new UserInfo(1, "fp1", "ich", FakeServers.Raid, false, false, false, Permission.All, [WellKnownGroups.Admin])));
        Settle(60);
        Assert.Single(Ghosts(main, FlyGhost.HighlightClass));
        Settle(140); // still flying; the row's own colour transition is over
        var raidRow = ChannelContainer(main, "Raid").GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("row"));
        Assert.Contains(FlyGhost.ArrivingClass, raidRow.Classes);
        Assert.Equal(Avalonia.Media.Colors.Transparent, (raidRow.Background as Avalonia.Media.ISolidColorBrush)?.Color); // waits for the highlight
        double landed = LastTop(Ghosts(main, FlyGhost.HighlightClass).Single(), out _);
        Assert.Empty(Ghosts(main, FlyGhost.HighlightClass));
        Settle(250);
        var overlay = Avalonia.Controls.Primitives.OverlayLayer.GetOverlayLayer(main)!;
        Assert.Equal(raidRow.TranslatePoint(default, overlay)!.Value.Y, landed, 1); // lands on Raid although my old row above folded meanwhile
        Assert.DoesNotContain(FlyGhost.ArrivingClass, raidRow.Classes);
        main.Close();
    }

    [AvaloniaFact]
    public void GroupReorder_Glides()
    {
        var rows = new ObservableCollection<Row> { new("Gast"), new("Moderator"), new("Admin") };
        var (window, list) = Host(rows, flip: true);
        rows.Move(2, 0);
        Frame();
        var admin = Container(list, "Admin");
        Assert.True(OffsetY(admin) > 45, $"{OffsetY(admin)}"); // from 60 px below, a frame may have passed
        Assert.True(OffsetY(Container(list, "Gast")) < -22, "the others make room, gliding as well");
        Settle(500);
        Assert.Equal(0, OffsetY(admin), 2);
    }

    [AvaloniaFact]
    public void OffscreenEnd_NoGhost()
    {
        var vm = new MainViewModel(dir, a => Dispatcher.UIThread.Post(a), useAudioDevices: false);
        var main = new MainWindow { DataContext = vm, Width = 1100, Height = 400 };
        main.Show();
        var server = FakeServers.Crowded();
        vm.Server = server;
        Settle(400);
        var lobby = server.Channels.First();
        var far = server.Channels.Last().Users.First(); // in the last channel, scrolled out of view
        server.Apply(new UserUpdated(server.Mirror.Users[far.SessionId] with { ChannelId = lobby.Id }));
        Settle(60);
        Assert.Empty(Ghosts(main, FlyGhost.GhostClass));

        // and the other way round: from the Lobby into a channel scrolled out of view
        var mine = server.Channels.First().Users.First(u => !u.IsSelf);
        server.Apply(new UserUpdated(server.Mirror.Users[mine.SessionId] with { ChannelId = server.Channels.Last().Id }));
        Settle(60);
        Assert.Empty(Ghosts(main, FlyGhost.GhostClass));
        main.Close();
    }

    /// <summary>The group list of the administration really glides (the attribute is there).</summary>
    [AvaloniaFact]
    public async Task AdminGroupList_Glides()
    {
        var vm = new MainViewModel(dir, a => Dispatcher.UIThread.Post(a), useAudioDevices: false);
        var main = new MainWindow { DataContext = vm, Width = 1100, Height = 700 };
        main.Show();
        vm.Server = FakeServers.Admin();
        await vm.OpenAdminAsync();
        Settle(400);
        var groups = main.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "GroupList");
        Assert.True(ItemMotion.GetFlip(groups));
        var admin = vm.AdminPage!;
        admin.SelectedGroup = admin.Groups.Single(g => g.Name == "Admin");
        await admin.MoveGroupUpCommand.ExecuteAsync(null);
        Frame();
        var row = (Control)groups.ContainerFromItem(admin.Groups.Live().First())!;
        Assert.True(row.RenderTransform?.Value.M32 > 0, "Admin glides up from below");
        main.Close();
    }

    [AvaloniaFact]
    public void Simplified_Jumps()
    {
        var main = Connected(out var server);
        var vm = (MainViewModel)main.DataContext!;
        vm.Appearance = vm.Appearance with { Display = DisplayMode.Simplified };
        server.Apply(new ChannelUpdated(new ChannelInfo(FakeServers.Raid, "Raid", "", -1)));
        server.Apply(new UserUpdated(Anna(FakeServers.Lobby)));
        Frame();
        Assert.Equal(0, OffsetY(ChannelContainer(main, "Raid")));
        Settle(60);
        Assert.Empty(Ghosts(main, FlyGhost.GhostClass));
        main.Close();
    }

    /// <summary>A row back after its fold has finished is whole again (a finished fold keeps its last values).</summary>
    [AvaloniaFact]
    public void BackAfterTheFoldFinished_IsWhole()
    {
        var rows = new ObservableCollection<Row> { new("a"), new("b") };
        var (_, list) = Host(rows);
        var leave = new LeaveTimer(TimeProvider.System, () => a => Dispatcher.UIThread.Post(a)) { Delay = TimeSpan.FromSeconds(5) }; // stays long after its fold
        CollectionSync.Sync(rows, [new Row("a", 1)], leave);
        Settle(400); // folded
        Assert.Equal(0, Container(list, "b").Opacity, 2);
        CollectionSync.Sync(rows, [new Row("a", 2), new Row("b", 2)], leave);
        Settle(100);
        var b = Container(list, "b");
        Assert.Equal(1, b.Opacity, 2);
        Assert.Equal(30, b.Bounds.Height, 1);
        Assert.Equal(0, OffsetX(b));
    }
}
