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

namespace OVS.Client.Views;

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

    /// <summary>A counter ("3/10" or "3") that ticks to its new number and pulses once when it gets full.</summary>
    public static readonly AttachedProperty<bool> TickProperty = AvaloniaProperty.RegisterAttached<TextBlock, bool>("Tick", typeof(LiveMotion));

    /// <summary>
    /// The level a meter shows: at once in the simplified display, followed smoothly in the animated one. NaN (the
    /// default, also while the meter has no model) keeps what it shows.
    /// </summary>
    public static readonly AttachedProperty<double> LevelProperty = AvaloniaProperty.RegisterAttached<RangeBase, double>("Level", typeof(LiveMotion), double.NaN);

    public static bool GetPop(Control control) => control.GetValue(PopProperty);
    public static void SetPop(Control control, bool value) => control.SetValue(PopProperty, value);
    public static bool GetTurn(Control control) => control.GetValue(TurnProperty);
    public static void SetTurn(Control control, bool value) => control.SetValue(TurnProperty, value);
    public static bool GetTick(TextBlock text) => text.GetValue(TickProperty);
    public static void SetTick(TextBlock text, bool value) => text.SetValue(TickProperty, value);
    public static double GetLevel(RangeBase meter) => meter.GetValue(LevelProperty);
    public static void SetLevel(RangeBase meter, double value) => meter.SetValue(LevelProperty, value);

    public const string GhostClass = "popGhost";
    public const double PopFrom = 0.4, TurnFrom = -90, TickBy = 6, FullPulse = 1.3;

    /// <summary>About the time the level meter takes to rise to a louder level, and to fall back to a quieter one.</summary>
    public static readonly TimeSpan Attack = TimeSpan.FromMilliseconds(80), Release = TimeSpan.FromMilliseconds(300);

    static LiveMotion()
    {
        Visual.IsVisibleProperty.Changed.AddClassHandler<Control>((control, e) =>
        {
            bool turn = GetTurn(control);
            if (!turn && !GetPop(control) || !Motion.IsAnimated || !control.IsAttachedToVisualTree()) return; // not while a row is built
            bool shown = e.GetNewValue<bool>();
            if (turn && shown) Run(control, Motion.Normal, Motion.Pop, (Visual.OpacityProperty, 0d, 1d), (RotateTransform.AngleProperty, TurnFrom, 0d));
            else if (!turn && shown) Run(control, Motion.Normal, Motion.Pop, (Visual.OpacityProperty, 0d, 1d),
                (ScaleTransform.ScaleXProperty, PopFrom, 1d), (ScaleTransform.ScaleYProperty, PopFrom, 1d));
            else if (!turn) PopOut(control);
        });
        TextBlock.TextProperty.Changed.AddClassHandler<TextBlock>((text, e) =>
        {
            if (GetTick(text) && Motion.IsAnimated && text.IsAttachedToVisualTree()
                && Count(e.GetOldValue<string?>()) is { } before && Count(e.GetNewValue<string?>()) is { } after)
                Tick(text, before, after);
        });
        LevelProperty.Changed.AddClassHandler<RangeBase>((meter, e) => Follow(meter, e.GetNewValue<double>()));
    }

    /// <summary>Fades a control in, e.g. a busy spinner that shows or the content a busy button gets back.</summary>
    public static void FadeIn(Visual? visual, double from = 0)
    {
        if (visual is not null && Motion.IsAnimated) Run(visual, Motion.Normal, Motion.Ease, (Visual.OpacityProperty, from, 1d));
    }

    static Task Run(Animatable target, TimeSpan duration, Easing easing, params (AvaloniaProperty Property, double From, double To)[] values) =>
        Run(target, duration, easing, FillMode.Backward, values);

    static Task Run(Animatable target, TimeSpan duration, Easing easing, FillMode fill, params (AvaloniaProperty Property, double From, double To)[] values)
    {
        var start = new KeyFrame { Cue = new Cue(0) };
        var end = new KeyFrame { Cue = new Cue(1) };
        foreach (var (property, from, to) in values)
        {
            start.Setters.Add(new Setter(property, from));
            end.Setters.Add(new Setter(property, to));
        }
        return new Animation { Duration = duration, Easing = easing, FillMode = fill, Children = { start, end } }.Play(target);
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
        await Run(ghost, Motion.Normal, Motion.Ease, FillMode.Forward, (Visual.OpacityProperty, 1d, 0d),
            (ScaleTransform.ScaleXProperty, 1d, PopFrom), (ScaleTransform.ScaleYProperty, 1d, PopFrom));
        overlay.Children.Remove(ghost);
    }

    /// <summary>"3/10" is 3 of at most 10, "3" is 3 without a limit; null when it is no counter.</summary>
    internal static (int Count, int Max)? Count(string? text)
    {
        var parts = (text ?? "").Split('/');
        if (!int.TryParse(parts[0], out int count)) return null;
        if (parts.Length == 1) return (count, 0);
        return parts.Length == 2 && int.TryParse(parts[1], out int max) ? (count, max) : null;
    }

    static bool IsFull((int Count, int Max) slots) => slots.Max > 0 && slots.Count >= slots.Max;

    static void Tick(TextBlock text, (int Count, int Max) before, (int Count, int Max) after)
    {
        if (after == before) return;
        // a higher number comes up from below, a lower one down from above
        Run(text, Motion.Normal, Motion.Ease, (TranslateTransform.YProperty, after.Count >= before.Count ? TickBy : -TickBy, 0d), (Visual.OpacityProperty, 0d, 1d));
        if (!IsFull(after) || IsFull(before)) return;
        var pulse = new Animation { Duration = Motion.Slow, Easing = Motion.Ease };
        foreach (var (cue, scale) in new[] { (0d, 1d), (0.4, FullPulse), (1d, 1d) })
            pulse.Children.Add(new KeyFrame { Cue = new Cue(cue), Setters = { new Setter(ScaleTransform.ScaleXProperty, scale), new Setter(ScaleTransform.ScaleYProperty, scale) } });
        _ = pulse.Play(text);
    }

    sealed class Follower
    {
        public double Target;
        public long Last;
        public bool Running;
        public object? Model;
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
        if (!Motion.IsAnimated || fresh || !meter.IsEffectivelyVisible || TopLevel.GetTopLevel(meter) is not { } top)
        {
            meter.Value = level;
            return;
        }
        if (follower.Running) return;
        follower.Running = true;
        follower.Last = Stopwatch.GetTimestamp();
        top.RequestAnimationFrame(Step);

        void Step(TimeSpan _)
        {
            double ms = Stopwatch.GetElapsedTime(follower.Last).TotalMilliseconds;
            follower.Last = Stopwatch.GetTimestamp();
            double target = Math.Clamp(follower.Target, meter.Minimum, meter.Maximum), gap = target - meter.Value;
            double tau = (gap > 0 ? Attack : Release).TotalMilliseconds / 4;
            if (!Motion.IsAnimated || Math.Abs(gap) < (meter.Maximum - meter.Minimum) / 100) meter.Value = target; // the last per cent at once
            else meter.Value += gap * (1 - Math.Exp(-ms / tau));
            if (meter.Value == target)
            {
                meter.Value = follower.Target; // the level itself, as in the simplified display
                follower.Running = false;
            }
            else top.RequestAnimationFrame(Step);
        }
    }
}
