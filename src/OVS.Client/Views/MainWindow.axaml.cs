using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using OVS.Client.Settings;
using OVS.Client.ViewModels;

namespace OVS.Client.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown);
    }

    MainViewModel Vm => (MainViewModel)DataContext!;

    /// <summary>The modal layer for all dialogs (A20: no second window).</summary>
    public OverlayHost Overlay => OverlayLayer;

    async void OnConnectClick(object? sender, RoutedEventArgs e) => await ConnectAsync(null);

    async Task ConnectAsync(Bookmark? preselect)
    {
        if (Vm.IsConnecting) return;
        if (await SimpleDialogs.Connect(Overlay, Vm.Settings, preselect) is { } choice) await Vm.ConnectAsync(choice);
    }

    // ---- Bookmarks in the sidebar (Package 40): a click connects right away ----

    static Bookmark? BookmarkOf(object? sender) => ((sender as Control)?.DataContext as BookmarkItem)?.Bookmark;

    async void OnBookmarkClick(object? sender, RoutedEventArgs e)
    {
        if (!Vm.IsConnecting && BookmarkOf(sender) is { } bookmark) await Vm.ConnectBookmarkAsync(bookmark);
    }

    async void OnBookmarkEdit(object? sender, RoutedEventArgs e)
    {
        if (BookmarkOf(sender) is { } bookmark) await Vm.EditBookmarkAsync(bookmark);
    }

    async void OnBookmarkDelete(object? sender, RoutedEventArgs e)
    {
        if (BookmarkOf(sender) is { } bookmark) await Vm.DeleteBookmarkAsync(bookmark);
    }

    /// <summary>Esc leaves settings (without saving) or the administration. An open dialog handles Esc itself first.</summary>
    void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.Escape || Overlay.IsOpen || Vm.IsHomePage) return;
        Vm.ClosePage();
        e.Handled = true;
    }

    void OnChannelDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ChannelViewModel channel) channel.JoinCommand.Execute(null);
    }

    // ---- Package 36: drag a channel onto another to reorder (needs "Channels bearbeiten") ----

    const double DragThreshold = 6;
    ChannelViewModel? dragSource, dropTarget;
    Point dragStart;
    bool dragging, dropAfter;

    void OnChannelPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not ChannelViewModel { CanEdit: true } channel) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        dragSource = channel;
        dragStart = e.GetPosition(ChannelItems);
    }

    void OnChannelPointerMoved(object? sender, PointerEventArgs e)
    {
        if (dragSource is null) return;
        var position = e.GetPosition(ChannelItems);
        if (!dragging && Math.Abs(position.Y - dragStart.Y) < DragThreshold && Math.Abs(position.X - dragStart.X) < DragThreshold) return;
        dragging = true;
        // Each channel block is its row plus its users; the upper half means "before", the lower half "after".
        var blocks = ChannelItems.GetRealizedContainers()
            .Select(c => (Channel: c.DataContext as ChannelViewModel, Top: c.TranslatePoint(default, ChannelItems)?.Y ?? 0, c.Bounds.Height))
            .Where(b => b.Channel is not null).ToList();
        if (blocks.Count == 0) return;
        var hit = blocks.FirstOrDefault(b => position.Y < b.Top + b.Height);
        if (hit.Channel is null) hit = blocks[^1];
        ShowDrop(hit.Channel, position.Y > hit.Top + hit.Height / 2);
    }

    void ShowDrop(ChannelViewModel? target, bool after)
    {
        if (dropTarget is not null) dropTarget.IsDropAbove = dropTarget.IsDropBelow = false;
        dropTarget = target == dragSource ? null : target;
        dropAfter = after;
        if (dropTarget is null) return;
        dropTarget.IsDropAbove = !after;
        dropTarget.IsDropBelow = after;
    }

    async void OnChannelPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var (source, target, after, wasDragging) = (dragSource, dropTarget, dropAfter, dragging);
        EndDrag();
        if (!wasDragging) return;
        e.Handled = true;
        if (source is not null && target is not null && Vm.Server is { } server) await server.MoveChannelAsync(source, target, after);
    }

    void OnChannelPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => EndDrag();

    void EndDrag()
    {
        ShowDrop(null, false);
        dragSource = null;
        dragging = false;
    }
}
