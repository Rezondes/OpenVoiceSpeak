using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using OVS.Client.Animations;
using OVS.Client.ViewModels;

namespace OVS.Client.Behaviors;

/// <summary>
/// Package 120 (A129): a user row dragged onto a channel of the tree. As in a reorder drag (A112) a lifted preview follows
/// the pointer past the threshold, the row stays dimmed and Esc cancels; but nothing makes room: the channel under the
/// pointer lights up when the user may go there. Releasing on it calls <c>drop(user, channel)</c>, anywhere else nothing
/// is sent. Lives beside the channel <see cref="ReorderDrag"/>, which ignores presses on user rows.
/// </summary>
public sealed class UserDrag
{
    public const string TargetClass = "dropTarget";

    readonly ItemsControl tree;
    readonly Func<UserViewModel, ChannelViewModel, Task> drop;

    Border? source; // the pressed user row
    Point start, grab;
    bool dragging;
    Border? preview, lit;
    TopLevel? top;

    public UserDrag(ItemsControl tree, Func<UserViewModel, ChannelViewModel, Task> drop)
    {
        (this.tree, this.drop) = (tree, drop);
        tree.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Bubble, handledEventsToo: true);
        tree.AddHandler(InputElement.PointerMovedEvent, OnMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        tree.AddHandler(InputElement.PointerReleasedEvent, OnReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        tree.AddHandler(InputElement.PointerCaptureLostEvent, (_, _) => End(), RoutingStrategies.Direct);
    }

    UserViewModel? User => source?.DataContext as UserViewModel;

    void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (dragging || !e.GetCurrentPoint(tree).Properties.IsLeftButtonPressed) return;
        var row = (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("row"));
        if (row?.DataContext is not UserViewModel { CanDrag: true }) return; // a channel row, or a user I may not move
        source = row;
        start = e.GetPosition(tree);
        grab = e.GetPosition(row);
    }

    void OnMoved(object? sender, PointerEventArgs e)
    {
        if (source is null) return;
        // let go outside the window, or the row is gone (the user left, a sync made a new one): the drag is over
        if (!e.GetCurrentPoint(tree).Properties.IsLeftButtonPressed || source.GetVisualRoot() is null)
        {
            End();
            return;
        }
        if (!dragging)
        {
            var position = e.GetPosition(tree);
            if (Math.Abs(position.Y - start.Y) < ReorderDrag.Threshold && Math.Abs(position.X - start.X) < ReorderDrag.Threshold) return;
            if (!Begin(e)) return;
        }
        ReorderDrag.Follow(preview!, e, grab);
        Light(TargetAt(e));
    }

    bool Begin(PointerEventArgs e)
    {
        if (source is null || OverlayLayer.GetOverlayLayer(tree) is not { } overlay) return false;
        dragging = true;
        preview = ReorderDrag.Lift(source);
        overlay.Children.Add(preview);
        source.Opacity = ReorderDrag.DimmedOpacity;
        e.Pointer.Capture(tree);
        top = TopLevel.GetTopLevel(tree);
        top?.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        return true;
    }

    /// <summary>
    /// The channel whose block (its row and its users) is under the pointer, if the user may go there. Only where the tree
    /// can be seen: the scrolled-away part of the list is no target.
    /// </summary>
    ChannelViewModel? TargetAt(PointerEventArgs e)
    {
        if (User is not { } user) return null;
        var viewer = tree.FindAncestorOfType<ScrollViewer>();
        if (viewer is not null && !new Rect(viewer.Bounds.Size).Contains(e.GetPosition(viewer))) return null;
        var at = e.GetPosition(tree);
        foreach (var container in tree.GetRealizedContainers())
        {
            if (container.Classes.Contains("leaving") || container.TranslatePoint(default, tree) is not { } topLeft) continue; // Package 102: folding away
            if (new Rect(topLeft, container.Bounds.Size).Contains(at) && tree.ItemFromContainer(container) is ChannelViewModel channel)
                return ServerViewModel.IsDropTarget(user, channel) ? channel : null;
        }
        return null;
    }

    /// <summary>Lights the channel's own row, not the user rows inside its block.</summary>
    void Light(ChannelViewModel? channel)
    {
        var row = channel is null ? null : tree.ContainerFromItem(channel)?.GetVisualDescendants().OfType<Border>()
            .FirstOrDefault(b => b.Classes.Contains("row") && b.DataContext == channel);
        if (row == lit) return;
        lit?.Classes.Set(TargetClass, false);
        row?.Classes.Set(TargetClass, true);
        lit = row;
    }

    void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        var (wasDragging, user, target) = (dragging, User, dragging ? TargetAt(e) : null);
        End();
        if (!wasDragging) return;
        e.Handled = true;
        if (user is null || target is null) return;
        // Package 103: the row already shows where it was dropped, it must not fly there again once the server confirms
        ItemMotion.Ground(user, drop(user, target));
    }

    void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !dragging) return;
        e.Handled = true;
        End();
    }

    /// <summary>Removes preview and highlight and restores the row, whether dropped or cancelled.</summary>
    void End()
    {
        if (dragging)
        {
            source?.ClearValue(Visual.OpacityProperty);
            if (preview?.Parent is OverlayLayer overlay) overlay.Children.Remove(preview);
            Light(null);
            top?.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        }
        (source, preview, top, dragging) = (null, null, null, false);
    }
}
