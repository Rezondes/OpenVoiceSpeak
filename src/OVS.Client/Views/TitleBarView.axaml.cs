using OVS.Client.Localization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Reactive;

namespace OVS.Client.Views;

/// <summary>Title bar of the main window: drag area, window title and the three window buttons.</summary>
public partial class TitleBarView : UserControl
{
    Window? window;
    IDisposable? stateWatch, titleWatch;

    /// <summary>The window's own title bar.</summary>
    public TitleBarView() => InitializeComponent();

    /// <summary>Follows the window's state.</summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs args)
    {
        base.OnAttachedToVisualTree(args);
        window = TopLevel.GetTopLevel(this) as Window;
        if (window is null) return;
        stateWatch = window.GetObservable(Window.WindowStateProperty).Subscribe(new AnonymousObserver<WindowState>(OnStateChanged));
        titleWatch = window.GetObservable(Window.TitleProperty).Subscribe(new AnonymousObserver<string?>(text => TitleText.Text = text));
    }

    /// <summary>Stops following the window.</summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs args)
    {
        stateWatch?.Dispose();
        titleWatch?.Dispose();
        base.OnDetachedFromVisualTree(args);
    }

    void OnStateChanged(WindowState state)
    {
        bool maximized = state == WindowState.Maximized;
        MaximizeIcon.IsVisible = !maximized;
        RestoreIcon.IsVisible = maximized;
        var label = maximized ? Strings.Window_Restore : Strings.Window_Maximize;
        ToolTip.SetTip(MaximizeButton, label);
        AutomationProperties.SetName(MaximizeButton, label);
    }

    void OnMinimize(object? sender, RoutedEventArgs args)
    {
        if (window is not null) window.WindowState = WindowState.Minimized;
    }

    void OnMaximize(object? sender, RoutedEventArgs args) => ToggleMaximized();

    void OnClose(object? sender, RoutedEventArgs args) => window?.Close();

    void ToggleMaximized()
    {
        if (window is not null)
            window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    /// <summary>The native move loop keeps Aero Snap working (drag to a screen edge).</summary>
    void OnDragAreaPressed(object? sender, PointerPressedEventArgs args)
    {
        if (window is null || !args.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (args.ClickCount == 2) ToggleMaximized();
        else window.BeginMoveDrag(args);
    }
}
