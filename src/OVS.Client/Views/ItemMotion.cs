using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using OVS.Client.ViewModels;

namespace OVS.Client.Views;

/// <summary>
/// Package 101: in the animated display new entries of a list slide in: they fade in, rise by 6 px and open their
/// height, so the entries below make room instead of jumping. Entries that come together are staggered by 30 ms,
/// at most 8 steps. The first fill of a list does not play this; it fades in once as a whole, and so does a list
/// that was empty and gets a whole batch at once (the administration lists arriving from the server), except where
/// every entry counts on its own (<see cref="FlashProperty"/>, the channel tree, whose first fill just shows). Rebuilt lists keep their entries
/// by <see cref="IMotionKey"/>, so only really new ones play it.
/// </summary>
public static class ItemMotion
{
    public const int StaggerMs = 30, StaggerSteps = 8;
    public const int FlashMs = 600;
    public const double Rise = 6;

    public static readonly AttachedProperty<bool> EnterProperty =
        AvaloniaProperty.RegisterAttached<ItemsControl, bool>("Enter", typeof(ItemMotion));

    /// <summary>The channel tree: every new entry slides in on its own and flashes once in the accent colour.</summary>
    public static readonly AttachedProperty<bool> FlashProperty =
        AvaloniaProperty.RegisterAttached<ItemsControl, bool>("Flash", typeof(ItemMotion));

    public static bool GetEnter(ItemsControl list) => list.GetValue(EnterProperty);
    public static void SetEnter(ItemsControl list, bool value) => list.SetValue(EnterProperty, value);
    public static bool GetFlash(ItemsControl list) => list.GetValue(FlashProperty);
    public static void SetFlash(ItemsControl list, bool value) => list.SetValue(FlashProperty, value);

    static readonly ConditionalWeakTable<ItemsControl, Tracker> trackers = [];

    static ItemMotion() =>
        EnterProperty.Changed.AddClassHandler<ItemsControl>((list, e) =>
        {
            if (e.GetNewValue<bool>()) trackers.GetValue(list, l => new Tracker(l));
        });

    public static object? KeyOf(object? item) => item is IMotionKey keyed ? keyed.MotionKey : item;

    sealed class Tracker
    {
        readonly ItemsControl list;
        HashSet<object> known = []; // the entries as of the last settled change
        bool filled, fading, batchQueued, settleQueued, fillBatch;
        int batch;

        public Tracker(ItemsControl list)
        {
            this.list = list;
            list.ContainerPrepared += OnPrepared;
            list.Items.CollectionChanged += OnItemsChanged;
            list.PropertyChanged += (_, e) =>
            {
                if (e.Property != ItemsControl.ItemsSourceProperty) return;
                if (e.OldValue is null) // a list that appears (connecting) is a first fill again
                {
                    filled = false;
                    fading = false;
                }
                QueueSettle();
            };
            QueueSettle();
        }

        void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => QueueSettle();

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
            var key = KeyOf(list.ItemFromContainer(e.Container) ?? e.Container.DataContext);
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
            Enter(e.Container, batch++, GetFlash(list));
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
            }.RunAsync(list).ContinueWith(_ => fading = false, TaskScheduler.FromCurrentSynchronizationContext());
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

    static async void Enter(Control container, int index, bool flash)
    {
        var delay = TimeSpan.FromMilliseconds(Math.Min(index, StaggerSteps - 1) * StaggerMs);
        container.Measure(new Size(container.Parent is Layoutable parent && parent.Bounds.Width > 0 ? parent.Bounds.Width : double.PositiveInfinity,
            double.PositiveInfinity));
        double height = container.DesiredSize.Height;
        container.ClipToBounds = true;
        container.Classes.Set("entering", true);
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
                        new Setter(Visual.OpacityProperty, 1d),
                        new Setter(TranslateTransform.YProperty, 0d),
                        new Setter(Layoutable.MaxHeightProperty, height),
                    },
                },
            },
        }.RunAsync(container);
        container.ClearValue(Visual.ClipToBoundsProperty);
        container.Classes.Set("entering", false);
        if (!flash) return;
        container.Classes.Set("fresh", true); // Motion.axaml: the row flashes once in the accent colour
        await Task.Delay(FlashMs);
        container.Classes.Set("fresh", false);
    }
}
