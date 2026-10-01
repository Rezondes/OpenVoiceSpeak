using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace OVS.Client.Views;

/// <summary>
/// Package 103: something that changes its place flies there in the overlay layer: a picture of a user's row from the
/// old channel to the new one, the current-channel highlight from the old channel to the new one. Only when both ends
/// are in view; across the edge of a scrolled list it would fly in from nowhere. The flight follows its target while
/// the rows around it still open and fold, so it lands exactly where the target ends up.
/// </summary>
public static class FlyGhost
{
    public const string GhostClass = "flyGhost", HighlightClass = "highlightGhost", ArrivingClass = "arriving";

    /// <summary>The control's rectangle in the overlay, or null when it is not to be seen (scrolled away, hidden).</summary>
    public static Rect? VisibleRect(Control control, Visual overlay)
    {
        if (!control.IsEffectivelyVisible || control.Bounds.Width <= 0 || control.TranslatePoint(default, overlay) is not { } topLeft) return null;
        var rect = new Rect(topLeft, control.Bounds.Size);
        if (control.FindAncestorOfType<ScrollViewer>() is { } scroll && scroll.TranslatePoint(default, overlay) is { } at
            && !new Rect(at, scroll.Bounds.Size).Contains(rect.Center))
            return null;
        return rect;
    }

    /// <summary>
    /// Lets <paramref name="ghost"/> fly from <paramref name="from"/> to wherever <paramref name="to"/> says the target is
    /// in each frame, then removes it.
    /// </summary>
    public static Task Fly(OverlayLayer overlay, Control ghost, Rect from, Func<Point?> to)
    {
        ghost.IsHitTestVisible = false;
        ghost.Width = from.Width;
        ghost.Height = from.Height;
        Canvas.SetLeft(ghost, from.X);
        Canvas.SetTop(ghost, from.Y);
        overlay.Children.Add(ghost);
        var done = new TaskCompletionSource();
        var top = TopLevel.GetTopLevel(overlay);
        var clock = Stopwatch.StartNew();
        var target = from.Position;
        void Frame(TimeSpan _)
        {
            target = to() ?? target;
            double t = Math.Min(1, clock.Elapsed / Motion.Slow);
            double eased = Motion.Ease.Ease(t);
            Canvas.SetLeft(ghost, from.X + (target.X - from.X) * eased);
            Canvas.SetTop(ghost, from.Y + (target.Y - from.Y) * eased);
            if (t < 1 && top is not null) top.RequestAnimationFrame(Frame);
            else
            {
                overlay.Children.Remove(ghost);
                done.TrySetResult();
            }
        }
        Frame(default);
        return done.Task;
    }

    /// <summary>A picture of a row flying to its new place; the picture is let go once it has landed.</summary>
    public static async Task Row(OverlayLayer overlay, IImage picture, Rect from, Func<Point?> to)
    {
        await Fly(overlay, new Border { Classes = { GhostClass }, Child = new Image { Source = picture } }, from, to);
        (picture as IDisposable)?.Dispose();
    }

    /// <summary>
    /// The current-channel highlight slides from the old channel's row to the new one; the new row shows its own
    /// highlight only once it has arrived (Motion.axaml: <c>Border.row.current.arriving</c>).
    /// </summary>
    public static async Task Highlight(Border fromRow, Border toRow)
    {
        if (!Motion.IsAnimated || OverlayLayer.GetOverlayLayer(toRow) is not { } overlay
            || VisibleRect(fromRow, overlay) is not { } from || VisibleRect(toRow, overlay) is null)
            return;
        // translucent: it passes over other rows in the overlay, their names stay readable underneath
        var ghost = new Border { Classes = { HighlightClass }, CornerRadius = toRow.CornerRadius, Opacity = 0.6 };
        ghost.Bind(Border.BackgroundProperty, ghost.GetResourceObservable("Ovs.SurfaceSelected"));
        toRow.Classes.Set(ArrivingClass, true);
        await Fly(overlay, ghost, from, () => toRow.TranslatePoint(default, overlay));
        toRow.Classes.Set(ArrivingClass, false);
    }
}
