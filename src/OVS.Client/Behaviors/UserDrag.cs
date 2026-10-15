using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;
using OVS.Client.Animations;
using OVS.Client.ViewModels;

namespace OVS.Client.Behaviors;

/// <summary>
/// Package 120 (A129): a user row dragged onto a channel of the tree. It looks like a reorder drag (A112): a lifted
/// preview follows the pointer past the threshold, the row stays dimmed, a gap of its height opens below the channel it
/// would land in with the same dashed frame, and Esc cancels; the channel's row lights up as well. Only a channel the
/// user may go to opens. Releasing on it calls <c>drop(user, channel)</c>, anywhere else nothing is sent. Lives beside
/// the channel <see cref="ReorderDrag"/>, which ignores presses on user rows.
/// </summary>
internal sealed class UserDrag
{
    /// <summary>The style class of the channel a dragged user would land in.</summary>
    public const string TargetClass = "dropTarget";

    readonly ItemsControl tree;
    readonly Func<UserViewModel, ChannelViewModel, Task> drop;

    Border? source; // the pressed user row
    Point start, grab;
    bool dragging;
    Border? preview, lit;
    Rectangle? placeholder;
    Control? gapped; // the channel block the gap opened below
    TopLevel? top;

    /// <summary>Lets users in tree be dragged onto a channel; drop moves them.</summary>
    public UserDrag(ItemsControl tree, Func<UserViewModel, ChannelViewModel, Task> drop)
    {
        (this.tree, this.drop) = (tree, drop);
        tree.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Bubble, handledEventsToo: true);
        tree.AddHandler(InputElement.PointerMovedEvent, OnMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        tree.AddHandler(InputElement.PointerReleasedEvent, OnReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        tree.AddHandler(InputElement.PointerCaptureLostEvent, (_, _) => End(), RoutingStrategies.Direct);
    }

    UserViewModel? User => source?.DataContext as UserViewModel;

    void OnPressed(object? sender, PointerPressedEventArgs args)
    {
        if (dragging || !args.GetCurrentPoint(tree).Properties.IsLeftButtonPressed) return;
        var row = (args.Source as Visual)?.GetSelfAndVisualAncestors().OfType<Border>().FirstOrDefault(border => border.Classes.Contains("row"));
        if (row?.DataContext is not UserViewModel { CanDrag: true }) return; // a channel row, or a user I may not move
        source = row;
        start = args.GetPosition(tree);
        grab = args.GetPosition(row);
    }

    void OnMoved(object? sender, PointerEventArgs args)
    {
        if (source is null) return;
        // let go outside the window, or the row is gone (the user left, a sync made a new one): the drag is over
        if (!args.GetCurrentPoint(tree).Properties.IsLeftButtonPressed || source.GetVisualRoot() is null)
        {
            End();
            return;
        }
        if (!dragging)
        {
            var position = args.GetPosition(tree);
            if (Math.Abs(position.Y - start.Y) < ReorderDrag.Threshold && Math.Abs(position.X - start.X) < ReorderDrag.Threshold) return;
            if (!Begin(args)) return;
        }
        DragVisuals.Follow(preview!, args, grab);
        var target = TargetAt(args);
        Light(target);
        ShowGap(target);
    }

    bool Begin(PointerEventArgs args)
    {
        if (source is null || OverlayLayer.GetOverlayLayer(tree) is not { } overlay) return false;
        dragging = true;
        preview = DragVisuals.Lift(source);
        placeholder = DragVisuals.Placeholder(source); // below the lifted preview
        overlay.Children.Add(placeholder);
        overlay.Children.Add(preview);
        source.Opacity = ReorderDrag.DimmedOpacity;
        var transitions = DragVisuals.GapTransitions(Motion.IsReduced(tree)); // reduced: the gap opens without sliding
        foreach (var container in tree.GetRealizedContainers()) container.Transitions = transitions;
        args.Pointer.Capture(tree);
        top = TopLevel.GetTopLevel(tree);
        top?.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        return true;
    }

    /// <summary>
    /// The channel whose block (its row and its users) is under the pointer, if the user may go there. Only where the tree
    /// can be seen: the scrolled-away part of the list is no target.
    /// </summary>
    ChannelViewModel? TargetAt(PointerEventArgs args)
    {
        if (User is not { } user) return null;
        var viewer = tree.FindAncestorOfType<ScrollViewer>();
        if (viewer is not null && !new Rect(viewer.Bounds.Size).Contains(args.GetPosition(viewer))) return null;
        var at = args.GetPosition(tree);
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
            .FirstOrDefault(border => border.Classes.Contains("row") && border.DataContext == channel);
        if (row == lit) return;
        lit?.Classes.Set(TargetClass, false);
        row?.Classes.Set(TargetClass, true);
        lit = row;
    }

    /// <summary>Opens a gap of the row's height below the channel's block (where the user will appear) and puts the dashed frame in it.</summary>
    void ShowGap(ChannelViewModel? channel)
    {
        var container = channel is null ? null : tree.ContainerFromItem(channel);
        if (container != gapped)
        {
            gapped?.ClearValue(Layoutable.MarginProperty);
            if (container is not null && source is not null) container.Margin = new Thickness(0, 0, 0, source.Bounds.Height);
            gapped = container;
        }
        PlacePlaceholder();
    }

    /// <summary>The frame sits at the block's own bottom (where the gap begins), indented like a user row.</summary>
    void PlacePlaceholder()
    {
        if (placeholder is null || placeholder.Parent is not Visual overlay) return;
        if (gapped is null || source?.TranslatePoint(default, tree) is not { } row || gapped.TranslatePoint(new Point(0, gapped.Bounds.Height), tree) is not { } bottom
            || tree.TranslatePoint(new Point(row.X, bottom.Y), overlay) is not { } at)
        {
            placeholder.IsVisible = false;
            return;
        }
        Canvas.SetLeft(placeholder, at.X);
        Canvas.SetTop(placeholder, at.Y);
        placeholder.IsVisible = true;
    }

    void OnReleased(object? sender, PointerReleasedEventArgs args)
    {
        var (wasDragging, user, target) = (dragging, User, dragging ? TargetAt(args) : null);
        End();
        if (!wasDragging) return;
        args.Handled = true;
        if (user is null || target is null) return;
        // Package 103: the row already shows where it was dropped, it must not fly there again once the server confirms
        ItemFlight.Ground(user, drop(user, target));
    }

    void OnKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key != Key.Escape || !dragging) return;
        args.Handled = true;
        End();
    }

    /// <summary>Removes preview and highlight and restores the row, whether dropped or cancelled.</summary>
    void End()
    {
        if (dragging)
        {
            source?.ClearValue(Visual.OpacityProperty);
            if (preview?.Parent is OverlayLayer overlay) overlay.Children.Remove(preview);
            if (placeholder?.Parent is OverlayLayer layer) layer.Children.Remove(placeholder);
            foreach (var container in tree.GetRealizedContainers())
            {
                container.Transitions = null;
                container.ClearValue(Layoutable.MarginProperty);
            }
            Light(null);
            top?.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        }
        (source, preview, placeholder, gapped, top, dragging) = (null, null, null, null, null, false);
    }
}
