using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OVS.Client.Behaviors;
using OVS.Client.Views;

namespace OVS.Client.Animations;

/// <summary>
/// Package 105: every popup (context menus, flyouts, dropdowns, tooltips) fades in and slides out of its anchor:
/// down below it, up above it. Package 109: they close at once, as Avalonia takes them away (keys and clicks no
/// longer reach them), and a picture of them fades where they were.
/// </summary>
internal static class PopupMotion
{
    /// <summary>Package 105: how far a popup slides from its anchor while it fades in.</summary>
    const double Slide = 4;

    internal const string GhostClass = "popupGhost";

    static bool registered;

    /// <summary>
    /// Hooks the opening and closing of every popup, once: a second call adds no second handler. The app calls it at its
    /// start (<c>App.OnFrameworkInitializationCompleted</c>); the headless tests build the app anew for every test, so
    /// there it is called once per test.
    /// </summary>
    internal static void Register()
    {
        if (registered) return;
        registered = true;
        Popup.IsOpenProperty.Changed.AddClassHandler<Popup>(OnIsOpenChanged);
    }

    static void OnIsOpenChanged(Popup popup, AvaloniaPropertyChangedEventArgs args)
    {
        // the display of the window it opens in, asked through its anchor like the closing picture below
        if (!Motion.IsAnimated(popup.PlacementTarget ?? popup) || popup.Child is not { } child) return;
        if (!args.GetNewValue<bool>())
        {
            _ = FadeAwayAsync(popup, child);
            return;
        }
        // where it lies on the screen once laid out (closed, it has no place any more); its own slide does not count
        Dispatcher.UIThread.Post(() =>
        {
            if (popup.IsOpen && child.GetVisualParent() is { } parent && TopLevel.GetTopLevel(parent) is not null)
                popupPlaces.AddOrUpdate(popup, parent.PointToScreen(child.Bounds.Position));
        }, DispatcherPriority.Background);
        double from = popup.Placement.ToString().StartsWith("Top", StringComparison.Ordinal) ? Slide : -Slide;
        var start = new KeyFrame { Cue = new Cue(0), Setters = { new Setter(TranslateTransform.YProperty, from) } };
        var end = new KeyFrame { Cue = new Cue(1), Setters = { new Setter(TranslateTransform.YProperty, 0d) } };
        // Package 115: a tooltip fades in by its theme already; a second fade on top made it blink once more at the end
        if (child is not ToolTip)
        {
            start.Setters.Add(new Setter(Visual.OpacityProperty, 0d));
            end.Setters.Add(new Setter(Visual.OpacityProperty, 1d));
        }
        _ = new Animation { Duration = Motion.Normal, Easing = Motion.Ease, FillMode = FillMode.Backward, Children = { start, end } }.PlayAsync(child);
    }

    static readonly ConditionalWeakTable<Popup, object> popupPlaces = [];

    /// <summary>
    /// A picture of the closed popup in the window's overlay fades out where the popup was. The overlay lies above the
    /// dialogs, so not when a dialog is open or opens with the close (a menu entry that asks something).
    /// </summary>
    static async Task FadeAwayAsync(Popup popup, Control child)
    {
        var anchor = popup.PlacementTarget ?? popup;
        if (!popupPlaces.TryGetValue(popup, out var place) || place is not PixelPoint screen || child.Bounds.Width <= 0
            || OverlayLayer.GetOverlayLayer(anchor) is not { } overlay || TopLevel.GetTopLevel(anchor) is not { } top)
            return;
        popupPlaces.Remove(popup);
        Image? ghost = null;
        RenderTargetBitmap? picture = null;
        try
        {
            var bitmap = DragVisuals.Snapshot(child, top.RenderScaling); // now, while it is still laid out
            picture = bitmap;
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background); // after what the close started
            if (top is MainWindow { Overlay.IsOpen: true } || !Motion.IsAnimated(top)) return;
            var at = overlay.PointToClient(screen);
            ghost = new Image { Source = bitmap, Width = child.Bounds.Width, Height = child.Bounds.Height, IsHitTestVisible = false, Classes = { GhostClass } };
            Canvas.SetLeft(ghost, at.X);
            Canvas.SetTop(ghost, at.Y);
            overlay.Children.Add(ghost);
            await FadeOutAsync(ghost);
        }
        #pragma warning disable CA1031 // a picture that cannot fade away must not take the window down
        catch (Exception)
        #pragma warning restore CA1031
        {
            // only a picture: if it cannot be taken or shown, the popup is just gone, as in the simplified display
        }
        finally
        {
            if (ghost is not null) overlay.Children.Remove(ghost);
            picture?.Dispose(); // also when something failed between the picture and its image
        }
    }

    static Task FadeOutAsync(Image ghost) => new Animation
    {
        Duration = Motion.Fast,
        Easing = Motion.Ease,
        FillMode = FillMode.Forward, // stays faded until it is taken away
        Children =
        {
            new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 1d) } },
            new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 0d) } },
        },
    }.PlayAsync(ghost);
}
