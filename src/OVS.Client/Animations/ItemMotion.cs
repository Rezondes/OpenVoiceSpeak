using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OVS.Client.Collections;

namespace OVS.Client.Animations;

/// <summary>
/// Package 101: in the animated display new entries of a list slide in: they fade in, rise by 6 px and open their
/// height, so the entries below make room instead of jumping. Entries that come together are staggered by 30 ms,
/// at most 8 steps. The first fill of a list does not play this; it fades in once as a whole, and so does a list
/// that was empty and gets a whole batch at once (the administration lists arriving from the server), except where
/// every entry counts on its own (<see cref="FlashProperty"/>, the channel tree, whose first fill just shows).
/// Rebuilt lists keep their entries by <see cref="IMotionKey"/>, so only really new ones play it. Per list, an
/// <see cref="EntryTracker"/> decides which entries are new and a <see cref="PlaceTracker"/> where moved ones glide from.
/// </summary>
public static class ItemMotion
{
    public const int StaggerMs = 30, StaggerSteps = 8;
    public const int FlashMs = 600;
    public const double Rise = 6;

    /// <summary>Package 102: a leaving entry moves this far to the left while it folds away.</summary>
    public const double Drift = 8;

    public static readonly AttachedProperty<bool> EnterProperty =
        AvaloniaProperty.RegisterAttached<ItemsControl, bool>("Enter", typeof(ItemMotion));

    /// <summary>The channel tree: every new entry slides in on its own and flashes once in the accent colour.</summary>
    public static readonly AttachedProperty<bool> FlashProperty =
        AvaloniaProperty.RegisterAttached<ItemsControl, bool>("Flash", typeof(ItemMotion));

    /// <summary>Package 103: entries that change their place in the list glide there instead of jumping.</summary>
    public static readonly AttachedProperty<bool> FlipProperty =
        AvaloniaProperty.RegisterAttached<ItemsControl, bool>("Flip", typeof(ItemMotion));

    /// <summary>Package 103: the users of a channel: one who leaves this list and comes into another flies there.</summary>
    public static readonly AttachedProperty<bool> FlyProperty =
        AvaloniaProperty.RegisterAttached<ItemsControl, bool>("Fly", typeof(ItemMotion));

    /// <summary>
    /// Package 106: the first fill (connecting) builds up entry by entry, staggered, instead of fading in as a whole;
    /// only the channel tree itself (a moved channel's new user list just shows).
    /// </summary>
    public static readonly AttachedProperty<bool> BuildUpProperty =
        AvaloniaProperty.RegisterAttached<ItemsControl, bool>("BuildUp", typeof(ItemMotion));

    public static bool GetBuildUp(ItemsControl list) => list.GetValue(BuildUpProperty);
    public static void SetBuildUp(ItemsControl list, bool value) => list.SetValue(BuildUpProperty, value);

    public static bool GetEnter(ItemsControl list) => list.GetValue(EnterProperty);
    public static void SetEnter(ItemsControl list, bool value) => list.SetValue(EnterProperty, value);
    public static bool GetFlash(ItemsControl list) => list.GetValue(FlashProperty);
    public static void SetFlash(ItemsControl list, bool value) => list.SetValue(FlashProperty, value);
    public static bool GetFlip(ItemsControl list) => list.GetValue(FlipProperty);
    public static void SetFlip(ItemsControl list, bool value) => list.SetValue(FlipProperty, value);
    public static bool GetFly(ItemsControl list) => list.GetValue(FlyProperty);
    public static void SetFly(ItemsControl list, bool value) => list.SetValue(FlyProperty, value);

    /// <summary>
    /// Package 103: the next change of the list's order is not animated: after a drop the row already sits where the
    /// new order puts it (Package 96).
    /// </summary>
    public static void Hold(ItemsControl list)
    {
        if (placeTrackers.TryGetValue(list, out var tracker)) tracker.Hold();
    }


    static readonly ConditionalWeakTable<ItemsControl, EntryTracker> entryTrackers = [];
    static readonly ConditionalWeakTable<ItemsControl, PlaceTracker> placeTrackers = [];

    /// <summary>Package 102: the lists showing a collection, to find the row of an entry that starts leaving.</summary>
    static readonly ConditionalWeakTable<object, List<WeakReference<ItemsControl>>> showing = [];
    static readonly ConditionalWeakTable<Control, CancellationTokenSource> folding = [], entering = [];

    /// <summary>
    /// Package 109: a row that starts leaving (or is handed to another entry) while it still slides in ends its entry at
    /// once; otherwise the entry would run on in a row that is gone and end long after the change (AC4: nothing queued).
    /// </summary>
    static void EndEntry(Control container)
    {
        if (!entering.TryGetValue(container, out var cancel)) return;
        cancel.Cancel();
        entering.Remove(container);
        container.Classes.Set("entering", false);
    }

    static ItemMotion()
    {
        EnterProperty.Changed.AddClassHandler<ItemsControl>((list, args) =>
        {
            if (args.GetNewValue<bool>()) entryTrackers.GetValue(list, itemsControl => new EntryTracker(itemsControl));
        });
        FlipProperty.Changed.AddClassHandler<ItemsControl>((list, args) =>
        {
            if (args.GetNewValue<bool>()) placeTrackers.GetValue(list, itemsControl => new PlaceTracker(itemsControl));
        });
        CollectionSync.LeavingChanged += OnLeavingChanged;
    }

    /// <summary>Package 122: whether the list notes its entries' places on layout passes right now (tests ask).</summary>
    internal static bool TracksPlaces(ItemsControl list) => placeTrackers.TryGetValue(list, out var tracker) && tracker.Tracking;

    /// <summary>Package 102: notes that <paramref name="list"/> shows <paramref name="source"/> now (<paramref name="on"/>) or no longer.</summary>
    internal static void Show(object? source, ItemsControl list, bool on)
    {
        if (source is null) return;
        var lists = showing.GetValue(source, _ => []);
        lists.RemoveAll(weakReference => !weakReference.TryGetTarget(out var itemsControl) || itemsControl == list);
        if (on) lists.Add(new WeakReference<ItemsControl>(list));
    }

    static void OnLeavingChanged(object source, object item, bool leaving)
    {
        if (!showing.TryGetValue(source, out var lists)) return;
        foreach (var weak in lists.ToList())
        {
            if (!weak.TryGetTarget(out var list) || list.ContainerFromItem(item) is not { } container) continue;
            TrackPlacesAround(list); // Package 122: the row folds away, the ones below move up
            if (leaving && GetFly(list)) ItemFlight.Depart(item, container);
            if (leaving) Leave(container);
            else Stay(container);
        }
    }

    /// <summary>Package 122: a list changes; the lists around it grow, shrink or shift as well (the channel tree around a channel's users).</summary>
    internal static void TrackPlacesAround(ItemsControl list)
    {
        foreach (var changed in list.GetSelfAndVisualAncestors().OfType<ItemsControl>())
            if (placeTrackers.TryGetValue(changed, out var tracker)) tracker.Track();
    }

    /// <summary>Folds the row away: it fades, drifts to the left and closes its height; it cannot be clicked any more.</summary>
    internal static void Leave(Control container)
    {
        Stay(container);
        EndEntry(container);
        container.IsHitTestVisible = false;
        container.Classes.Set("leaving", true);
        if (!Motion.IsAnimated(container)) return;
        var cancel = new CancellationTokenSource();
        folding.AddOrUpdate(container, cancel);
        container.ClipToBounds = true;
        _ = new Animation
        {
            Duration = Motion.Normal,
            Easing = Motion.Ease,
            FillMode = FillMode.Forward, // stays folded until the list lets it go
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 1d),
                        new Setter(TranslateTransform.XProperty, 0d),
                        new Setter(Layoutable.MaxHeightProperty, container.Bounds.Height),
                    },
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 0d),
                        new Setter(TranslateTransform.XProperty, -Drift),
                        new Setter(Layoutable.MaxHeightProperty, 0d),
                    },
                },
            },
        }.PlayAsync(container, cancel.Token);
    }

    /// <summary>Came back while leaving, or handed to another entry: the row is whole again.</summary>
    static void Stay(Control container)
    {
        if (folding.TryGetValue(container, out var cancel))
        {
            cancel.Cancel();
            folding.Remove(container);
            // a finished fold keeps its last values (faded, no height, moved left)
            container.ClearValue(Visual.OpacityProperty);
            container.ClearValue(Layoutable.MaxHeightProperty);
            container.ClearValue(Visual.RenderTransformProperty);
        }
        container.ClearValue(InputElement.IsHitTestVisibleProperty);
        container.ClearValue(Visual.ClipToBoundsProperty);
        container.Classes.Set("leaving", false);
    }

    /// <summary>A recycled row starts whole, whatever its last entry was doing (its glide ends in <see cref="PlaceTracker"/>).</summary>
    internal static void Reset(Control container)
    {
        Stay(container);
        EndEntry(container);
        container.ClearValue(Visual.OpacityProperty);
        container.ClearValue(Layoutable.MaxHeightProperty);
        ItemFlight.Forget(container);
    }

    /// <param name="fly">
    /// A list whose entries may fly in from another (Package 103): the entry waits hidden until the pairing has looked
    /// for its departure; if a picture flies to it, it only opens its height and shows when the picture lands.
    /// </param>
    internal static async void Enter(Control container, int index, bool flash, bool fly)
    {
        container.ClipToBounds = true;
        container.Classes.Set("entering", true);
        if (fly)
        {
            container.Opacity = 0;
            container.MaxHeight = 0;
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background); // after the pairing
        }
        bool flying = ItemFlight.IsFlownTo(container);
        var delay = TimeSpan.FromMilliseconds(Math.Min(index, StaggerSteps - 1) * StaggerMs);
        container.ClearValue(Layoutable.MaxHeightProperty);
        container.Measure(new Size(container.Parent is Layoutable parent && parent.Bounds.Width > 0 ? parent.Bounds.Width : double.PositiveInfinity,
            double.PositiveInfinity));
        double height = container.DesiredSize.Height;
        var cancel = new CancellationTokenSource();
        entering.AddOrUpdate(container, cancel);
        await new Animation
        {
            Duration = Motion.Normal,
            Delay = delay,
            Easing = Motion.Ease,
            FillMode = FillMode.Backward,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 0d),
                        new Setter(TranslateTransform.YProperty, Rise),
                        new Setter(Layoutable.MaxHeightProperty, 0d),
                    },
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, flying ? 0d : 1d), // a flown-in entry waits for its picture
                        new Setter(TranslateTransform.YProperty, 0d),
                        new Setter(Layoutable.MaxHeightProperty, height),
                    },
                },
            },
        }.PlayAsync(container, cancel.Token);
        // ended early: EndEntry cleared the mark already, and a recycled row may carry a new entry's mark by now
        if (cancel.IsCancellationRequested) return; // it leaves now (or holds another entry): the fold takes over
        container.Classes.Set("entering", false);
        entering.Remove(container);
        container.ClearValue(Visual.ClipToBoundsProperty);
        if (flying) return; // Land shows it
        container.ClearValue(Visual.OpacityProperty);
        if (flash) await FlashAsync(container);
    }

    /// <summary>The row flashes once in the accent colour (Motion.axaml).</summary>
    static async Task FlashAsync(Control container)
    {
        container.Classes.Set("fresh", true);
        await Task.Delay(FlashMs); // clock: frame timing
        container.Classes.Set("fresh", false);
    }
}
