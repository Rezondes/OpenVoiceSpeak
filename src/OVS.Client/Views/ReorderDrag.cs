using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

namespace OVS.Client.Views;

/// <summary>
/// Package 96 (A112): the one reorder drag for every list (channel tree, group list). After a 6 px move a lifted
/// preview of the item follows the pointer in the overlay layer (scale 1.03, shadow, 3 degrees tilt), the item stays
/// dimmed in its slot and a gap of its height opens where it will land. Releasing inside the list calls
/// <c>move(source, target, after)</c>; Esc or releasing outside the list cancels.
/// </summary>
public sealed partial class ReorderDrag
{
    public const double Threshold = 6, Scale = 1.03, Tilt = 3, DimmedOpacity = 0.4;

    /// <summary>
    /// The Windows setting "Show animations in Windows" is off: the gap opens without sliding. The tilt stays, it is a
    /// static pose and not an animation. Tests replace it.
    /// </summary>
    public static Func<bool> ReducedMotion { get; set; } = () =>
        OperatingSystem.IsWindows() && SystemParametersInfo(SpiGetClientAreaAnimation, 0, out var on, 0) && on == 0;

    const uint SpiGetClientAreaAnimation = 0x1042;

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfo(uint action, uint param, out int value, uint winIni);

    readonly ItemsControl list;
    readonly Func<object, bool> canDrag;
    readonly Func<object, object, bool, Task> move;

    Control? source; // container of the pressed item
    Point start, grab;
    bool dragging;
    Border? preview;
    Rectangle? placeholder;
    int slot = -1; // gap before the container with this index; Count means after the last one
    TopLevel? top;

    public ReorderDrag(ItemsControl list, Func<object, bool> canDrag, Func<object, object, bool, Task> move)
    {
        (this.list, this.canDrag, this.move) = (list, canDrag, move);
        // Handled events too: a ListBox marks the press as handled when it selects the item.
        list.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Bubble, handledEventsToo: true);
        list.AddHandler(InputElement.PointerMovedEvent, OnMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        list.AddHandler(InputElement.PointerReleasedEvent, OnReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        list.AddHandler(InputElement.PointerCaptureLostEvent, (_, _) => End(), RoutingStrategies.Direct);
    }

    /// <summary>Realized containers in item order.</summary>
    List<Control> Containers() => list.GetRealizedContainers().OrderBy(list.IndexFromContainer).ToList();

    void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (dragging || !e.GetCurrentPoint(list).Properties.IsLeftButtonPressed) return;
        // Only the item's own content is a handle, not nested items such as the users inside a channel block.
        var container = (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<Control>().FirstOrDefault(c => list.IndexFromContainer(c) >= 0);
        var item = container is null ? null : list.ItemFromContainer(container);
        if (item is null || (e.Source as StyledElement)?.DataContext != item || !canDrag(item)) return;
        source = container;
        start = e.GetPosition(list);
        grab = e.GetPosition(container);
    }

    void OnMoved(object? sender, PointerEventArgs e)
    {
        if (source is null) return;
        var position = e.GetPosition(list);
        if (!dragging)
        {
            if (Math.Abs(position.Y - start.Y) < Threshold && Math.Abs(position.X - start.X) < Threshold) return;
            if (!Begin(e)) return;
        }
        if (preview is not null && preview.Parent is Visual overlay)
        {
            var at = e.GetPosition(overlay) - grab;
            Canvas.SetLeft(preview, at.X);
            Canvas.SetTop(preview, at.Y);
        }
        ShowGap(SlotAt(position.Y));
    }

    bool Begin(PointerEventArgs e)
    {
        if (source is null || OverlayLayer.GetOverlayLayer(list) is not { } overlay) return false;
        dragging = true;
        bool reduced = ReducedMotion();
        var transforms = new TransformGroup();
        transforms.Children.Add(new ScaleTransform(Scale, Scale));
        transforms.Children.Add(new RotateTransform(Tilt));
        preview = new Border
        {
            Classes = { "dragPreview" },
            Width = source.Bounds.Width,
            Height = source.Bounds.Height,
            IsHitTestVisible = false,
            RenderTransform = transforms,
            Child = new Image { Source = Snapshot(source) },
        };
        // the drop target: a dashed frame of the item's size where it will land, below the lifted preview
        placeholder = new Rectangle
        {
            Classes = { "dropPlaceholder" },
            Width = source.Bounds.Width,
            Height = source.Bounds.Height,
            IsHitTestVisible = false,
            IsVisible = false,
        };
        overlay.Children.Add(placeholder);
        overlay.Children.Add(preview);
        source.Opacity = DimmedOpacity;
        foreach (var container in Containers())
            container.Transitions = reduced ? null : [new ThicknessTransition { Property = Avalonia.Layout.Layoutable.MarginProperty, Duration = TimeSpan.FromMilliseconds(120) }];
        e.Pointer.Capture(list);
        top = TopLevel.GetTopLevel(list);
        top?.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        return true;
    }

    static RenderTargetBitmap Snapshot(Control control)
    {
        double scaling = TopLevel.GetTopLevel(control)?.RenderScaling ?? 1;
        var size = new PixelSize(Math.Max(1, (int)Math.Ceiling(control.Bounds.Width * scaling)), Math.Max(1, (int)Math.Ceiling(control.Bounds.Height * scaling)));
        var bitmap = new RenderTargetBitmap(size, new Vector(96 * scaling, 96 * scaling));
        // Subpixel text on a transparent bitmap gets dark fringes; grayscale antialiasing stays clean on any background.
        var mode = RenderOptions.GetTextRenderingMode(control);
        RenderOptions.SetTextRenderingMode(control, TextRenderingMode.Antialias);
        bitmap.Render(control);
        RenderOptions.SetTextRenderingMode(control, mode);
        return bitmap;
    }

    /// <summary>
    /// Index of the gap from the items' natural positions (as if there were no gap): before the first item whose
    /// middle lies below the pointer. The gap next to the dragged item itself means "no change" and is not shown.
    /// </summary>
    int SlotAt(double y)
    {
        var containers = Containers();
        double top = list.ItemsPanelRoot?.TranslatePoint(default, list)?.Y ?? 0;
        int index = containers.Count;
        for (int i = 0; i < containers.Count; i++)
        {
            double height = containers[i].Bounds.Height;
            if (y < top + height / 2)
            {
                index = i;
                break;
            }
            top += height;
        }
        int own = containers.IndexOf(source!);
        return index == own || index == own + 1 ? -1 : index;
    }

    void ShowGap(int newSlot)
    {
        var containers = Containers();
        double height = source?.Bounds.Height ?? 0;
        slot = newSlot;
        // the gap opens above the target item, or below the last one when dropping at the end
        for (int i = 0; i < containers.Count; i++)
            containers[i].Margin = i == slot ? new Thickness(0, height, 0, 0)
                : i == containers.Count - 1 && slot == containers.Count ? new Thickness(0, 0, 0, height)
                : default;
        PlacePlaceholder(containers, height);
    }

    /// <summary>The dashed frame sits exactly in the gap, at the items' natural positions (where the gap ends up).</summary>
    void PlacePlaceholder(List<Control> containers, double height)
    {
        if (placeholder is null || placeholder.Parent is not Visual overlay) return;
        if (slot < 0)
        {
            placeholder.IsVisible = false;
            return;
        }
        var panelTop = list.ItemsPanelRoot?.TranslatePoint(default, list) ?? default;
        double y = panelTop.Y + containers.Take(slot).Sum(c => c.Bounds.Height);
        if (list.TranslatePoint(new Point(panelTop.X, y), overlay) is not { } at) return;
        Canvas.SetLeft(placeholder, at.X);
        Canvas.SetTop(placeholder, at.Y);
        placeholder.IsVisible = true;
    }

    async void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        var (wasDragging, dropped, containers, gap) = (dragging, source, Containers(), slot);
        bool inside = new Rect(list.Bounds.Size).Contains(e.GetPosition(list));
        End();
        if (!wasDragging) return;
        e.Handled = true;
        if (!inside || gap < 0 || dropped is null || list.ItemFromContainer(dropped) is not { } item) return;
        bool after = gap == containers.Count;
        var target = list.ItemFromContainer(containers[after ? gap - 1 : gap]);
        if (target is not null) await move(item, target, after);
    }

    void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !dragging) return;
        e.Handled = true;
        End();
    }

    /// <summary>Removes preview and gap and restores the item, whether dropped or cancelled.</summary>
    void End()
    {
        if (dragging)
        {
            foreach (var container in Containers())
            {
                container.Transitions = null;
                container.Margin = default;
            }
            if (source is not null) source.Opacity = 1;
            if (preview?.Parent is OverlayLayer overlay) overlay.Children.Remove(preview);
            if (placeholder?.Parent is OverlayLayer layer) layer.Children.Remove(placeholder);
            top?.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        }
        (source, preview, placeholder, top, slot, dragging) = (null, null, null, null, -1, false);
    }
}
