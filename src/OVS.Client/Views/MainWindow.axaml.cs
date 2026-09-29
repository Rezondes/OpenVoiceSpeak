using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OVS.Client.Settings;
using OVS.Client.ViewModels;

namespace OVS.Client.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown);
        DataContextChanged += (_, _) => WatchServer();
        LayoutUpdated += (_, _) => ApplyResponsive();
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

    /// <summary>Esc closes the drawer, otherwise leaves settings (without saving) or the administration. An open dialog handles Esc itself first.</summary>
    void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.Escape || Overlay.IsOpen) return;
        if (drawerOpen) SetDrawer(false);
        else if (!Vm.IsHomePage) Vm.ClosePage();
        else return;
        e.Handled = true;
    }

    void OnChannelDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not ChannelViewModel channel) return;
        channel.JoinCommand.Execute(null);
        SetDrawer(false);
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

    // ---- Package 67 (A81): sidebar as wide as the widest channel row, until the user drags it ----

    MainViewModel? watchedVm;
    ServerViewModel? watchedServer;
    bool sidebarDragged;

    void WatchServer()
    {
        if (watchedVm is not null) watchedVm.PropertyChanged -= OnVmPropertyChanged;
        watchedVm = DataContext as MainViewModel;
        if (watchedVm is not null) watchedVm.PropertyChanged += OnVmPropertyChanged;
        OnServerChanged();
    }

    void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Server)) OnServerChanged();
        else if (e.PropertyName == nameof(MainViewModel.Page) && watchedVm?.IsHomePage == false) SetDrawer(false);
    }

    void OnServerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ServerViewModel.CurrentChannel)) SetDrawer(false);
    }

    /// <summary>Every connect starts with the fitted width again.</summary>
    void OnServerChanged()
    {
        if (watchedServer is not null)
        {
            watchedServer.StateChanged -= QueueFitSidebar;
            watchedServer.PropertyChanged -= OnServerPropertyChanged;
        }
        watchedServer = watchedVm?.Server;
        if (watchedServer is null) return;
        watchedServer.StateChanged += QueueFitSidebar;
        watchedServer.PropertyChanged += OnServerPropertyChanged;
        sidebarDragged = false;
        QueueFitSidebar();
    }

    // After layout, so the rows of new or renamed channels exist and carry their styles.
    void QueueFitSidebar() => Dispatcher.UIThread.Post(FitSidebar, DispatcherPriority.Background);

    void OnSidebarDragCompleted(object? sender, VectorEventArgs e) => sidebarDragged = true;

    void FitSidebar()
    {
        if (sidebarDragged || watchedServer is null || !IsVisible) return;
        var rows = ChannelItems.GetRealizedContainers().Select(c =>
        {
            var texts = c.GetVisualDescendants().OfType<TextBlock>().ToList();
            var name = texts.FirstOrDefault(t => t.Classes.Contains("channelName"));
            var count = texts.FirstOrDefault(t => t.Classes.Contains("channelCount"));
            var channel = c.DataContext as ChannelViewModel;
            return new SidebarRow(TextWidth(name), (channel?.IsDefault == true ? 1 : 0) + (channel?.IsLinked == true ? 1 : 0), TextWidth(count));
        }).ToList();
        if (rows.Count == 0) return;
        sidebarFitted = sidebarWanted = SidebarWidth.For(rows);
        ApplyResponsive();
    }

    /// <summary>The untrimmed width of a text block's text in its current font.</summary>
    static double TextWidth(TextBlock? text) => string.IsNullOrEmpty(text?.Text) ? 0
        : new TextLayout(text.Text, new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize, null)
            .WidthIncludingTrailingWhitespace;

    // ---- Package 68 (A82, A93, A94): width steps, upper bound of the sidebar, drawer below 700 px ----

    /// <summary>The width the sidebar would like (fitted or dragged) and its fitted minimum; both yield to the header.</summary>
    double sidebarWanted = 300, sidebarFitted, sidebarSet = 300;
    /// <summary>Header width without the title, remembered while the header is hidden (settings, administration).</summary>
    double headerNeed;
    bool drawerOpen;

    void OnDrawerToggle(object? sender, RoutedEventArgs e) => SetDrawer(!drawerOpen);

    void OnDrawerScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        SetDrawer(false);
        e.Handled = true;
    }

    void SetDrawer(bool open)
    {
        drawerOpen = open && Classes.Contains("compact");
        Classes.Set("drawer", drawerOpen);
    }

    /// <summary>
    /// Runs after every layout pass and only changes what differs, so it settles after one more pass:
    /// the narrow step changes the header, which changes the bound of the sidebar.
    /// </summary>
    void ApplyResponsive()
    {
        var width = Shell.Bounds.Width;
        if (width <= 0) return;
        var compact = Responsive.IsCompact(width);
        if (ChannelHeader.IsEffectivelyVisible) headerNeed = HeaderNeed();

        var column = Shell.ColumnDefinitions[0];
        if (column.Width.Value != sidebarSet) sidebarWanted = column.Width.Value; // the splitter moved it
        var max = Responsive.SidebarMax(width, SidebarSplitter.Width, headerNeed);
        var min = compact ? 0 : Math.Min(sidebarFitted, max);
        sidebarSet = compact ? 0 : Math.Clamp(sidebarWanted, min, max);
        if (column.MinWidth != min) column.MinWidth = min;
        if (column.MaxWidth != max) column.MaxWidth = max;
        if (column.Width.Value != sidebarSet) column.Width = new GridLength(sidebarSet);
        var drawer = compact ? Math.Min(sidebarWanted, width - Responsive.DrawerGap) : double.NaN;
        if (!Sidebar.Width.Equals(drawer)) Sidebar.Width = drawer;

        Responsive.Apply(this, width, compact ? width : width - SidebarSplitter.Width - sidebarSet);
        if (!compact && drawerOpen) SetDrawer(false);
    }

    /// <summary>
    /// A82: padding, voice icon and the visible buttons with their texts; the channel title may shrink to nothing.
    /// Texts hidden under narrow count too, so dragging the sidebar never forces the header into icons only.
    /// </summary>
    double HeaderNeed()
    {
        var actions = HeaderActions.Children.Where(c => c.IsVisible).ToList();
        var hiddenTexts = actions.SelectMany(a => a.GetLogicalDescendants().OfType<TextBlock>())
            .Where(t => !t.IsVisible && t.Classes.Contains("wideOnly"))
            .Sum(t => TextWidth(t) + ((t.Parent as StackPanel)?.Spacing ?? 0));
        return ChannelHeader.Padding.Left + ChannelHeader.Padding.Right + VoiceIcon.DesiredSize.Width
            + actions.Sum(c => c.DesiredSize.Width) + hiddenTexts + HeaderActions.Spacing * Math.Max(0, actions.Count - 1);
    }
}
