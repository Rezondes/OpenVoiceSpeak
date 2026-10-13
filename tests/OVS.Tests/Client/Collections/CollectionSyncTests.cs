using System.Collections.ObjectModel;
using OVS.Client.Collections;
using OVS.Client.Services;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Collections;

/// <summary>Package 102: a removed entry may stay a moment as leaving; leaving belongs to the entry in one list.</summary>
public class CollectionSyncTests
{
    /// <summary>
    /// The UI thread of the test: the removals come from Task.Delay on the manual clock and are posted here, so they run
    /// one after the other on the test thread (Step 30: two removals on the pool raced on the list).
    /// </summary>
    readonly PostQueue posts = new();

    SyncRig Setup(params string[] items)
    {
        var clock = new ManualTimeProvider();
        return new(new ObservableCollection<string>(items), new LeaveTimer(clock, () => posts.Post) { Delay = TimeSpan.FromMilliseconds(220) }, clock);
    }

    [Fact]
    public async Task Removed_MarkedLeaving_RemovedAfterDelay()
    {
        var (list, leave, clock) = Setup("a", "b", "c");
        CollectionSync.Sync(list, ["a", "c"], leave);
        Assert.Equal(["a", "b", "c"], list); // b keeps its place while it folds away
        Assert.True(CollectionSync.IsLeaving(list, "b"));
        Assert.Equal(["a", "c"], list.Live());
        clock.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Contains("b", list);
        await posts.RunAsync(clock, TimeSpan.FromMilliseconds(200));
        Assert.Equal(["a", "c"], list);
        Assert.False(CollectionSync.IsLeaving(list, "b"));
    }

    [Fact]
    public async Task Sync_ReturnsWhileLeaving_SameInstanceRevived()
    {
        var clock = new ManualTimeProvider();
        var leave = new LeaveTimer(clock, () => posts.Post) { Delay = TimeSpan.FromMilliseconds(220) };
        object first = new(), second = new();
        var list = new ObservableCollection<object> { first, second };
        var events = new List<(object Item, bool Leaving)>();
        void Seen(object source, object item, bool leaving)
        {
            if (source == list) events.Add((item, leaving));
        }
        CollectionSync.LeavingChanged += Seen;
        try
        {
            CollectionSync.Sync(list, [first], leave);
            CollectionSync.Sync(list, [first, second], leave);
            Assert.Equal([first, second], list); // no second row
            Assert.Same(second, list[1]);
            Assert.False(CollectionSync.IsLeaving(list, second));
            Assert.Equal([(second, true), (second, false)], events);
            await posts.RunAsync(clock, TimeSpan.FromSeconds(1));
            Assert.Equal([first, second], list); // the old removal does not take it away
        }
        finally
        {
            CollectionSync.LeavingChanged -= Seen;
        }
    }

    [Fact]
    public void Remove_NoDelay_GoneAtOnce()
    {
        var list = new ObservableCollection<string> { "a", "b" };
        CollectionSync.Sync(list, ["a"], new LeaveTimer(TimeProvider.System));
        Assert.Equal(["a"], list);
        CollectionSync.Sync(list, ["b", "a"]);
        Assert.Equal(["b", "a"], list);
    }

    [Fact]
    public void Moves_LeavingItemsPresent_Ignored()
    {
        var (list, leave, _) = Setup("a", "b", "c", "d");
        CollectionSync.Sync(list, ["d", "a", "c"], leave);
        Assert.Equal(["d", "a", "b", "c"], list); // b stays behind a, the entry it followed
        Assert.Equal(["d", "a", "c"], list.Live());
        CollectionSync.Sync(list, ["c", "d", "a", "e"], leave);
        Assert.Equal(["c", "d", "a", "b", "e"], list);
        Assert.Equal(["c", "d", "a", "e"], list.Live());
    }

    [Fact]
    public void Sync_RebuiltRow_TakesTheOldOnesPlace()
    {
        var clock = new ManualTimeProvider();
        var leave = new LeaveTimer(clock) { Delay = TimeSpan.FromMilliseconds(220) };
        var list = new ObservableCollection<KeyedRow> { new("a"), new("b") };
        var changes = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
        list.CollectionChanged += (_, args) => changes.Add(args.Action);
        CollectionSync.Sync(list, [new KeyedRow("a", 1), new KeyedRow("b", 1)], leave);
        Assert.Equal([1, 1], list.Select(keyedRow => keyedRow.Version));
        Assert.All(changes, notifyCollectionChangedAction => Assert.Equal(System.Collections.Specialized.NotifyCollectionChangedAction.Replace, notifyCollectionChangedAction));

        CollectionSync.Sync(list, [new KeyedRow("b", 2)], leave); // a is gone: it folds away
        Assert.True(CollectionSync.IsLeaving(list, list[0]));
        CollectionSync.Sync(list, [new KeyedRow("a", 3), new KeyedRow("b", 3)], leave); // and comes back as a new object
        Assert.Equal([3, 3], list.Select(keyedRow => keyedRow.Version));
        Assert.DoesNotContain(list, keyedRow => CollectionSync.IsLeaving(list, keyedRow));
    }

    /// <summary>A user who switches channels leaves the old channel's list while entering the new one.</summary>
    [Fact]
    public void Leaving_UserSwitchesChannel_BelongsToTheOldList()
    {
        var (from, leave, _) = Setup("anna");
        var to = new ObservableCollection<string>();
        CollectionSync.Sync(from, [], leave);
        CollectionSync.Sync(to, ["anna"], leave);
        Assert.True(CollectionSync.IsLeaving(from, "anna"));
        Assert.False(CollectionSync.IsLeaving(to, "anna"));
    }

    /// <summary>Package 122: a new order alone only moves entries, whatever the order (fixed seed).</summary>
    [Fact]
    public void Sync_PureReorder_OnlyMoves()
    {
        var random = new Random(122);
        var list = new ObservableCollection<string>(Enumerable.Range(0, 30).Select(index => $"e{index}"));
        var changes = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
        list.CollectionChanged += (_, args) => changes.Add(args.Action);
        for (int run = 0; run < 200; run++)
        {
            var order = list.OrderBy(_ => random.Next()).ToList();
            CollectionSync.Sync(list, order, run % 2 == 0 ? null : new LeaveTimer(new ManualTimeProvider()) { Delay = TimeSpan.FromMilliseconds(220) });
            Assert.Equal(order, list);
        }
        Assert.All(changes, notifyCollectionChangedAction => Assert.Equal(System.Collections.Specialized.NotifyCollectionChangedAction.Move, notifyCollectionChangedAction));
    }

    /// <summary>Package 122: leaving entries stay behind the entry they followed while the others move around them.</summary>
    [Fact]
    public async Task Leaving_OthersMove_StayBehindTheirEntry()
    {
        var (list, leave, clock) = Setup("a", "b", "c", "d", "e");
        CollectionSync.Sync(list, ["e", "c", "a"], leave);
        Assert.Equal(["e", "c", "d", "a", "b"], list);
        Assert.Equal(["e", "c", "a"], list.Live());
        CollectionSync.Sync(list, ["a", "e", "c"], leave); // a reorder while they fold away
        Assert.Equal(["a", "b", "e", "c", "d"], list);
        Assert.True(CollectionSync.IsLeaving(list, "b") && CollectionSync.IsLeaving(list, "d"));
        await posts.RunAsync(clock, TimeSpan.FromMilliseconds(300), posts: 2); // b and d
        Assert.Equal(["a", "e", "c"], list);
    }

    /// <summary>Package 122: an entry that comes back while leaving takes its new place and stays, also when the order changed.</summary>
    [Fact]
    public async Task Revived_OrderChanged_TakesItsNewPlace()
    {
        var (list, leave, clock) = Setup("a", "b", "c");
        CollectionSync.Sync(list, ["a", "c"], leave);
        Assert.True(CollectionSync.IsLeaving(list, "b"));
        CollectionSync.Sync(list, ["c", "b", "a"], leave);
        Assert.Equal(["c", "b", "a"], list);
        Assert.False(CollectionSync.IsLeaving(list, "b"));
        await posts.RunAsync(clock, TimeSpan.FromMilliseconds(300));
        Assert.Equal(["c", "b", "a"], list); // the old removal does not take it away
    }

    sealed record SyncRig(ObservableCollection<string> List, LeaveTimer Leave, ManualTimeProvider Clock);
}
