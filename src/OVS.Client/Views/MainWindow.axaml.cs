using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OVS.Client.Animations;
using OVS.Client.Behaviors;
using OVS.Client.Controls;
using OVS.Client.Dialogs;
using OVS.Client.Layout;
using OVS.Client.Models;
using OVS.Client.Services;
using OVS.Client.Settings;
using OVS.Client.ViewModels;

namespace OVS.Client.Views;

/// <summary>The client's one window: every page and dialog shows inside it.</summary>
#pragma warning disable CA1001 // the window disposes its notifier when it closes (OnClosed)
internal partial class MainWindow : Window
#pragma warning restore CA1001
{
    // Package 161: the chat's text at 100 % (the defaults in Controls.axaml)
    const double ChatFontSize = 14;
    const double ChatLineHeight = 21;

    /// <summary>The window; its view model is set by App.</summary>
    public MainWindow()
    {
        InitializeComponent();
        drawer = new SidebarDrawer(this, Sidebar, DrawerScrim);
        AddHandler(KeyDownEvent, OnKeyDown);
        Closing += OnClosing;
        DataContextChanged += (_, _) => WatchServer();
        LayoutUpdated += (_, _) => ApplyResponsive();
        // Package 36, 96: drag a channel to a new position (needs "Channels bearbeiten")
        _ = new ReorderDrag(ChannelItems, item => item is ChannelViewModel { CanEdit: true },
            (source, target, after) => Vm.Server?.Tree.MoveChannelAsync((ChannelViewModel)source, (ChannelViewModel)target, after) ?? Task.CompletedTask);
        // Package 120 (A129): drag a user onto a channel to move them there, my own row to join it
        _ = new UserDrag(ChannelItems, (user, channel) => Vm.Server?.DropUserAsync(user, channel) ?? Task.CompletedTask);
    }

    MainViewModel Vm => (MainViewModel)DataContext!;

    /// <summary>Package 162: the desktop notifications, only on Windows with a real window handle.</summary>
    DesktopNotifier? notifier;

    /// <inheritdoc />
    protected override void OnOpened(EventArgs args)
    {
        base.OnOpened(args);
        if (!OperatingSystem.IsWindows() || TryGetPlatformHandle()?.Handle is not { } handle || handle == 0) return;
        notifier = new DesktopNotifier(handle);
        notifier.Clicked += OnNoticeClicked;
        Win32Properties.AddWndProcHookCallback(this, OnWindowMessage);
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs args)
    {
        notifier?.Dispose();
        base.OnClosed(args);
    }

    nint OnWindowMessage(nint hwnd, uint message, nint wParam, nint lParam, ref bool handled)
    {
        handled = notifier?.OnWindowMessage(message, lParam) == true;
        return 0;
    }

    /// <summary>Package 162: a click on a notification brings the window to the front with its chat tab.</summary>
    void OnNoticeClicked(DesktopNotice notice)
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Vm.OpenNotified(notice);
    }

    void OnNoticeRequested(DesktopNotice notice) => notifier?.Show(notice);

    /// <summary>The sidebar as a drawer below the compact width (Package 68, 105).</summary>
    readonly SidebarDrawer drawer;

    bool closeConfirmed, closeAsking;

    /// <summary>Package 114: closing the window with unsaved changes asks first; "Weiter bearbeiten" keeps it open.</summary>
    async void OnClosing(object? sender, WindowClosingEventArgs args)
    {
        if (closeConfirmed || DataContext is not MainViewModel viewModel || !viewModel.PageFlow.HasUnsavedChanges()) return;
        args.Cancel = true;
        if (closeAsking) return; // closed again while asking: the open question answers it
        closeAsking = true;
        try
        {
            if (!await viewModel.PageFlow.ConfirmLeaveAsync()) return;
        }
        finally
        {
            closeAsking = false;
        }
        closeConfirmed = true;
        Close();
    }

    /// <summary>The modal layer for all dialogs (A20: no second window).</summary>
    public OverlayHost Overlay => OverlayLayer;

    async void OnConnectClick(object? sender, RoutedEventArgs args) => await ConnectAsync(null);

    async Task ConnectAsync(Bookmark? preselect)
    {
        if (Vm.Connection.IsConnecting) return;
        if (await ConnectDialog.ShowAsync(Overlay, Vm.Bookmarks.Saved, preselect) is { } choice) await Vm.Connection.ConnectAsync(choice);
    }

    // ---- Bookmarks in the sidebar (Package 40): a click connects right away ----

    static Bookmark? BookmarkOf(object? sender) => ((sender as Control)?.DataContext as BookmarkItem)?.Bookmark;

    async void OnBookmarkClick(object? sender, RoutedEventArgs args)
    {
        if (!Vm.Connection.IsConnecting && BookmarkOf(sender) is { } bookmark) await Vm.Connection.ConnectBookmarkAsync(bookmark);
    }

    async void OnBookmarkEdit(object? sender, RoutedEventArgs args)
    {
        if (BookmarkOf(sender) is { } bookmark) await Vm.Bookmarks.EditAsync(bookmark);
    }

    async void OnBookmarkDelete(object? sender, RoutedEventArgs args)
    {
        if (BookmarkOf(sender) is { } bookmark) await Vm.Bookmarks.DeleteAsync(bookmark);
    }

    /// <summary>Esc closes the drawer, otherwise leaves settings (without saving) or the administration. An open dialog handles Esc itself first.</summary>
    void OnKeyDown(object? sender, KeyEventArgs args)
    {
        if (!args.Handled && args.Key == Key.K && args.KeyModifiers == KeyModifiers.Control && !Overlay.IsOpen)
        {
            _ = OpenQuickSwitcherAsync(); // Package 163
            args.Handled = true;
            return;
        }
        if (args.Handled || args.Key != Key.Escape || Overlay.IsOpen) return;
        if (drawer.IsOpen) drawer.Set(false);
        else if (!Vm.PageFlow.IsHomePage) _ = Vm.PageFlow.ClosePageAsync(); // Package 114: asks first when something is not saved
        else return;
        args.Handled = true;
    }

    /// <summary>Package 163: Ctrl+K, the channels of the connected server and the saved servers; Enter opens the chosen one.</summary>
    async Task OpenQuickSwitcherAsync()
    {
        var switcher = new QuickSwitcherViewModel(Vm.Server?.Tree.Channels ?? [], Vm.Bookmarks.Saved,
            bookmark => Vm.Connection.IsConnecting ? Task.CompletedTask : Vm.Connection.ConnectBookmarkAsync(bookmark));
        if (await QuickSwitcherDialog.ShowAsync(Overlay, switcher) is not { } hit) return;
        drawer.Set(false);
        await switcher.ChooseAsync(hit);
    }

    void OnChannelDoubleTapped(object? sender, TappedEventArgs args)
    {
        if ((sender as Control)?.DataContext is not ChannelViewModel { IsSeparator: false } channel) return; // Package 111: nothing at all
        channel.JoinCommand.Execute(null);
        drawer.Set(false);
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
            watchedVm.Connection.PropertyChanged -= OnConnectionPropertyChanged;
            watchedVm.PageFlow.PropertyChanging -= OnPageFlowPropertyChanging;
            watchedVm.PageFlow.PropertyChanged -= OnPageFlowPropertyChanged;
            watchedVm.NoticeRequested -= OnNoticeRequested;
        }
        watchedVm = DataContext as MainViewModel;
        if (watchedVm is not null)
        {
            watchedVm.PropertyChanged += OnVmPropertyChanged;
            watchedVm.PropertyChanging += OnVmPropertyChanging;
            watchedVm.Connection.PropertyChanged += OnConnectionPropertyChanged;
            watchedVm.PageFlow.PropertyChanging += OnPageFlowPropertyChanging;
            watchedVm.PageFlow.PropertyChanged += OnPageFlowPropertyChanged;
            watchedVm.NoticeRequested += OnNoticeRequested; // Package 162
            watchedVm.IsWindowActive = () => IsActive;
        }
        OnServerChanged();
        ApplyDisplay();
        ApplyChatLook();
    }

    /// <summary>Package 161: the chat's text size, also while it is only previewed on the settings page.</summary>
    void ApplyChatLook()
    {
        if (watchedVm is null) return;
        double scale = watchedVm.ChatTextScale / 100.0;
        Resources["Ovs.ChatFontSize"] = ChatFontSize * scale;
        Resources["Ovs.ChatLineHeight"] = ChatLineHeight * scale;
    }

    /// <summary>Package 99: the chosen display, also while it is only previewed on the settings page.</summary>
    void ApplyDisplay()
    {
        if (watchedVm is null) return;
        Motion.Apply(this, watchedVm.Appearance.Display);
        // Package 102: what is gone stays a moment to fold away, only in the animated display
        watchedVm.Bookmarks.Leave.Delay = Motion.ChooseDuration(this, Motion.Normal);
        if (watchedVm.Server is { } server) server.Leave.Delay = Motion.ChooseDuration(this, Motion.Normal);
    }

    void OnVmPropertyChanging(object? sender, System.ComponentModel.PropertyChangingEventArgs args)
    {
        if (watchedVm is not { } mainViewModel || args.PropertyName != nameof(MainViewModel.Server)) return;
        // Package 106: connecting, home fades away while the chat comes up from below; disconnecting, the server folds
        // down and home comes back. The channel tree does the same in the sidebar.
        bool wasConnected = mainViewModel.Server is not null;
        PageSlide? Way() => (mainViewModel.Server is not null) == wasConnected ? null
            : mainViewModel.Server is not null ? new PageSlide(new Vector(0, PageMotion.Slide), default) : new PageSlide(default, new Vector(0, PageMotion.Slide));
        if (mainViewModel.PageFlow.IsHomePage) PageMotion.Leave(PageHost, PageGhost, Way); // settings and administration stay what they are
        PageMotion.Leave(TreeHost, TreeGhost, Way);
    }

    /// <summary>
    /// Package 104: before the page or its model changes (closing clears the model first), a picture of the page as it
    /// is now covers the page area; the switch plays once the change is complete.
    /// </summary>
    void OnPageFlowPropertyChanging(object? sender, System.ComponentModel.PropertyChangingEventArgs args)
    {
        if (watchedVm is not { } mainViewModel) return;
        if (args.PropertyName is not (nameof(PageFlowViewModel.Page) or nameof(PageFlowViewModel.ShownPage))) return;
        var pageFlow = mainViewModel.PageFlow;
        var before = pageFlow.Page;
        PageMotion.Leave(PageHost, PageGhost, () => pageFlow.Page == before ? null : PageMotion.Sideways(!pageFlow.IsHomePage));
    }

    /// <summary>Another page than home closes the drawer.</summary>
    void OnPageFlowPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(PageFlowViewModel.Page) && watchedVm?.PageFlow.IsHomePage == false) drawer.Set(false);
    }

    bool wasConnecting;

    /// <summary>Package 106: a connect that failed shakes the start card once and lets the reason fade in.</summary>
    void OnConnectingChanged(MainViewModel viewModel)
    {
        if (wasConnecting && !viewModel.Connection.IsConnecting && viewModel.Server is null)
        {
            ShakeMotion.Shake(StartCard);
            LiveMotion.FadeIn(StatusText);
        }
        wasConnecting = viewModel.Connection.IsConnecting;
    }

    /// <summary>The connecting flag lives on the connection, a second source next to the main view model's <c>Server</c>.</summary>
    void OnConnectionPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ConnectionViewModel.IsConnecting) && watchedVm is { } mainViewModel) OnConnectingChanged(mainViewModel);
    }

    void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MainViewModel.Server))
        {
            if (watchedVm?.Server is not null) LiveMotion.FadeIn(ServerHeader); // Package 106: connected, the server's name fades into the header
            OnServerChanged();
            ApplyDisplay();
        }
        else if (args.PropertyName == nameof(MainViewModel.Appearance)) ApplyDisplay();
        else if (args.PropertyName == nameof(MainViewModel.ChatTextScale)) ApplyChatLook();
        else if (args.PropertyName is nameof(MainViewModel.TalkHint) or nameof(MainViewModel.HasNoPttBinding)) QueueFitSidebar(); // Package 113
    }

    void OnServerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(ServerViewModel.CurrentChannel)) return;
        drawer.Set(false);
        // Package 103: the highlight slides from the channel I left to the one I am in now
        var now = watchedServer?.CurrentChannel;
        if (currentChannel is { } was && now is not null && was != now && ChannelRow(was) is { } from && ChannelRow(now) is { } to)
            _ = FlyGhost.HighlightAsync(from, to);
        currentChannel = now;
    }

    ChannelViewModel? currentChannel;

    Border? ChannelRow(ChannelViewModel channel) =>
        ChannelItems.ContainerFromItem(channel)?.GetVisualDescendants().OfType<Border>().FirstOrDefault(border => border.Classes.Contains("row"));

    /// <summary>Every connect starts with the fitted width again.</summary>
    void OnServerChanged()
    {
        if (watchedServer is not null)
        {
            watchedServer.StateChanged -= OnServerStateChanged;
            watchedServer.PropertyChanged -= OnServerPropertyChanged;
        }
        watchedServer = watchedVm?.Server;
        currentChannel = watchedServer?.CurrentChannel; // a new connection starts where it is, nothing slides
        if (watchedServer is null) return;
        watchedServer.StateChanged += OnServerStateChanged;
        watchedServer.PropertyChanged += OnServerPropertyChanged;
        sidebarDragged = false;
        QueueFitSidebar();
    }

    /// <summary>Package 122: only a change of what the sidebar measures (a channel's name, count or icons) refits it.</summary>
    void OnServerStateChanged()
    {
        if (watchedServer?.LastChange.HasFlag(StateChange.Sidebar) == true) QueueFitSidebar();
    }

    bool fitQueued;

    // After layout, so the rows of new or renamed channels exist and carry their styles. Package 122: changes that come
    // together are measured once.
    void QueueFitSidebar()
    {
        if (fitQueued) return;
        fitQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            fitQueued = false;
            FitSidebar();
        }, DispatcherPriority.Background);
    }

    void OnSidebarDragCompleted(object? sender, VectorEventArgs args) => sidebarDragged = true;

    /// <summary>Package 122: how often the sidebar measured its rows (tests count it).</summary>
    internal int SidebarFits { get; private set; }

    void FitSidebar()
    {
        if (sidebarDragged || watchedServer is null || !IsVisible) return;
        SidebarFits++;
        var rows = ChannelItems.GetRealizedContainers().Where(control => control.DataContext is not ChannelViewModel { IsSeparator: true }).Select(control =>
        {
            var texts = control.GetVisualDescendants().OfType<TextBlock>().ToList();
            var name = texts.FirstOrDefault(textBlock => textBlock.Classes.Contains("channelName"));
            var count = texts.FirstOrDefault(textBlock => textBlock.Classes.Contains("channelCount"));
            var channel = control.DataContext as ChannelViewModel;
            int icons = new[] { channel?.IsDefault, channel?.IsLinked, channel?.IsLocked }.Count(on => on == true); // Package 93: the lock too
            return new SidebarRow(TextWidth(name), icons, TextWidth(count));
        }).ToList();
        if (rows.Count == 0) return;
        sidebarFitted = sidebarWanted = SidebarWidth.For(rows, FooterNeed());
        ApplyResponsive();
    }

    /// <summary>The untrimmed width of a text block's text in its current font.</summary>
    static double TextWidth(TextBlock? text)
    {
        if (string.IsNullOrEmpty(text?.Text)) return 0;
        using var layout = new TextLayout(text.Text, new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize, null);
        return layout.WidthIncludingTrailingWhitespace;
    }

    /// <summary>
    /// Package 113 (A121): the footer untrimmed: padding, avatar, the wider of the nickname and the talk hint (or the hint
    /// button without a PTT key), and the buttons. The "Sendet" texts only show while talking and may trim; sending never
    /// refits, so the width does not jump.
    /// </summary>
    double FooterNeed()
    {
        var hint = PttHint.IsVisible ? PttHint.DesiredSize.Width : TextWidth(TalkHintLine);
        var texts = Math.Max(TextWidth(SelfName), hint);
        return SidebarWidth.Border + SelfFooter.Padding.Left + SelfFooter.Padding.Right + SelfRing.DesiredSize.Width
               + SelfTexts.Margin.Left + SelfTexts.Margin.Right + texts + FooterButtons.DesiredSize.Width;
    }

    // ---- Package 68 (A82, A93, A94): width steps, upper bound of the sidebar, drawer below 700 px ----

    /// <summary>The width the sidebar would like (fitted or dragged) and its fitted minimum; both yield to the header.</summary>
    double sidebarWanted = 300, sidebarFitted, sidebarSet = 300;
    /// <summary>Header width without the title, remembered while the header is hidden (settings, administration).</summary>
    double headerNeed;

    void OnDrawerToggle(object? sender, RoutedEventArgs args) => drawer.Set(!drawer.IsOpen);

    void OnDrawerScrimPressed(object? sender, PointerPressedEventArgs args)
    {
        drawer.Set(false);
        args.Handled = true;
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
        var drawerWidth = compact ? Math.Min(sidebarWanted, width - Responsive.DrawerGap) : double.NaN;
        if (!Sidebar.Width.Equals(drawerWidth)) Sidebar.Width = drawerWidth;

        Responsive.Apply(this, width, compact ? width : width - SidebarSplitter.Width - sidebarSet);
        if (!compact && (drawer.IsOpen || Classes.Contains("drawer"))) drawer.Set(false); // also while it still slides away
    }

    /// <summary>
    /// A82: padding, voice icon and the visible buttons with their texts; the channel title may shrink to nothing.
    /// Texts hidden under narrow count too, so dragging the sidebar never forces the header into icons only.
    /// </summary>
    double HeaderNeed()
    {
        var actions = HeaderActions.Children.Where(control => control.IsVisible).ToList();
        var hiddenTexts = actions.SelectMany(control => control.GetLogicalDescendants().OfType<TextBlock>())
            .Where(textBlock => !textBlock.IsVisible && textBlock.Classes.Contains("wideOnly"))
            .Sum(textBlock => TextWidth(textBlock) + ((textBlock.Parent as StackPanel)?.Spacing ?? 0));
        return ChannelHeader.Padding.Left + ChannelHeader.Padding.Right + VoiceIcon.DesiredSize.Width
            + actions.Sum(control => control.DesiredSize.Width) + hiddenTexts + HeaderActions.Spacing * Math.Max(0, actions.Count - 1);
    }
}
