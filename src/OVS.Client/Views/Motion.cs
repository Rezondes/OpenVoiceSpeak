using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OVS.Client.Settings;

namespace OVS.Client.Views;

/// <summary>
/// Package 99 (A114 to A116): the animated display. Styles react to the class <c>animated</c> on the window
/// (all of them in <c>Styles/Motion.axaml</c>, like the width classes of <see cref="Responsive"/>); animations
/// started from code ask <see cref="IsAnimated"/> or take their length from <see cref="Duration"/>.
/// </summary>
public static class Motion
{
    /// <summary>Hover and press.</summary>
    public static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(120);

    /// <summary>Items appearing and leaving, tabs, dialogs.</summary>
    public static readonly TimeSpan Normal = TimeSpan.FromMilliseconds(220);

    /// <summary>Pages, connecting, moves.</summary>
    public static readonly TimeSpan Slow = TimeSpan.FromMilliseconds(320);

    /// <summary>The usual curve; <see cref="Pop"/> overshoots a little for things that pop in.</summary>
    public static readonly Easing Ease = new CubicEaseOut(), Pop = new BackEaseOut();

    /// <summary>Package 105: how far a popup slides from its anchor while it fades in.</summary>
    public const double PopupSlide = 4;

    public const string PopupGhostClass = "popupGhost";

    /// <summary>
    /// Package 105: every popup (context menus, flyouts, dropdowns, tooltips) fades in and slides out of its anchor:
    /// down below it, up above it. Package 109: they close at once, as Avalonia takes them away (keys and clicks no
    /// longer reach them), and a picture of them fades where they were.
    /// </summary>
    static Motion() => Popup.IsOpenProperty.Changed.AddClassHandler<Popup>((popup, e) =>
    {
        if (!IsAnimated || popup.Child is not { } child) return;
        if (!e.GetNewValue<bool>())
        {
            FadeAway(popup, child);
            return;
        }
        // where it lies on the screen once laid out (closed, it has no place any more); its own slide does not count
        Dispatcher.UIThread.Post(() =>
        {
            if (popup.IsOpen && child.GetVisualParent() is { } parent && TopLevel.GetTopLevel(parent) is not null)
                popupPlaces.AddOrUpdate(popup, parent.PointToScreen(child.Bounds.Position));
        }, DispatcherPriority.Background);
        double from = popup.Placement.ToString().StartsWith("Top", StringComparison.Ordinal) ? PopupSlide : -PopupSlide;
        _ = new Animation
        {
            Duration = Normal,
            Easing = Ease,
            FillMode = FillMode.Backward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 0d), new Setter(TranslateTransform.YProperty, from) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 1d), new Setter(TranslateTransform.YProperty, 0d) } },
            },
        }.Play(child);
    });

    static readonly ConditionalWeakTable<Popup, object> popupPlaces = [];

    /// <summary>
    /// A picture of the closed popup in the window's overlay fades out where the popup was. The overlay lies above the
    /// dialogs, so not when a dialog is open or opens with the close (a menu entry that asks something).
    /// </summary>
    static async void FadeAway(Popup popup, Control child)
    {
        var anchor = popup.PlacementTarget ?? popup;
        if (!popupPlaces.TryGetValue(popup, out var place) || place is not PixelPoint screen || child.Bounds.Width <= 0
            || OverlayLayer.GetOverlayLayer(anchor) is not { } overlay || TopLevel.GetTopLevel(anchor) is not { } top)
            return;
        popupPlaces.Remove(popup);
        Image? ghost = null;
        IDisposable? picture = null;
        try
        {
            var bitmap = ReorderDrag.Snapshot(child, top.RenderScaling); // now, while it is still laid out
            picture = bitmap;
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background); // after what the close started
            if (top is MainWindow { Overlay.IsOpen: true } || !IsAnimated) return;
            var at = overlay.PointToClient(screen);
            ghost = new Image { Source = bitmap, Width = child.Bounds.Width, Height = child.Bounds.Height, IsHitTestVisible = false, Classes = { PopupGhostClass } };
            Canvas.SetLeft(ghost, at.X);
            Canvas.SetTop(ghost, at.Y);
            overlay.Children.Add(ghost);
            await FadeOut(ghost);
        }
        catch (Exception)
        {
            // only a picture: if it cannot be taken or shown, the popup is just gone, as in the simplified display
        }
        finally
        {
            if (ghost is not null) overlay.Children.Remove(ghost);
            picture?.Dispose(); // also when something failed between the picture and its image
        }
    }

    static Task FadeOut(Image ghost) => new Animation
    {
        Duration = Fast,
        Easing = Ease,
        FillMode = FillMode.Forward, // stays faded until it is taken away
        Children =
        {
            new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 1d) } },
            new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 0d) } },
        },
    }.Play(ghost);

    public const double ShakeBy = 6;

    /// <summary>Package 105, 106: a refused input or a failed connect shakes its card once (3 times 6 px).</summary>
    public static void Shake(Animatable target)
    {
        if (!IsAnimated) return;
        var shake = new Animation { Duration = TimeSpan.FromMilliseconds(360), Easing = Ease };
        double[] steps = [0, -ShakeBy, ShakeBy, -ShakeBy, ShakeBy, -ShakeBy / 2, 0];
        for (int i = 0; i < steps.Length; i++)
            shake.Children.Add(new KeyFrame { Cue = new Cue((double)i / (steps.Length - 1)), Setters = { new Setter(TranslateTransform.XProperty, steps[i]) } });
        _ = shake.Play(target);
    }

    /// <summary>
    /// Package 108: starts an animation from code and asks for one frame as a safety measure. In the headless tests,
    /// which drive frames by hand, an animation that started while nothing else moved (no loop runs any more when idle,
    /// A117) got no first frame, e.g. a dialog's card that plays back; in the app Avalonia already schedules a render
    /// for a new animation. Once a frame came, the clock keeps them coming while the animation runs.
    /// </summary>
    public static Task Play(this Animation animation, Animatable target, CancellationToken cancel = default)
    {
        var run = Count(animation.RunAsync(target, cancel));
        // asked after the pending work: the animation joins the clock only then
        if (target is Visual visual && TopLevel.GetTopLevel(visual) is { } top)
            Dispatcher.UIThread.Post(() => top.RequestAnimationFrame(_ => { }), DispatcherPriority.Background);
        return run;
    }

    // ---- Package 109: a test hook, every animation started from code passes here (Play, FlyGhost.Fly) ----

    static int started, running;

    /// <summary>Animations started from code since the last <see cref="ResetStarted"/>.</summary>
    public static int Started => Volatile.Read(ref started);

    /// <summary>Animations started from code that have not ended yet (finished or cancelled).</summary>
    public static int Running => Volatile.Read(ref running);

    public static void ResetStarted() => Interlocked.Exchange(ref started, 0);

    /// <summary>Counts an animation that starts and, once its task ends, no longer counts it as running.</summary>
    public static Task Count(Task run)
    {
        Interlocked.Increment(ref started);
        Interlocked.Increment(ref running);
        run.ContinueWith(_ => Interlocked.Decrement(ref running), TaskContinuationOptions.ExecuteSynchronously);
        return run;
    }

    /// <summary>The display the user chose; tests set it directly.</summary>
    public static bool IsAnimated { get; set; } = true;

    /// <summary>The length of an animation started from code: zero in the simplified display.</summary>
    public static TimeSpan Duration(TimeSpan animated) => IsAnimated ? animated : TimeSpan.Zero;

    /// <summary>Package 104: the display changed (things waiting to be shown appear at once in the simplified one).</summary>
    public static event Action? Changed;

    public static void Apply(Window window, DisplayMode mode)
    {
        bool was = IsAnimated;
        IsAnimated = mode == DisplayMode.Animated;
        window.Classes.Set("animated", IsAnimated);
        if (was != IsAnimated) Changed?.Invoke();
    }
}
