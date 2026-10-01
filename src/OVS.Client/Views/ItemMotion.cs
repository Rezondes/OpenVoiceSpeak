using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using OVS.Client.ViewModels;

namespace OVS.Client.Views;

/// <summary>
/// Package 101: in the animated display new entries of a list slide in: they fade in, rise by 6 px and open their
/// height, so the entries below make room instead of jumping. Entries that come together are staggered by 30 ms,
/// at most 8 steps. The first fill of a list does not play this; it fades in once as a whole, and so does a list
/// that was empty and gets a whole batch at once (the administration lists arriving from the server), except where
/// every entry counts on its own (<see cref="FlashProperty"/>, the channel tree, whose first fill just shows).
/// Rebuilt lists keep their entries by <see cref="IMotionKey"/>, so only really new ones play it.
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
        if (trackers.TryGetValue(list, out var tracker)) tracker.Hold();
    }

    static readonly ConditionalWeakTable<ItemsControl, Tracker> trackers = [];

    /// <summary>Package 102: the lists showing a collection, to find the row of an entry that starts leaving.</summary>
    static readonly ConditionalWeakTable<object, List<WeakReference<ItemsControl>>> showing = [];
    static readonly ConditionalWeakTable<Control, CancellationTokenSource> folding = [], gliding = [], entering = [];

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
        EnterProperty.Changed.AddClassHandler<ItemsControl>((list, e) =>
        {
            if (e.GetNewValue<bool>()) trackers.GetValue(list, l => new Tracker(l));
        });
        FlipProperty.Changed.AddClassHandler<ItemsControl>((list, e) =>
        {
            if (e.GetNewValue<bool>()) trackers.GetValue(list, l => new Tracker(l)).WatchPlaces();
        });
        CollectionSync.LeavingChanged += OnLeavingChanged;
    }

    public static object? KeyOf(object? item) => item is IMotionKey keyed ? keyed.MotionKey : item;

    static void Show(object? source, ItemsControl list, bool on)
    {
        if (source is null) return;
        var lists = showing.GetValue(source, _ => []);
        lists.RemoveAll(w => !w.TryGetTarget(out var l) || l == list);
        if (on) lists.Add(new WeakReference<ItemsControl>(list));
    }

    static void OnLeavingChanged(object source, object item, bool leaving)
    {
        if (!showing.TryGetValue(source, out var lists)) return;
        foreach (var weak in lists.ToList())
        {
            if (!weak.TryGetTarget(out var list) || list.ContainerFromItem(item) is not { } container) continue;
            if (leaving && GetFly(list)) Depart(item, container);
            if (leaving) Leave(container);
            else Stay(container);
        }
    }

    // ---- Package 103: a user who switches channels flies from the old row to the new one ----

    sealed record Departure(OverlayLayer Overlay, RenderTargetBitmap Picture, Rect From);

    static readonly Dictionary<object, Departure> departures = new(ReferenceEqualityComparer.Instance);
    static readonly Dictionary<object, Control> arrivals = new(ReferenceEqualityComparer.Instance);
    static readonly ConditionalWeakTable<Control, object> flown = [];
    static bool pairing;

    /// <summary>The picture is taken now, before the row folds away; it flies only if the entry turns up elsewhere.</summary>
    static void Depart(object item, Control container)
    {
        if (!Motion.IsAnimated || OverlayLayer.GetOverlayLayer(container) is not { } overlay || FlyGhost.VisibleRect(container, overlay) is not { } from) return;
        if (departures.Remove(item, out var earlier)) earlier.Picture.Dispose();
        departures[item] = new Departure(overlay, ReorderDrag.Snapshot(container), from);
        QueuePairing();
    }

    static void Arrive(object item, Control container)
    {
        arrivals[item] = container;
        QueuePairing();
    }

    /// <summary>Once the change is laid out: whoever left one list and came into another in the same change flies.</summary>
    static void QueuePairing()
    {
        if (pairing) return;
        pairing = true;
        Dispatcher.UIThread.Post(() =>
        {
            pairing = false;
            foreach (var (item, departure) in departures)
            {
                if (arrivals.TryGetValue(item, out var container) && FlyGhost.VisibleRect(container, departure.Overlay) is not null)
                {
                    flown.AddOrUpdate(container, item);
                    _ = Land(container, departure);
                }
                else departure.Picture.Dispose(); // gone for good, or its new place cannot be seen
            }
            departures.Clear();
            arrivals.Clear();
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// The picture flies to where the new row is in each frame (it still opens, the old one still folds); the row shows
    /// once the picture has landed on it, and flashes.
    /// </summary>
    static async Task Land(Control container, Departure departure)
    {
        await FlyGhost.Row(departure.Overlay, departure.Picture, departure.From, () =>
            container.TranslatePoint(default, departure.Overlay) is { } at ? new Point(at.X, at.Y - (container.RenderTransform?.Value.M32 ?? 0)) : null);
        flown.Remove(container);
        container.ClearValue(Visual.OpacityProperty);
        await Flash(container);
    }

    /// <summary>Folds the row away: it fades, drifts to the left and closes its height; it cannot be clicked any more.</summary>
    static void Leave(Control container)
    {
        Stay(container);
        EndEntry(container);
        container.IsHitTestVisible = false;
        container.Classes.Set("leaving", true);
        if (!Motion.IsAnimated) return;
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
        }.Play(container, cancel.Token);
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

    /// <summary>A recycled row starts whole and still, whatever its last entry was doing.</summary>
    static void Reset(Control container)
    {
        Stay(container);
        EndEntry(container);
        if (gliding.TryGetValue(container, out var cancel))
        {
            cancel.Cancel();
            gliding.Remove(container);
        }
        container.ClearValue(Visual.OpacityProperty);
        container.ClearValue(Layoutable.MaxHeightProperty);
        flown.Remove(container);
    }

    sealed class Tracker
    {
        readonly ItemsControl list;
        HashSet<object> known = []; // the entries as of the last settled change
        bool filled, fading, batchQueued, settleQueued, fillBatch, held, moved, watching;
        int batch;
        Dictionary<object, double> places = []; // Package 103: where each entry sat after the last layout

        public Tracker(ItemsControl list)
        {
            this.list = list;
            list.ContainerPrepared += OnPrepared;
            list.ContainerClearing += (_, e) => Reset(e.Container);
            Show(list.ItemsSource, list, true);
            list.Items.CollectionChanged += OnItemsChanged;
            list.PropertyChanged += (_, e) =>
            {
                if (e.Property != ItemsControl.ItemsSourceProperty) return;
                Show(e.OldValue, list, false);
                Show(e.NewValue, list, true);
                if (e.OldValue is null) // a list that appears (connecting) is a first fill again
                {
                    filled = false;
                    fading = false;
                    // the window already shows (a hidden tree is not in the visual tree yet, so ask the logical one)
                    if (GetBuildUp(list) && Motion.IsAnimated && Avalonia.LogicalTree.LogicalExtensions.FindLogicalAncestorOfType<Window>(list) is { IsVisible: true })
                        BuildUpOnceShown();
                }
                QueueSettle();
            };
            QueueSettle();
        }

        /// <summary>
        /// Package 106: the tree that appears while the window shows (connecting) builds up entry by entry. Its rows are
        /// made while the tree is still hidden (its visibility follows a moment later), so it plays once it is laid out
        /// visible.
        /// </summary>
        void BuildUpOnceShown()
        {
            var source = list.ItemsSource;
            var since = System.Diagnostics.Stopwatch.StartNew();
            void OnLayout(object? sender, EventArgs e)
            {
                // gone, switched to the simplified display, or not shown soon (the drawer is closed): it just shows later
                if (list.ItemsSource != source || !Motion.IsAnimated || since.ElapsedMilliseconds > 500)
                {
                    list.LayoutUpdated -= OnLayout;
                    return;
                }
                if (!list.IsEffectivelyVisible) return;
                list.LayoutUpdated -= OnLayout;
                int index = 0;
                foreach (var container in list.GetRealizedContainers().OrderBy(list.IndexFromContainer).ToList())
                    Enter(container, index++, flash: false, fly: false);
            }
            list.LayoutUpdated += OnLayout;
        }

        public void Hold()
        {
            held = true;
            Dispatcher.UIThread.Post(() => held = false, DispatcherPriority.Background);
        }

        void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Move && GetFlip(list) && Motion.IsAnimated && !held) moved = true;
            QueueSettle();
        }

        /// <summary>
        /// Package 103 (FLIP): after every layout the list notes where each entry sits (a moved entry gets a new row
        /// at once, so its old place cannot be asked for when the move is announced). After a change of order each
        /// entry that moved glides from its old place to the new one.
        /// </summary>
        public void WatchPlaces()
        {
            if (watching) return;
            watching = true;
            list.LayoutUpdated += (_, _) =>
            {
                var now = new Dictionary<object, double>();
                foreach (var container in list.GetRealizedContainers())
                {
                    if (KeyOf(list.ItemFromContainer(container)) is not { } key) continue;
                    now.TryAdd(key, container.Bounds.Y);
                    if (moved && !held && places.TryGetValue(key, out var was) && Math.Abs(was - container.Bounds.Y) >= 0.5)
                        Glide(container, was - container.Bounds.Y);
                }
                moved = false;
                places = now;
            };
        }

        /// <summary>
        /// Once a change is complete (after the layout that shows it) the list knows its entries again: a rebuild
        /// (clear, then add the same entries) does not count them as new, and entries that came while the list was
        /// hidden (the drawer, a closed page) do not slide in when it shows.
        /// </summary>
        void QueueSettle()
        {
            if (settleQueued) return;
            settleQueued = true;
            Dispatcher.UIThread.Post(() =>
            {
                settleQueued = false;
                known = list.Items.Select(KeyOf).OfType<object>().ToHashSet();
                filled = true;
            }, DispatcherPriority.Background);
        }

        void OnPrepared(object? sender, ContainerPreparedEventArgs e)
        {
            var item = list.ItemFromContainer(e.Container) ?? e.Container.DataContext;
            if (item is not null && list.ItemsSource is { } source && CollectionSync.IsLeaving(source, item))
            {
                Leave(e.Container); // shown in the middle of folding away (a scrolled list): folded at once
                return;
            }
            var key = KeyOf(item);
            if (key is null || !Motion.IsAnimated) return;
            if (!list.IsEffectivelyVisible) // a hidden list makes its rows at once; nobody sees them arrive
            {
                known.Add(key);
                return;
            }
            if (!filled || (fillBatch && !known.Contains(key)))
            {
                // the first fill: one fade for the whole list, no entry on its own. Not in the channel tree: a channel
                // that moves gets a new user list, whose users were there all along (connecting is Package 106)
                if (!GetFlash(list)) FadeList();
                return;
            }
            if (!known.Add(key)) return;
            if (known.Count == 1 && batch == 0 && !GetFlash(list) && settleQueued)
            {
                // an empty list gets a whole batch at once (an administration list arriving from the server)
                fillBatch = true;
                FadeList();
                QueueBatchEnd();
                return;
            }
            if (GetFly(list)) Arrive(item!, e.Container);
            Enter(e.Container, batch++, GetFlash(list), GetFly(list));
            QueueBatchEnd();
        }

        void FadeList()
        {
            if (fading) return;
            fading = true;
            _ = new Animation
            {
                Duration = Motion.Normal,
                Easing = Motion.Ease,
                FillMode = FillMode.Backward,
                Children =
                {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 0d) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 1d) } },
                },
            }.Play(list).ContinueWith(_ => fading = false, TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>Entries prepared in the same round of the dispatcher came together.</summary>
        void QueueBatchEnd()
        {
            if (batchQueued) return;
            batchQueued = true;
            Dispatcher.UIThread.Post(() =>
            {
                batchQueued = false;
                batch = 0;
                fillBatch = false;
            }, DispatcherPriority.Background);
        }
    }

    /// <summary>Package 103: the entry starts where it was and glides to its new place.</summary>
    static void Glide(Control container, double from)
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

    /// <param name="fly">
    /// A list whose entries may fly in from another (Package 103): the entry waits hidden until the pairing has looked
    /// for its departure; if a picture flies to it, it only opens its height and shows when the picture lands.
    /// </param>
    static async void Enter(Control container, int index, bool flash, bool fly)
    {
        container.ClipToBounds = true;
        container.Classes.Set("entering", true);
        if (fly)
        {
            container.Opacity = 0;
            container.MaxHeight = 0;
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background); // after the pairing
        }
        bool flying = flown.TryGetValue(container, out _);
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
        }.Play(container, cancel.Token);
        // ended early: EndEntry cleared the mark already, and a recycled row may carry a new entry's mark by now
        if (cancel.IsCancellationRequested) return; // it leaves now (or holds another entry): the fold takes over
        container.Classes.Set("entering", false);
        entering.Remove(container);
        container.ClearValue(Visual.ClipToBoundsProperty);
        if (flying) return; // Land shows it and lets it flash
        container.ClearValue(Visual.OpacityProperty);
        if (flash) await Flash(container);
    }

    /// <summary>The row flashes once in the accent colour (Motion.axaml).</summary>
    static async Task Flash(Control container)
    {
        container.Classes.Set("fresh", true);
        await Task.Delay(FlashMs);
        container.Classes.Set("fresh", false);
    }
}
