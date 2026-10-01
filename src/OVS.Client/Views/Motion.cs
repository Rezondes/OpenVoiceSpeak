using Avalonia.Animation.Easings;
using Avalonia.Controls;
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
