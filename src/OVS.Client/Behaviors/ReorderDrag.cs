using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using OVS.Client.Animations;

namespace OVS.Client.Behaviors;

/// <summary>
/// Package 96 (A112): the one reorder drag for every list (channel tree, group list). After a 6 px move a lifted
/// preview of the item follows the pointer in the overlay layer (scale 1.03, shadow, 3 degrees tilt), the item stays
/// dimmed in its slot and a gap of its height opens where it will land. Releasing inside the list calls
/// <c>move(source, target, after)</c>; Esc or releasing outside the list cancels.
/// </summary>
public sealed class ReorderDrag
{
    /// <summary>The distance before a drag starts, the dragged row's scale and tilt, and the opacity of its place.</summary>
    public const double Threshold = 6, Scale = 1.03, Tilt = 3, DimmedOpacity = 0.4;

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

    /// <summary>Lets the rows of list be reordered by dragging; move gets the dragged item, the target and whether it goes after it.</summary>
    public ReorderDrag(ItemsControl list, Func<object, bool> canDrag, Func<object, object, bool, Task> move)
    {
        (this.list, this.canDrag, this.move) = (list, canDrag, move);
        // Handled events too: a ListBox marks the press as handled when it selects the item.
        list.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Bubble, handledEventsToo: true);
        list.AddHandler(InputElement.PointerMovedEvent, OnMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        list.AddHandler(InputElement.PointerReleasedEvent, OnReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        list.AddHandler(InputElement.PointerCaptureLostEvent, (_, _) => End(), RoutingStrategies.Direct);
    }

    /// <summary>Realized containers in item order; a row folding away (Package 102) is no place to drop at.</summary>
    List<Control> Containers() => list.GetRealizedContainers().Where(control => !control.Classes.Contains("leaving")).OrderBy(list.IndexFromContainer).ToList();

    void OnPressed(object? sender, PointerPressedEventArgs args)
    {
        if (dragging || !args.GetCurrentPoint(list).Properties.IsLeftButtonPressed) return;
        // Only the item's own content is a handle, not nested items such as the users inside a channel block.
        var container = (args.Source as Visual)?.GetSelfAndVisualAncestors().OfType<Control>().FirstOrDefault(control => list.IndexFromContainer(control) >= 0);
        var item = container is null ? null : list.ItemFromContainer(container);
        if (item is null || container!.Classes.Contains("leaving") || (args.Source as StyledElement)?.DataContext != item || !canDrag(item)) return;
        source = container;
        start = args.GetPosition(list);
        grab = args.GetPosition(container);
    }

    void OnMoved(object? sender, PointerEventArgs args)
    {
        if (source is null) return;
        var position = args.GetPosition(list);
        if (!dragging)
        {
            if (Math.Abs(position.Y - start.Y) < Threshold && Math.Abs(position.X - start.X) < Threshold) return;
            if (!Begin(args)) return;
        }
        if (preview is not null) DragVisuals.Follow(preview, args, grab);
        ShowGap(SlotAt(position.Y));
    }

    bool Begin(PointerEventArgs args)
    {
        if (source is null || OverlayLayer.GetOverlayLayer(list) is not { } overlay) return false;
        dragging = true;
        bool reduced = Motion.IsReduced(list); // reduced: the gap opens without sliding
        preview = DragVisuals.Lift(source);
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
        args.Pointer.Capture(list);
        top = TopLevel.GetTopLevel(list);
        top?.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        return true;
    }

    /// <summary>
    /// Index of the gap from the items' natural positions (as if there were no gap): before the first item whose
    /// middle lies below the pointer. The gap next to the dragged item itself means "no change" and is not shown.
    /// </summary>
    int SlotAt(double pointerY)
    {
        var containers = Containers();
        double top = list.ItemsPanelRoot?.TranslatePoint(default, list)?.Y ?? 0;
        int index = containers.Count;
        for (int position = 0; position < containers.Count; position++)
        {
            double height = containers[position].Bounds.Height;
            if (pointerY < top + height / 2)
            {
                index = position;
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
        for (int index = 0; index < containers.Count; index++)
            containers[index].Margin = index == slot ? new Thickness(0, height, 0, 0)
                : index == containers.Count - 1 && slot == containers.Count ? new Thickness(0, 0, 0, height)
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
        double gapTop = panelTop.Y + containers.Take(slot).Sum(control => control.Bounds.Height);
        if (list.TranslatePoint(new Point(panelTop.X, gapTop), overlay) is not { } at) return;
        Canvas.SetLeft(placeholder, at.X);
        Canvas.SetTop(placeholder, at.Y);
        placeholder.IsVisible = true;
    }

    async void OnReleased(object? sender, PointerReleasedEventArgs args)
    {
        var (wasDragging, dropped, containers, gap) = (dragging, source, Containers(), slot);
        bool inside = new Rect(list.Bounds.Size).Contains(args.GetPosition(list));
        End();
        if (!wasDragging) return;
        args.Handled = true;
        if (!inside || gap < 0 || dropped is null || list.ItemFromContainer(dropped) is not { } item) return;
        bool after = gap == containers.Count;
        var target = list.ItemFromContainer(containers[after ? gap - 1 : gap]);
        if (target is null) return;
        ItemMotion.Hold(list); // Package 103: the row already sits where it was dropped, it must not glide there again
        await move(item, target, after);
    }

    void OnKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key != Key.Escape || !dragging) return;
        args.Handled = true;
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
