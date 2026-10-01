using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
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

    /// <summary>
    /// Package 105: every popup (context menus, flyouts, dropdowns, tooltips) fades in and slides out of its anchor:
    /// down below it, up above it. They close at once, as Avalonia takes them away.
    /// </summary>
    static Motion() => Popup.IsOpenProperty.Changed.AddClassHandler<Popup>((popup, e) =>
    {
        if (!IsAnimated || !e.GetNewValue<bool>() || popup.Child is not { } child) return;
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
        }.RunAsync(child);
    });

    public const double ShakeBy = 6;

    /// <summary>Package 105, 106: a refused input or a failed connect shakes its card once (3 times 6 px).</summary>
    public static void Shake(Animatable target)
    {
        if (!IsAnimated) return;
        var shake = new Animation { Duration = TimeSpan.FromMilliseconds(360), Easing = Ease };
        double[] steps = [0, -ShakeBy, ShakeBy, -ShakeBy, ShakeBy, -ShakeBy / 2, 0];
        for (int i = 0; i < steps.Length; i++)
            shake.Children.Add(new KeyFrame { Cue = new Cue((double)i / (steps.Length - 1)), Setters = { new Setter(TranslateTransform.XProperty, steps[i]) } });
        _ = shake.RunAsync(target);
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
