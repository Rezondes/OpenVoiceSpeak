using System.Collections.ObjectModel;
using OVS.Client.ViewModels;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client;

/// <summary>Package 102: a removed entry may stay a moment as leaving; leaving belongs to the entry in one list.</summary>
public class CollectionSyncTests
{
    sealed record Row(string Key, int Version = 0) : IMotionKey
    {
        public object MotionKey => Key;
    }

    static (ObservableCollection<string> List, LeaveTimer Leave, ManualTimeProvider Clock) Setup(params string[] items)
    {
        var clock = new ManualTimeProvider();
        return (new ObservableCollection<string>(items), new LeaveTimer(clock) { Delay = TimeSpan.FromMilliseconds(220) }, clock);
    }

    /// <summary>The removal comes from Task.Delay on the manual clock: give its continuation a moment.</summary>
    static void Pass(ManualTimeProvider clock, TimeSpan by)
    {
        clock.Advance(by);
        Thread.Sleep(50);
    }

    [Fact]
    public void Removed_MarkedLeaving_RemovedAfterDelay()
    {
        var (list, leave, clock) = Setup("a", "b", "c");
        CollectionSync.Sync(list, ["a", "c"], leave);
        Assert.Equal(["a", "b", "c"], list); // b keeps its place while it folds away
        Assert.True(CollectionSync.IsLeaving(list, "b"));
        Assert.Equal(["a", "c"], list.Live());
        Pass(clock, TimeSpan.FromMilliseconds(100));
        Assert.Contains("b", list);
        Pass(clock, TimeSpan.FromMilliseconds(200));
        Assert.Equal(["a", "c"], list);
        Assert.False(CollectionSync.IsLeaving(list, "b"));
    }

    [Fact]
    public void ReturnsWhileLeaving_SameInstanceRevived()
    {
        var clock = new ManualTimeProvider();
        var leave = new LeaveTimer(clock) { Delay = TimeSpan.FromMilliseconds(220) };
        object a = new(), b = new();
        var list = new ObservableCollection<object> { a, b };
        var events = new List<(object Item, bool Leaving)>();
        void Seen(object source, object item, bool leaving)
        {
            if (source == list) events.Add((item, leaving));
        }
        CollectionSync.LeavingChanged += Seen;
        try
        {
            CollectionSync.Sync(list, [a], leave);
            CollectionSync.Sync(list, [a, b], leave);
            Assert.Equal([a, b], list); // no second row
            Assert.Same(b, list[1]);
            Assert.False(CollectionSync.IsLeaving(list, b));
            Assert.Equal([(b, true), (b, false)], events);
            Pass(clock, TimeSpan.FromSeconds(1));
            Assert.Equal([a, b], list); // the old removal does not take it away
        }
        finally
        {
            CollectionSync.LeavingChanged -= Seen;
        }
    }

    [Fact]
    public void NoDelay_RemovedAtOnce()
    {
        var list = new ObservableCollection<string> { "a", "b" };
        CollectionSync.Sync(list, ["a"], new LeaveTimer(TimeProvider.System));
        Assert.Equal(["a"], list);
        CollectionSync.Sync(list, ["b", "a"]);
        Assert.Equal(["b", "a"], list);
    }

    [Fact]
    public void Moves_IgnoreLeavingItems()
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
    public void RebuiltRow_TakesTheOldOnesPlace()
    {
        var clock = new ManualTimeProvider();
        var leave = new LeaveTimer(clock) { Delay = TimeSpan.FromMilliseconds(220) };
        var list = new ObservableCollection<Row> { new("a"), new("b") };
        var changes = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
        list.CollectionChanged += (_, e) => changes.Add(e.Action);
        CollectionSync.Sync(list, [new Row("a", 1), new Row("b", 1)], leave);
        Assert.Equal([1, 1], list.Select(r => r.Version));
        Assert.All(changes, c => Assert.Equal(System.Collections.Specialized.NotifyCollectionChangedAction.Replace, c));

        CollectionSync.Sync(list, [new Row("b", 2)], leave); // a is gone: it folds away
        Assert.True(CollectionSync.IsLeaving(list, list[0]));
        CollectionSync.Sync(list, [new Row("a", 3), new Row("b", 3)], leave); // and comes back as a new object
        Assert.Equal([3, 3], list.Select(r => r.Version));
        Assert.DoesNotContain(list, r => CollectionSync.IsLeaving(list, r));
    }

    /// <summary>A user who switches channels leaves the old channel's list while entering the new one.</summary>
    [Fact]
    public void LeavingBelongsToTheList()
    {
        var (from, leave, _) = Setup("anna");
        var to = new ObservableCollection<string>();
        CollectionSync.Sync(from, [], leave);
        CollectionSync.Sync(to, ["anna"], leave);
        Assert.True(CollectionSync.IsLeaving(from, "anna"));
        Assert.False(CollectionSync.IsLeaving(to, "anna"));
    }
}
