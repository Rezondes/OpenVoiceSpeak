using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using OVS.Client.Animations;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Animations;

/// <summary>
/// Package 122: a list with <c>Enter</c> lets its new entries slide in and makes the <c>Flip</c> lists around it note their
/// places, but notes none of its own; without <c>Enter</c> new entries just show.
/// </summary>
[Collection(MotionCollection.Name)]
public sealed class EntryTrackerTests
{
    [AvaloniaFact]
    public void Add_FlipWithoutEnter_EntryShowsAtOnce()
    {
        int running = AnimationCounter.Running;
        var rows = new ObservableCollection<KeyedRow> { new("a"), new("b") };
        var list = KeyedList.Create(rows, enter: false, flip: true);
        var window = KeyedList.Show(list, running);
        try
        {
            int started = AnimationCounter.Started;
            rows.Add(new KeyedRow("c"));
            MotionWait.Frame();
            Assert.DoesNotContain("entering", KeyedList.Container(list, "c").Classes);
            Assert.Equal(started, AnimationCounter.Started);
            KeyedList.Still(list, running);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Add_EnterWithoutFlip_NotesNoPlaces()
    {
        int running = AnimationCounter.Running;
        var rows = new ObservableCollection<KeyedRow> { new("a"), new("b") };
        var list = KeyedList.Create(rows, enter: true, flip: false);
        var window = KeyedList.Show(list, running);
        try
        {
            rows.Add(new KeyedRow("c"));
            MotionWait.Frame();
            Assert.Contains("entering", KeyedList.Container(list, "c").Classes); // it did change: the entry slides in
            Assert.False(ItemMotion.TracksPlaces(list));
            KeyedList.Still(list, running); // the entry has ended
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Change_EnterListInsideAFlipList_OuterListTracksPlaces()
    {
        int running = AnimationCounter.Running;
        var inside = new ObservableCollection<KeyedRow> { new("anna") };
        var inner = KeyedList.Create(inside, enter: true, flip: false);
        var outer = KeyedList.Create([new KeyedRow("Lobby"), new KeyedRow("Raid")], enter: true, flip: true,
            row => row.Key == "Lobby" ? new StackPanel { Children = { new Border { Height = 30 }, inner } } : new Border { Height = 30 });
        var window = KeyedList.Show(outer, running);
        try
        {
            Assert.False(ItemMotion.TracksPlaces(outer));
            inside.Add(new KeyedRow("bert")); // the inner list grows, the rows below it move down
            Assert.True(ItemMotion.TracksPlaces(outer));
            KeyedList.Still(outer, running);
        }
        finally
        {
            window.Close();
        }
    }
}
