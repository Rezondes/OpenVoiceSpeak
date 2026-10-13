using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Threading;
using OVS.Client.Layout;
using OVS.Client.Settings;

namespace OVS.Client.Animations;

/// <summary>
/// Package 99 (A114 to A116): the animated display. Styles react to the class <c>animated</c> on the window
/// (all of them in <c>Styles/Motion.axaml</c>, like the width classes of <see cref="Responsive"/>); animations
/// started from code run through <see cref="PlayAsync"/>, ask <see cref="IsAnimated"/> or take their length from
/// <see cref="ChooseDuration"/>. Package 96: what Windows' own animation setting turns off asks <see cref="IsReduced"/>.
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

    /// <summary>
    /// Package 108: starts an animation from code and asks for one frame as a safety measure. In the headless tests,
    /// which drive frames by hand, an animation that started while nothing else moved (no loop runs any more when idle,
    /// A117) got no first frame, e.g. a dialog's card that plays back; in the app Avalonia already schedules a render
    /// for a new animation. Once a frame came, the clock keeps them coming while the animation runs.
    /// </summary>
    public static Task PlayAsync(this Animation animation, Animatable target, CancellationToken cancel = default)
    {
        var run = AnimationCounter.CountAsync(animation.RunAsync(target, cancel));
        // asked after the pending work: the animation joins the clock only then
        if (target is Visual visual && TopLevel.GetTopLevel(visual) is { } top)
            Dispatcher.UIThread.Post(() => top.RequestAnimationFrame(_ => { }), DispatcherPriority.Background);
        return run;
    }

    /// <summary>
    /// The display the user chose, set on the window by <see cref="Apply"/>. Everything in the window inherits it, its
    /// popups too (through their placement target); a control in no window counts as animated. Tests set it per window.
    /// Package 104: a change reaches every control in the window as a change of this property (things waiting to be
    /// shown appear at once in the simplified display).
    /// </summary>
    internal static readonly AttachedProperty<DisplayMode> DisplayProperty =
        AvaloniaProperty.RegisterAttached<Visual, DisplayMode>("Display", typeof(Motion), DisplayMode.Animated, inherits: true);

    /// <summary>Whether <paramref name="visual"/> shows the animated display, the one of its window.</summary>
    internal static bool IsAnimated(Visual visual) => visual.GetValue(DisplayProperty) == DisplayMode.Animated;

    /// <summary>The length of an animation started from code: zero in the simplified display.</summary>
    internal static TimeSpan ChooseDuration(Visual visual, TimeSpan animated) => IsAnimated(visual) ? animated : TimeSpan.Zero;

    /// <summary>Sets the display of <paramref name="window"/>: its class <c>animated</c> for the styles and <see cref="DisplayProperty"/> for the code.</summary>
    public static void Apply(Window window, DisplayMode mode)
    {
        window.Classes.Set("animated", mode == DisplayMode.Animated);
        window.SetValue(DisplayProperty, mode);
    }

    /// <summary>
    /// Package 96: the Windows setting "Show animations in Windows" is off, so a drag's gap opens without sliding (its
    /// tilt stays, a static pose and not an animation). Set on the window by <see cref="FollowReduced"/> and inherited like
    /// <see cref="DisplayProperty"/>; not reduced by default. Tests set it per window.
    /// </summary>
    internal static readonly AttachedProperty<bool> ReducedProperty =
        AvaloniaProperty.RegisterAttached<Visual, bool>("Reduced", typeof(Motion), false, inherits: true);

    /// <summary>Whether <paramref name="visual"/> is in a window whose OS setting turns animations off.</summary>
    internal static bool IsReduced(Visual visual) => visual.GetValue(ReducedProperty);

    /// <summary>
    /// Sets <see cref="ReducedProperty"/> on the window from <paramref name="read"/> now and whenever the window comes to
    /// the front again: the setting is changed in another window, so it can have changed only then.
    /// </summary>
    internal static void FollowReduced(Window window, Func<bool> read)
    {
        window.SetValue(ReducedProperty, read());
        window.Activated += (_, _) => window.SetValue(ReducedProperty, read());
    }
}
