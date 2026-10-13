using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using OVS.Client.Settings;

namespace OVS.Client.Appearance;

/// <summary>
/// Package 61: makes the large backgrounds see-through. Only these brushes change their opacity, in both themes, so
/// text, cards, menus and dialogs (own brushes Ovs.DialogBg and Ovs.DialogBar) stay solid. Clicks on see-through
/// parts still reach the window: Avalonia draws with DirectComposition, not as a layered window (A72).
/// </summary>
internal static class BackgroundTransparency
{
    /// <summary>The resource keys of the large backgrounds that turn see-through.</summary>
    public static readonly IReadOnlyList<string> BackgroundKeys = ["Ovs.Bg", "Ovs.Sidebar", "Ovs.SidebarFooter"];

    /// <summary>Applies the background opacity and blur to the app's backgrounds and the window.</summary>
    public static void Apply(Application app, Window window, BackgroundAppearance appearance)
    {
        foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
            foreach (var key in BackgroundKeys)
                if (app.Resources.TryGetResource(key, variant, out var value) && value is SolidColorBrush brush)
                    brush.Opacity = appearance.Opacity;
        // At 100 % without blur the window stays a plain, opaque one.
        window.TransparencyLevelHint = appearance is { Opacity: >= 1f, Blur: false } ? []
            : appearance.Blur ? [WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Transparent]
            : [WindowTransparencyLevel.Transparent];
    }
}
