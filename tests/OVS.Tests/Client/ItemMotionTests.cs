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
        }
    }

    static void Frame()
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    static (Window Window, ItemsControl List) Host(ObservableCollection<Row> rows, bool animated = true)
    {
        var list = new ItemsControl
        {
            ItemsSource = rows,
            ItemTemplate = new FuncDataTemplate<Row>((row, _) => new Border { Height = 30, Child = new TextBlock { Text = row?.Key } }),
        };
        ItemMotion.SetEnter(list, true);
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
        Settle(400);
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
}
