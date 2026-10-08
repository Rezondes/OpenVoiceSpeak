using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OVS.Client.Collections;

namespace OVS.Client.Animations;

/// <summary>
/// Package 103 (FLIP): where the entries of a list with <see cref="ItemMotion.FlipProperty"/> sit. After a layout the list
/// notes where each entry sits (a moved entry gets a new row at once, so its old place cannot be asked for when the move
/// is announced). After a change of order each entry that moved glides from its old place to the new one.
/// </summary>
internal sealed class PlaceTracker
{
    readonly ItemsControl list;
    bool held, moved, tracking;
    Dictionary<object, double> places = []; // Package 103: where each entry sat after the last layout
    long trackUntil;
    int layoutPasses;

    readonly ConditionalWeakTable<Control, CancellationTokenSource> gliding = [];

    /// <summary>Package 122: whether the list notes its entries' places on layout passes right now (tests ask).</summary>
    internal bool Tracking => tracking;

    internal PlaceTracker(ItemsControl list)
    {
        this.list = list;
        list.ContainerClearing += (_, e) => EndGlide(e.Container);
        list.Items.CollectionChanged += OnItemsChanged;
        list.PropertyChanged += (_, e) =>
        {
            if (e.Property == ItemsControl.ItemsSourceProperty) Track();
        };
        Track();
    }

    /// <summary>The next change of the list's order does not glide (see <see cref="ItemMotion.Hold"/>).</summary>
    internal void Hold()
    {
        held = true;
        Dispatcher.UIThread.Post(() => held = false, DispatcherPriority.Background);
    }

    void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Move && ItemMotion.GetFlip(list) && Motion.IsAnimated(list) && !held) moved = true;
        ItemMotion.TrackPlacesAround(list);
    }

    /// <summary>
    /// Package 122: the places only change around a change of the list (or of one inside it), so only then does the
    /// list note them on its layout passes: until whatever the change started (entering, leaving, gliding) is over
    /// and they stay the same. The places noted last stay for the next change.
    /// </summary>
    internal void Track()
    {
        trackUntil = Environment.TickCount64 + (long)Motion.Slow.TotalMilliseconds + ItemMotion.StaggerSteps * ItemMotion.StaggerMs; // clock: frame timing
        if (tracking) return;
        // the places noted last may be stale (a row grew, the list was laid out anew): where the rows sit now is where
        // they glide from. Asked by what each row still shows (the list's indexes may have moved on with the change);
        // a row made for the change has no place yet and keeps the one noted
        foreach (var container in list.GetRealizedContainers())
            if (container.IsArrangeValid && IMotionKey.KeyOf(container.DataContext) is { } key) places[key] = container.Bounds.Y;
        tracking = true;
        list.LayoutUpdated += OnLayoutUpdated;
        _ = UntrackWhenStillAsync();
    }

    /// <summary>A list nothing lays out any more is still: the places noted on its last layout pass are where its entries sit.</summary>
    async Task UntrackWhenStillAsync()
    {
        while (tracking)
        {
            int passes = layoutPasses;
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(trackUntil - Environment.TickCount64, 100))); // clock: frame timing
            // a move still waiting for its layout pass (the UI thread was busy) keeps it, unless the list is gone
            if (tracking && passes == layoutPasses && Environment.TickCount64 >= trackUntil && (!moved || !list.IsAttachedToVisualTree())) Untrack(); // clock: frame timing
        }
    }

    void Untrack()
    {
        tracking = false;
        moved = false; // a later pass must not glide from these places
        list.LayoutUpdated -= OnLayoutUpdated;
    }

    void OnLayoutUpdated(object? sender, EventArgs e)
    {
        layoutPasses++;
        var now = new Dictionary<object, double>();
        bool same = true;
        foreach (var container in list.GetRealizedContainers())
        {
            if (IMotionKey.KeyOf(list.ItemFromContainer(container)) is not { } key) continue;
            now.TryAdd(key, container.Bounds.Y);
            bool known = places.TryGetValue(key, out var was);
            same &= known && was == container.Bounds.Y;
            if (moved && !held && known && Math.Abs(was - container.Bounds.Y) >= 0.5)
                Glide(container, was - container.Bounds.Y);
        }
        same &= now.Count == places.Count;
        moved = false;
        places = now;
        if (same && Environment.TickCount64 >= trackUntil) Untrack(); // clock: frame timing
    }

    /// <summary>Package 103: the entry starts where it was and glides to its new place.</summary>
    void Glide(Control container, double from)
    {
        var cancel = new CancellationTokenSource();
        gliding.AddOrUpdate(container, cancel);
        _ = new Animation
        {
            Duration = Motion.Slow,
            Easing = Motion.Ease,
            FillMode = FillMode.Backward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(TranslateTransform.YProperty, from) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(TranslateTransform.YProperty, 0d) } },
            },
        }.Play(container, cancel.Token);
    }

    /// <summary>A recycled row starts still, whatever its last entry's glide was doing.</summary>
    void EndGlide(Control container)
    {
        if (!gliding.TryGetValue(container, out var cancel)) return;
        cancel.Cancel();
        gliding.Remove(container);
    }
}
