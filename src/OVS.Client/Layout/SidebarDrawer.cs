using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using OVS.Client.Animations;

namespace OVS.Client.Layout;

/// <summary>
/// Package 68, 105: below the compact width the sidebar is a drawer over the page. The window carries the class
/// "drawer" while it shows; it slides in from the left with its scrim and slides out again.
/// </summary>
#pragma warning disable CA1001 // its cancellation sources have no timer, so nothing needs freeing
internal sealed class SidebarDrawer
#pragma warning restore CA1001
{
    readonly Window window;
    readonly Control sidebar;
    readonly Control scrim;
    CancellationTokenSource? cancel;

    /// <param name="window">The window whose classes say "compact" and get "drawer".</param>
    /// <param name="sidebar">The sidebar that slides.</param>
    /// <param name="scrim">The dim layer behind the open drawer.</param>
    internal SidebarDrawer(Window window, Control sidebar, Control scrim)
    {
        this.window = window;
        this.sidebar = sidebar;
        this.scrim = scrim;
    }

    /// <summary>Whether the drawer counts as open; closed counts at once, while it still slides away.</summary>
    internal bool IsOpen { get; private set; }

    /// <summary>Opens or closes the drawer; it only opens in the compact width and only slides in the animated display.</summary>
    internal void Set(bool open)
    {
        bool was = IsOpen;
        IsOpen = open && window.Classes.Contains("compact");
        if (IsOpen == was || !Motion.IsAnimated(window) || !window.Classes.Contains("compact"))
        {
            cancel?.Cancel();
            Still();
            window.Classes.Set("drawer", IsOpen);
            return;
        }
        _ = SlideAsync(IsOpen);
    }

    /// <summary>
    /// Package 105: the drawer slides in from the left with its scrim; closing slides it out. Closed counts at once
    /// (keys, clicks); the sidebar only stays visible until it has slid away.
    /// </summary>
    async Task SlideAsync(bool open)
    {
#pragma warning disable CA1849 // Cancel, not CancelAsync: the running slide must stop before the new one starts below
        cancel?.Cancel();
#pragma warning restore CA1849
        var slideCancel = cancel = new CancellationTokenSource();
        if (open) window.Classes.Set("drawer", true);
        sidebar.IsHitTestVisible = open;
        window.UpdateLayout();
        double width = sidebar.Bounds.Width;
        double from = open ? -width : 0, to = open ? 0 : -width;
        var slide = new Avalonia.Animation.Animation
        {
            Duration = Motion.Normal,
            Easing = Motion.Ease,
            FillMode = open ? Avalonia.Animation.FillMode.Backward : Avalonia.Animation.FillMode.Forward,
            Children =
            {
                new Avalonia.Animation.KeyFrame { Cue = new Avalonia.Animation.Cue(0), Setters = { new Avalonia.Styling.Setter(TranslateTransform.XProperty, from) } },
                new Avalonia.Animation.KeyFrame { Cue = new Avalonia.Animation.Cue(1), Setters = { new Avalonia.Styling.Setter(TranslateTransform.XProperty, to) } },
            },
        }.PlayAsync(sidebar, slideCancel.Token);
        var dim = new Avalonia.Animation.Animation
        {
            Duration = Motion.Normal,
            Easing = Motion.Ease,
            FillMode = open ? Avalonia.Animation.FillMode.Backward : Avalonia.Animation.FillMode.Forward,
            Children =
            {
                new Avalonia.Animation.KeyFrame { Cue = new Avalonia.Animation.Cue(0), Setters = { new Avalonia.Styling.Setter(Visual.OpacityProperty, open ? 0d : 1d) } },
                new Avalonia.Animation.KeyFrame { Cue = new Avalonia.Animation.Cue(1), Setters = { new Avalonia.Styling.Setter(Visual.OpacityProperty, open ? 1d : 0d) } },
            },
        }.PlayAsync(scrim, slideCancel.Token);
        await Task.WhenAll(slide, dim);
        if (slideCancel.IsCancellationRequested) return;
        if (!open) window.Classes.Set("drawer", false);
        Still();
    }

    /// <summary>
    /// The drawer as it rests: a finished slide-out keeps its last values (moved away, faded), so they go once it is
    /// hidden; the next slide starts from its place.
    /// </summary>
    void Still()
    {
        sidebar.ClearValue(InputElement.IsHitTestVisibleProperty);
        sidebar.ClearValue(Visual.RenderTransformProperty);
        scrim.ClearValue(Visual.OpacityProperty);
    }
}
