using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OVS.Client.Animations;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Animations;

/// <summary>Package 103, 122: a list with <c>Flip</c> notes where its entries sit, and the ones that move glide there.</summary>
[Collection(MotionCollection.Name)]
public sealed class PlaceTrackerTests
{
    [AvaloniaFact]
    public void Move_FlipWithoutEnter_MovedRowsGlideFromTheirOldPlaces()
    {
        int running = AnimationCounter.Running;
        var rows = new ObservableCollection<KeyedRow> { new("a"), new("b"), new("c") };
        var list = KeyedList.Create(rows, enter: false, flip: true);
        var window = KeyedList.Show(list, running);
        try
        {
            int started = AnimationCounter.Started;
            rows.Move(2, 0); // c goes up by 60 px, a and b down by 30
            var offsets = MotionWait.Record(KeyedList.Container(list, "c"), MotionWait.OffsetY); // a moved entry gets a new row at once
            KeyedList.Still(list, running);
            Assert.Equal(started + 3, AnimationCounter.Started);
            Assert.True(offsets.Any(offset => offset > 30), $"c starts below, where it was ({string.Join(" ", offsets.Distinct())})");
            MotionWait.Eventually(() => Assert.Equal(0, MotionWait.OffsetY(KeyedList.Container(list, "c")), 2));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Hold_MoveRightAfter_NoRowGlides()
    {
        int running = AnimationCounter.Running;
        var rows = new ObservableCollection<KeyedRow> { new("a"), new("b"), new("c") };
        var list = KeyedList.Create(rows, enter: true, flip: true);
        var window = KeyedList.Show(list, running);
        try
        {
            int started = AnimationCounter.Started;
            ItemMotion.Hold(list); // a drop: the row already sits where the new order puts it
            rows.Move(2, 0);
            KeyedList.Still(list, running);
            Assert.Equal(started, AnimationCounter.Started);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Hold_MoveAfterTheHoldEnded_RowsGlide()
    {
        int running = AnimationCounter.Running;
        var rows = new ObservableCollection<KeyedRow> { new("a"), new("b"), new("c") };
        var list = KeyedList.Create(rows, enter: true, flip: true);
        var window = KeyedList.Show(list, running);
        try
        {
            ItemMotion.Hold(list);
            Dispatcher.UIThread.RunJobs(); // the hold covers only the change that comes right after it
            int started = AnimationCounter.Started;
            rows.Move(2, 0);
            KeyedList.Still(list, running);
            Assert.Equal(started + 3, AnimationCounter.Started);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Change_FlipListInsideAFlipList_OuterListTracksPlaces()
    {
        int running = AnimationCounter.Running;
        var users = new ObservableCollection<KeyedRow> { new("anna") };
        var inner = KeyedList.Create(users, enter: false, flip: true);
        var outer = KeyedList.Create([new KeyedRow("Lobby"), new KeyedRow("Raid")], enter: true, flip: true,
            row => row.Key == "Lobby" ? new StackPanel { Children = { new Border { Height = 30 }, inner } } : new Border { Height = 30 });
        var window = KeyedList.Show(outer, running);
        try
        {
            Assert.False(ItemMotion.TracksPlaces(outer));
            users.Add(new KeyedRow("bert")); // the inner list grows, the rows below it move down
            Assert.True(ItemMotion.TracksPlaces(outer));
            KeyedList.Still(outer, running);
        }
        finally
        {
            window.Close();
        }
    }
}
