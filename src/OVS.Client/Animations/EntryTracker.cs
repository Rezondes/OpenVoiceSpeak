using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using OVS.Client.Collections;

namespace OVS.Client.Animations;

/// <summary>
/// Package 101: which entries of a list with <see cref="ItemMotion.EnterProperty"/> are new, so they slide in (staggered),
/// and which are not: the first fill and a whole batch for an empty list fade in as one, a rebuild with the same keys and
/// entries that came while the list was hidden do not slide in. Package 106: the channel tree builds up entry by entry
/// when connecting.
/// </summary>
internal sealed class EntryTracker
{
    readonly ItemsControl list;
    HashSet<object> known = []; // the entries as of the last settled change
    bool filled, fading, batchQueued, settleQueued, fillBatch;
    int batch;

    internal EntryTracker(ItemsControl list)
    {
        this.list = list;
        list.ContainerPrepared += OnPrepared;
        list.ContainerClearing += (_, e) => ItemMotion.Reset(e.Container);
        ItemMotion.Show(list.ItemsSource, list, true);
        list.Items.CollectionChanged += (_, _) =>
        {
            ItemMotion.TrackPlacesAround(list); // Package 122: the list grows or shrinks, the lists around it move
            QueueSettle();
        };
        list.PropertyChanged += (_, e) =>
        {
            if (e.Property != ItemsControl.ItemsSourceProperty) return;
            ItemMotion.Show(e.OldValue, list, false);
            ItemMotion.Show(e.NewValue, list, true);
            if (e.OldValue is null) // a list that appears (connecting) is a first fill again
            {
                filled = false;
                fading = false;
                // the window already shows (a hidden tree is not in the visual tree yet, so ask the logical one)
                if (ItemMotion.GetBuildUp(list) && Avalonia.LogicalTree.LogicalExtensions.FindLogicalAncestorOfType<Window>(list) is { IsVisible: true } window
                    && Motion.IsAnimated(window))
                    BuildUpOnceShown(window);
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
    void BuildUpOnceShown(Window window)
    {
        var source = list.ItemsSource;
        var since = System.Diagnostics.Stopwatch.StartNew(); // clock: frame timing
        void OnLayout(object? sender, EventArgs e)
        {
            // gone, switched to the simplified display, or not shown soon (the drawer is closed): it just shows later
            if (list.ItemsSource != source || !Motion.IsAnimated(window) || since.ElapsedMilliseconds > 500)
            {
                list.LayoutUpdated -= OnLayout;
                return;
            }
            if (!list.IsEffectivelyVisible) return;
            list.LayoutUpdated -= OnLayout;
            int index = 0;
            foreach (var container in list.GetRealizedContainers().OrderBy(list.IndexFromContainer).ToList())
                ItemMotion.Enter(container, index++, flash: false, fly: false);
        }
        list.LayoutUpdated += OnLayout;
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
            known = list.Items.Select(IMotionKey.KeyOf).OfType<object>().ToHashSet();
            filled = true;
        }, DispatcherPriority.Background);
    }

    void OnPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        var item = list.ItemFromContainer(e.Container) ?? e.Container.DataContext;
        if (item is not null && list.ItemsSource is { } source && CollectionSync.IsLeaving(source, item))
        {
            ItemMotion.Leave(e.Container); // shown in the middle of folding away (a scrolled list): folded at once
            return;
        }
        var key = IMotionKey.KeyOf(item);
        if (key is null || !Motion.IsAnimated(list)) return;
        if (!list.IsEffectivelyVisible) // a hidden list makes its rows at once; nobody sees them arrive
        {
            known.Add(key);
            return;
        }
        if (!filled || (fillBatch && !known.Contains(key)))
        {
            // the first fill: one fade for the whole list, no entry on its own. Not in the channel tree: a channel
            // that moves gets a new user list, whose users were there all along (connecting is Package 106)
            if (!ItemMotion.GetFlash(list)) FadeList();
            return;
        }
        if (!known.Add(key)) return;
        if (known.Count == 1 && batch == 0 && !ItemMotion.GetFlash(list) && settleQueued)
        {
            // an empty list gets a whole batch at once (an administration list arriving from the server)
            fillBatch = true;
            FadeList();
            QueueBatchEnd();
            return;
        }
        if (ItemMotion.GetFly(list)) ItemMotion.Arrive(item!, e.Container);
        ItemMotion.Enter(e.Container, batch++, ItemMotion.GetFlash(list), ItemMotion.GetFly(list));
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
