using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace OVS.Client.Behaviors;

/// <summary>The pictures of the drags and the flights: the lifted preview, how it follows the pointer, a control's snapshot.</summary>
internal static class DragVisuals
{
    /// <summary>The lifted picture of <paramref name="source"/> (A112), not yet placed; also the user drag's (Package 120).</summary>
    internal static Border Lift(Control source)
    {
        var transforms = new TransformGroup();
        transforms.Children.Add(new ScaleTransform(ReorderDrag.Scale, ReorderDrag.Scale));
        transforms.Children.Add(new RotateTransform(ReorderDrag.Tilt));
        return new Border
        {
            Classes = { "dragPreview" },
            Width = source.Bounds.Width,
            Height = source.Bounds.Height,
            IsHitTestVisible = false,
            RenderTransform = transforms,
            Child = new Image { Source = Snapshot(source) },
        };
    }

    /// <summary>Keeps the point where the item was grabbed under the pointer.</summary>
    internal static void Follow(Border preview, PointerEventArgs args, Point grab)
    {
        if (preview.Parent is not Visual overlay) return;
        var at = args.GetPosition(overlay) - grab;
        Canvas.SetLeft(preview, at.X);
        Canvas.SetTop(preview, at.Y);
    }

    /// <summary>A picture of the control as it looks now (also the flying row of Package 103).</summary>
    /// <param name="scale">For a control no longer shown (a closed popup): the scaling of the window it is drawn in.</param>
    /// <param name="control">The control to draw.</param>
    internal static RenderTargetBitmap Snapshot(Control control, double? scale = null)
    {
        double scaling = scale ?? TopLevel.GetTopLevel(control)?.RenderScaling ?? 1;
        var size = new PixelSize(Math.Max(1, (int)Math.Ceiling(control.Bounds.Width * scaling)), Math.Max(1, (int)Math.Ceiling(control.Bounds.Height * scaling)));
        var bitmap = new RenderTargetBitmap(size, new Vector(96 * scaling, 96 * scaling));
        // Subpixel text on a transparent bitmap gets dark fringes; grayscale antialiasing stays clean on any background.
        var mode = RenderOptions.GetTextRenderingMode(control);
        RenderOptions.SetTextRenderingMode(control, TextRenderingMode.Antialias);
        bitmap.Render(control);
        RenderOptions.SetTextRenderingMode(control, mode);
        return bitmap;
    }
}
