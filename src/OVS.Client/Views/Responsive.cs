using Avalonia;
using Avalonia.Controls;

namespace OVS.Client.Views;

/// <summary>
/// Package 68 (A93): the two width steps as classes on the window, so every page reacts by style
/// (e.g. <c>Window.narrow TextBlock.wideOnly</c>) instead of code.
/// </summary>
public static class Responsive
{
    /// <summary>Below this window width the sidebar becomes a drawer.</summary>
    public const double CompactBelow = 700;

    /// <summary>Below this main-area width pages switch to their narrow layout.</summary>
    public const double NarrowBelow = 560;

    /// <summary>A82: the side-by-side sidebar never gets narrower than this for the header's sake.</summary>
    public const double MinSidebar = 200;

    /// <summary>The drawer leaves this much of the window uncovered, so a click beside it closes it.</summary>
    public const double DrawerGap = 48;

    public static bool IsCompact(double windowWidth) => windowWidth < CompactBelow;

    /// <summary>A82: as wide as possible while the main area still holds the header without its title.</summary>
    public static double SidebarMax(double windowWidth, double splitter, double header) =>
        Math.Max(MinSidebar, windowWidth - splitter - header);

    public static void Apply(Window window, double windowWidth, double mainWidth)
    {
        window.Classes.Set("compact", IsCompact(windowWidth));
        window.Classes.Set("narrow", mainWidth < NarrowBelow);
    }

    /// <summary>
    /// Package 77: a control that needs more room than the narrow step promises sets its own break width in XAML
    /// (<c>v:Responsive.StackBelow="660"</c>) and gets the class <c>stacked</c> while it is narrower.
    /// </summary>
    public static readonly AttachedProperty<double> StackBelowProperty =
        AvaloniaProperty.RegisterAttached<Control, double>("StackBelow", typeof(Responsive));

    public static double GetStackBelow(Control control) => control.GetValue(StackBelowProperty);
    public static void SetStackBelow(Control control, double value) => control.SetValue(StackBelowProperty, value);

    static Responsive() => StackBelowProperty.Changed.AddClassHandler<Control>((control, _) =>
    {
        control.SizeChanged -= OnStackSizeChanged;
        control.SizeChanged += OnStackSizeChanged;
    });

    static void OnStackSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var control = (Control)sender!;
        control.Classes.Set("stacked", e.NewSize.Width < GetStackBelow(control));
    }
}
