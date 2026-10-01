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
        // Package 36, 96: drag a channel to a new position (needs "Channels bearbeiten")
        _ = new ReorderDrag(ChannelItems, item => item is ChannelViewModel { CanEdit: true },
            (source, target, after) => Vm.Server?.MoveChannelAsync((ChannelViewModel)source, (ChannelViewModel)target, after) ?? Task.CompletedTask);
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
        if ((sender as Control)?.DataContext is not ChannelViewModel { IsSeparator: false } channel) return; // Package 111: nothing at all
        channel.JoinCommand.Execute(null);
        SetDrawer(false);
    }

    // ---- Package 67 (A81): sidebar as wide as the widest channel row, until the user drags it ----

    MainViewModel? watchedVm;
    ServerViewModel? watchedServer;
    bool sidebarDragged;

    void WatchServer()
    {
        if (watchedVm is not null)
        {
            watchedVm.PropertyChanged -= OnVmPropertyChanged;
            watchedVm.PropertyChanging -= OnVmPropertyChanging;
        }
        watchedVm = DataContext as MainViewModel;
        if (watchedVm is not null)
        {
            watchedVm.PropertyChanged += OnVmPropertyChanged;
            watchedVm.PropertyChanging += OnVmPropertyChanging;
        }
        OnServerChanged();
        ApplyDisplay();
    }

    /// <summary>Package 99: the chosen display, also while it is only previewed on the settings page.</summary>
    void ApplyDisplay()
    {
        if (watchedVm is null) return;
        Motion.Apply(this, watchedVm.Appearance.Display);
        // Package 102: what is gone stays a moment to fold away, only in the animated display
        watchedVm.Leave.Delay = Motion.Duration(Motion.Normal);
        if (watchedVm.Server is { } server) server.Leave.Delay = Motion.Duration(Motion.Normal);
    }

    /// <summary>
    /// Package 104: before the page or its model changes (closing clears the model first), a picture of the page as it
    /// is now covers the page area; the switch plays once the change is complete.
    /// </summary>
    void OnVmPropertyChanging(object? sender, System.ComponentModel.PropertyChangingEventArgs e)
    {
        if (watchedVm is not { } vm) return;
        if (e.PropertyName == nameof(MainViewModel.Server))
        {
            // Package 106: connecting, home fades away while the chat comes up from below; disconnecting, the server folds
            // down and home comes back. The channel tree does the same in the sidebar.
            bool was = vm.Server is not null;
            (Vector In, Vector Away)? Way() => (vm.Server is not null) == was ? null
                : vm.Server is not null ? (new Vector(0, PageMotion.Slide), default) : (default, new Vector(0, PageMotion.Slide));
            if (vm.IsHomePage) PageMotion.Leave(PageHost, PageGhost, Way); // settings and administration stay what they are
            PageMotion.Leave(TreeHost, TreeGhost, Way);
            return;
        }
        if (e.PropertyName is not (nameof(MainViewModel.Page) or nameof(MainViewModel.SettingsPage) or nameof(MainViewModel.AdminPage))) return;
        var before = vm.Page;
        PageMotion.Leave(PageHost, PageGhost, () => vm.Page == before ? null : PageMotion.Sideways(!vm.IsHomePage));
    }

    bool wasConnecting;

    /// <summary>
    /// Package 106: connected, the server's name fades into the header; a connect that failed shakes the start card once
    /// and lets the reason fade in.
    /// </summary>
    void OnConnectionChanged(MainViewModel vm, string property)
    {
        if (property == nameof(MainViewModel.Server) && vm.Server is not null) FadeIn(ServerHeader);
        if (property != nameof(MainViewModel.IsConnecting)) return;
        if (wasConnecting && !vm.IsConnecting && vm.Server is null)
        {
            Motion.Shake(StartCard);
            FadeIn(StatusText);
        }
        wasConnecting = vm.IsConnecting;
    }

    static void FadeIn(Visual target)
    {
        if (!Motion.IsAnimated) return;
        _ = new Avalonia.Animation.Animation
        {
            Duration = Motion.Normal,
            Easing = Motion.Ease,
            FillMode = Avalonia.Animation.FillMode.Backward,
            Children =
            {
                new Avalonia.Animation.KeyFrame { Cue = new Avalonia.Animation.Cue(0), Setters = { new Avalonia.Styling.Setter(OpacityProperty, 0d) } },
                new Avalonia.Animation.KeyFrame { Cue = new Avalonia.Animation.Cue(1), Setters = { new Avalonia.Styling.Setter(OpacityProperty, 1d) } },
            },
        }.Play(target);
    }

    void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (watchedVm is { } changed && e.PropertyName is { } name) OnConnectionChanged(changed, name);
        if (e.PropertyName == nameof(MainViewModel.Server))
        {
            OnServerChanged();
            ApplyDisplay();
        }
        else if (e.PropertyName == nameof(MainViewModel.Appearance)) ApplyDisplay();
        else if (e.PropertyName == nameof(MainViewModel.Page) && watchedVm?.IsHomePage == false) SetDrawer(false);
    }

    void OnServerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ServerViewModel.CurrentChannel)) return;
        SetDrawer(false);
        // Package 103: the highlight slides from the channel I left to the one I am in now
        var now = watchedServer?.CurrentChannel;
        if (currentChannel is { } was && now is not null && was != now && ChannelRow(was) is { } from && ChannelRow(now) is { } to)
            _ = FlyGhost.Highlight(from, to);
        currentChannel = now;
    }

    ChannelViewModel? currentChannel;

    Border? ChannelRow(ChannelViewModel channel) =>
        ChannelItems.ContainerFromItem(channel)?.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("row"));

    /// <summary>Every connect starts with the fitted width again.</summary>
    void OnServerChanged()
    {
        if (watchedServer is not null)
        {
            watchedServer.StateChanged -= QueueFitSidebar;
            watchedServer.PropertyChanged -= OnServerPropertyChanged;
        }
        watchedServer = watchedVm?.Server;
        currentChannel = watchedServer?.CurrentChannel; // a new connection starts where it is, nothing slides
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
        var rows = ChannelItems.GetRealizedContainers().Where(c => c.DataContext is not ChannelViewModel { IsSeparator: true }).Select(c =>
        {
            var texts = c.GetVisualDescendants().OfType<TextBlock>().ToList();
            var name = texts.FirstOrDefault(t => t.Classes.Contains("channelName"));
            var count = texts.FirstOrDefault(t => t.Classes.Contains("channelCount"));
            var channel = c.DataContext as ChannelViewModel;
            int icons = new[] { channel?.IsDefault, channel?.IsLinked, channel?.IsLocked }.Count(on => on == true); // Package 93: the lock too
            return new SidebarRow(TextWidth(name), icons, TextWidth(count));
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
        bool was = drawerOpen;
        drawerOpen = open && Classes.Contains("compact");
        if (drawerOpen == was || !Motion.IsAnimated || !Classes.Contains("compact"))
        {
            drawerCancel?.Cancel();
            StillDrawer();
            Classes.Set("drawer", drawerOpen);
            return;
        }
        _ = SlideDrawer(drawerOpen);
    }

    CancellationTokenSource? drawerCancel;

    /// <summary>
    /// Package 105: the drawer slides in from the left with its scrim; closing slides it out. Closed counts at once
    /// (keys, clicks); the sidebar only stays visible until it has slid away.
    /// </summary>
    async Task SlideDrawer(bool open)
    {
        drawerCancel?.Cancel();
        var cancel = drawerCancel = new CancellationTokenSource();
        if (open) Classes.Set("drawer", true);
        Sidebar.IsHitTestVisible = open;
        UpdateLayout();
        double width = Sidebar.Bounds.Width;
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
        }.Play(Sidebar, cancel.Token);
        var dim = new Avalonia.Animation.Animation
        {
            Duration = Motion.Normal,
            Easing = Motion.Ease,
            FillMode = open ? Avalonia.Animation.FillMode.Backward : Avalonia.Animation.FillMode.Forward,
            Children =
            {
                new Avalonia.Animation.KeyFrame { Cue = new Avalonia.Animation.Cue(0), Setters = { new Avalonia.Styling.Setter(OpacityProperty, open ? 0d : 1d) } },
                new Avalonia.Animation.KeyFrame { Cue = new Avalonia.Animation.Cue(1), Setters = { new Avalonia.Styling.Setter(OpacityProperty, open ? 1d : 0d) } },
            },
        }.Play(DrawerScrim, cancel.Token);
        await Task.WhenAll(slide, dim);
        if (cancel.IsCancellationRequested) return;
        if (!open) Classes.Set("drawer", false);
        StillDrawer();
    }

    /// <summary>
    /// The drawer as it rests: a finished slide-out keeps its last values (moved away, faded), so they go once it is
    /// hidden; the next slide starts from its place.
    /// </summary>
    void StillDrawer()
    {
        Sidebar.ClearValue(IsHitTestVisibleProperty);
        Sidebar.ClearValue(RenderTransformProperty);
        DrawerScrim.ClearValue(OpacityProperty);
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
        if (!compact && (drawerOpen || Classes.Contains("drawer"))) SetDrawer(false); // also while it still slides away
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
