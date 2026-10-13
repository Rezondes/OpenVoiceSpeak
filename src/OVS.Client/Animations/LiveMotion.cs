using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using OVS.Client.Models;
using OVS.Client.Views;

namespace OVS.Client.Animations;

/// <summary>
/// Package 108: live states that animate from code in the animated display (the loops are styles in
/// <c>Styles/Motion.axaml</c>). State icons pop in and out, the own mute and deafen icons turn in, the slot counter
/// ticks to its new number and pulses once when the channel fills up, the level meter follows with a fast attack and a
/// slower release. Nothing of it runs in the simplified display, nor while a row is first built.
/// </summary>
public static class LiveMotion
{
    /// <summary>The icon pops in (scale and fade) when it shows, and a copy of it pops out when it hides.</summary>
    public static readonly AttachedProperty<bool> PopProperty = AvaloniaProperty.RegisterAttached<Control, bool>("Pop", typeof(LiveMotion));

    /// <summary>The icon turns in with a short rotation when it shows (the own mute and deafen buttons).</summary>
    public static readonly AttachedProperty<bool> TurnProperty = AvaloniaProperty.RegisterAttached<Control, bool>("Turn", typeof(LiveMotion));

    /// <summary>
    /// The numbers of a slot counter ("3/10" or "3"): when they change it ticks to its new number, and it pulses once when
    /// the channel gets full. Null (the default, also while the row has no model) ticks nothing.
    /// </summary>
    public static readonly AttachedProperty<ChannelSlots?> SlotsProperty = AvaloniaProperty.RegisterAttached<TextBlock, ChannelSlots?>("Slots", typeof(LiveMotion));

    /// <summary>Package 109: a text that fades in anew when it changes (a renamed channel).</summary>
    public static readonly AttachedProperty<bool> FadeProperty = AvaloniaProperty.RegisterAttached<TextBlock, bool>("Fade", typeof(LiveMotion));

    /// <summary>
    /// The level a meter shows: at once in the simplified display, followed smoothly in the animated one. NaN (the
    /// default, also while the meter has no model) keeps what it shows.
    /// </summary>
    public static readonly AttachedProperty<double> LevelProperty = AvaloniaProperty.RegisterAttached<RangeBase, double>("Level", typeof(LiveMotion), double.NaN);

    /// <summary>Gets whether the icon pops in and out with its visibility.</summary>
    public static bool GetPop(Control control) => control.GetValue(PopProperty);
    /// <summary>Sets whether the icon pops in and out with its visibility.</summary>
    public static void SetPop(Control control, bool value) => control.SetValue(PopProperty, value);
    /// <summary>Gets whether the icon turns in as it appears.</summary>
    public static bool GetTurn(Control control) => control.GetValue(TurnProperty);
    /// <summary>Sets whether the icon turns in as it appears.</summary>
    public static void SetTurn(Control control, bool value) => control.SetValue(TurnProperty, value);
    /// <summary>Gets the channel slots the counter shows.</summary>
    public static ChannelSlots? GetSlots(TextBlock text) => text.GetValue(SlotsProperty);
    /// <summary>Sets the channel slots the counter shows; a change ticks it and a full channel pulses.</summary>
    public static void SetSlots(TextBlock text, ChannelSlots? value) => text.SetValue(SlotsProperty, value);
    /// <summary>Gets whether a new text fades in.</summary>
    public static bool GetFade(TextBlock text) => text.GetValue(FadeProperty);
    /// <summary>Sets whether a new text fades in.</summary>
    public static void SetFade(TextBlock text, bool value) => text.SetValue(FadeProperty, value);
    /// <summary>Gets the level the meter moves to.</summary>
    public static double GetLevel(RangeBase meter) => meter.GetValue(LevelProperty);
    /// <summary>Sets the level the meter moves to, with a fast attack and a slow release.</summary>
    public static void SetLevel(RangeBase meter, double value) => meter.SetValue(LevelProperty, value);

    /// <summary>The style class of the picture a popping icon leaves behind.</summary>
    public const string GhostClass = "popGhost";
    /// <summary>The pop's starting scale, the turn's starting angle, the counter's tick in pixels and the full pulse's scale.</summary>
    public const double PopFrom = 0.4, TurnFrom = -90, TickBy = 6, FullPulse = 1.3;

    /// <summary>About the time the level meter takes to rise to a louder level, and to fall back to a quieter one.</summary>
    public static readonly TimeSpan Attack = TimeSpan.FromMilliseconds(80), Release = TimeSpan.FromMilliseconds(300);

    static LiveMotion()
    {
        Visual.IsVisibleProperty.Changed.AddClassHandler<Control>((control, args) =>
        {
            bool turn = GetTurn(control);
            if (!turn && !GetPop(control) || !Motion.IsAnimated(control) || !control.IsAttachedToVisualTree()) return; // not while a row is built
            bool shown = args.GetNewValue<bool>();
            if (turn && shown) RunAsync(control, Motion.Normal, Motion.Pop, FillMode.Backward, Replace(control), (Visual.OpacityProperty, 0d, 1d), (RotateTransform.AngleProperty, TurnFrom, 0d));
            else if (!turn && shown) RunAsync(control, Motion.Normal, Motion.Pop, FillMode.Backward, Replace(control), (Visual.OpacityProperty, 0d, 1d),
                (ScaleTransform.ScaleXProperty, PopFrom, 1d), (ScaleTransform.ScaleYProperty, PopFrom, 1d));
            else if (!turn)
            {
                Replace(control); // hidden: its pop-in ends
                PopOut(control);
            }
        });
        SlotsProperty.Changed.AddClassHandler<TextBlock>((text, args) =>
        {
            if (Motion.IsAnimated(text) && text.IsAttachedToVisualTree()
                && args.GetOldValue<ChannelSlots?>() is { } before && args.GetNewValue<ChannelSlots?>() is { } after)
                Tick(text, before, after);
        });
        TextBlock.TextProperty.Changed.AddClassHandler<TextBlock>((text, args) =>
        {
            if (GetFade(text) && Motion.IsAnimated(text) && text.IsAttachedToVisualTree() && args.GetOldValue<string?>() is not null)
                FadeIn(text);
        });
        LevelProperty.Changed.AddClassHandler<RangeBase>((meter, args) => Follow(meter, args.GetNewValue<double>()));
    }

    /// <summary>Fades a control in, e.g. a busy spinner that shows or the content a busy button gets back.</summary>
    public static void FadeIn(Visual? visual, double from = 0)
    {
        if (visual is not null && Motion.IsAnimated(visual)) RunAsync(visual, Motion.Normal, Motion.Ease, (Visual.OpacityProperty, from, 1d));
    }

    static Task RunAsync(Animatable target, TimeSpan duration, Easing easing, params (AvaloniaProperty Property, double From, double To)[] values) =>
        RunAsync(target, duration, easing, FillMode.Backward, default, values);

    static Task RunAsync(Animatable target, TimeSpan duration, Easing easing, FillMode fill, CancellationToken cancel,
        params (AvaloniaProperty Property, double From, double To)[] values)
    {
        var start = new KeyFrame { Cue = new Cue(0) };
        var end = new KeyFrame { Cue = new Cue(1) };
        foreach (var (property, from, to) in values)
        {
            start.Setters.Add(new Setter(property, from));
            end.Setters.Add(new Setter(property, to));
        }
        return new Animation { Duration = duration, Easing = easing, FillMode = fill, Children = { start, end } }.PlayAsync(target, cancel);
    }

    static readonly ConditionalWeakTable<Control, CancellationTokenSource> moving = [];

    /// <summary>
    /// Package 109: a control's new state ends its last movement at once (a counter that changes often, an icon that
    /// shows and hides quickly); otherwise the movements would stack up and end one per frame long after the last change.
    /// </summary>
    static CancellationToken Replace(Control control)
    {
        if (moving.TryGetValue(control, out var last)) last.Cancel();
        var next = new CancellationTokenSource();
        moving.AddOrUpdate(control, next);
        return next.Token;
    }

    /// <summary>
    /// The hidden icon is gone at once; a copy of it in the overlay shrinks and fades where it was. Only where the icon
    /// could be seen: not in a closed drawer or a row scrolled away, and not over an open dialog.
    /// </summary>
    static async void PopOut(Control control)
    {
        if (control is not PathIcon icon || icon.Bounds.Width <= 0 || OverlayLayer.GetOverlayLayer(icon) is not { } overlay
            || icon.GetVisualParent() is not Control parent || FlyGhost.VisibleRect(parent, overlay) is null
            || TopLevel.GetTopLevel(icon) is MainWindow { Overlay.IsOpen: true } || icon.TranslatePoint(default, overlay) is not { } at)
            return;
        var ghost = new PathIcon
        {
            Classes = { GhostClass },
            Data = icon.Data,
            Foreground = icon.Foreground,
            Width = icon.Bounds.Width,
            Height = icon.Bounds.Height,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(ghost, at.X);
        Canvas.SetTop(ghost, at.Y);
        overlay.Children.Add(ghost);
        // forward: the copy stays faded until it is taken away
        await RunAsync(ghost, Motion.Normal, Motion.Ease, FillMode.Forward, default, (Visual.OpacityProperty, 1d, 0d),
            (ScaleTransform.ScaleXProperty, 1d, PopFrom), (ScaleTransform.ScaleYProperty, 1d, PopFrom));
        overlay.Children.Remove(ghost);
    }

    static void Tick(TextBlock text, ChannelSlots before, ChannelSlots after)
    {
        var cancel = Replace(text);
        // a higher number comes up from below, a lower one down from above
        RunAsync(text, Motion.Normal, Motion.Ease, FillMode.Backward, cancel, (TranslateTransform.YProperty, after.UserCount >= before.UserCount ? TickBy : -TickBy, 0d), (Visual.OpacityProperty, 0d, 1d));
        if (!after.IsFull || before.IsFull) return;
        var pulse = new Animation { Duration = Motion.Slow, Easing = Motion.Ease };
        foreach (var (cue, scale) in new[] { (0d, 1d), (0.4, FullPulse), (1d, 1d) })
            pulse.Children.Add(new KeyFrame { Cue = new Cue(cue), Setters = { new Setter(ScaleTransform.ScaleXProperty, scale), new Setter(ScaleTransform.ScaleYProperty, scale) } });
        _ = pulse.PlayAsync(text, cancel);
    }

    static readonly ConditionalWeakTable<RangeBase, Follower> followers = new();

    /// <summary>
    /// The meter moves towards the level every frame and arrives within about <see cref="Attack"/> when it rises and
    /// <see cref="Release"/> when it falls (98 % of the way, the last per cent at once). The frames stop once it has arrived.
    /// </summary>
    static void Follow(RangeBase meter, double level)
    {
        if (double.IsNaN(level)) return;
        var follower = followers.GetOrCreateValue(meter);
        follower.Target = level;
        // a new model (the settings opened again) or a meter nobody sees shows the level at once, no sweep from the old one
        bool fresh = !ReferenceEquals(follower.Model, meter.DataContext);
        follower.Model = meter.DataContext;
        if (!Motion.IsAnimated(meter) || fresh || !meter.IsEffectivelyVisible || TopLevel.GetTopLevel(meter) is not { } top)
        {
            meter.Value = level;
            return;
        }
        if (follower.Running) return;
        follower.Running = true;
        follower.Last = Stopwatch.GetTimestamp(); // clock: frame timing
        top.RequestAnimationFrame(Step);

        void Step(TimeSpan _)
        {
            double milliseconds = Stopwatch.GetElapsedTime(follower.Last).TotalMilliseconds; // clock: frame timing
            follower.Last = Stopwatch.GetTimestamp(); // clock: frame timing
            double target = Math.Clamp(follower.Target, meter.Minimum, meter.Maximum), gap = target - meter.Value;
            double tau = (gap > 0 ? Attack : Release).TotalMilliseconds / 4;
            if (!Motion.IsAnimated(top) || Math.Abs(gap) < (meter.Maximum - meter.Minimum) / 100) meter.Value = target; // the last per cent at once
            else meter.Value += gap * (1 - Math.Exp(-milliseconds / tau));
            if (meter.Value == target)
            {
                meter.Value = follower.Target; // the level itself, as in the simplified display
                follower.Running = false;
            }
            else top.RequestAnimationFrame(Step);
        }
    }

    sealed class Follower
    {
        public double Target;
        public long Last;
        public bool Running;
        public object? Model;
    }
}
