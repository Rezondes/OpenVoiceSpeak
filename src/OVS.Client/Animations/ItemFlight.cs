using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using OVS.Client.Behaviors;

namespace OVS.Client.Animations;

/// <summary>
/// Package 103: a user who switches channels flies from the old row to the new one. A row that leaves a flying list
/// leaves its picture (<see cref="Depart"/>), a row that comes into one is noted (<see cref="Arrive"/>); once the change is
/// laid out, an entry that did both flies. Package 120: an entry dropped by hand is grounded until the server answers.
/// </summary>
internal static class ItemFlight
{
    static readonly Dictionary<object, int> grounded = new(ReferenceEqualityComparer.Instance);
    static readonly Dictionary<object, Departure> departures = new(ReferenceEqualityComparer.Instance);
    static readonly Dictionary<object, Control> arrivals = new(ReferenceEqualityComparer.Instance);
    static readonly ConditionalWeakTable<Control, object> flown = [];
    static bool pairing;

    /// <summary>
    /// Package 120: an entry dropped by hand (a user dragged onto a channel) already sits where it was dropped, so it does
    /// not fly when the change comes. The hold ends with <paramref name="until"/> (the server's answer), so a refused drop
    /// leaves the next move flying.
    /// </summary>
    internal static void Ground(object item, Task until)
    {
        grounded[item] = grounded.GetValueOrDefault(item) + 1;
        until.ContinueWith(_ =>
        {
            // counted: of two drops of the same entry, the first answer does not let the second one fly
            if (--grounded[item] == 0) grounded.Remove(item);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>The picture is taken now, before the row folds away; it flies only if the entry turns up elsewhere.</summary>
    internal static void Depart(object item, Control container)
    {
        if (!Motion.IsAnimated(container) || grounded.ContainsKey(item) || OverlayLayer.GetOverlayLayer(container) is not { } overlay || FlyGhost.VisibleRect(container, overlay) is not { } from) return;
        if (departures.Remove(item, out var earlier)) earlier.Picture.Dispose();
        departures[item] = new Departure(overlay, DragVisuals.Snapshot(container), from);
        QueuePairing();
    }

    /// <summary>The entry came into a flying list in <paramref name="container"/>; it flies there if it left another list in the same change.</summary>
    internal static void Arrive(object item, Control container)
    {
        arrivals[item] = container;
        QueuePairing();
    }

    /// <summary>True while a picture flies to <paramref name="container"/>: the row shows only when it lands.</summary>
    internal static bool IsFlownTo(Control container) => flown.TryGetValue(container, out _);

    /// <summary>A recycled row forgets the picture that flew to its last entry.</summary>
    internal static void Forget(Control container) => flown.Remove(container);

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
    /// in the frame the picture lands on it. No flash: the flight already shows where it went.
    /// </summary>
    static Task Land(Control container, Departure departure) =>
        FlyGhost.Row(departure.Overlay, departure.Picture, departure.From, () =>
            container.TranslatePoint(default, departure.Overlay) is { } at ? new Point(at.X, at.Y - (container.RenderTransform?.Value.M32 ?? 0)) : null,
            () =>
            {
                flown.Remove(container);
                container.ClearValue(Visual.OpacityProperty);
            });

    /// <summary>The picture of a leaving row, the layer it flies on and where it started.</summary>
    sealed record Departure(OverlayLayer Overlay, RenderTargetBitmap Picture, Rect From);
}
