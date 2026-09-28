using OVS.Client.Localization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Reactive;

namespace OVS.Client.Views;

/// <summary>Title bar of the main window: drag area, window title and the three window buttons.</summary>
public partial class TitleBar : UserControl
{
    Window? window;
    IDisposable? stateWatch, titleWatch;

    public TitleBar() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        window = TopLevel.GetTopLevel(this) as Window;
        if (window is null) return;
        stateWatch = window.GetObservable(Window.WindowStateProperty).Subscribe(new AnonymousObserver<WindowState>(OnStateChanged));
        titleWatch = window.GetObservable(Window.TitleProperty).Subscribe(new AnonymousObserver<string?>(t => TitleText.Text = t));
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        stateWatch?.Dispose();
        titleWatch?.Dispose();
        base.OnDetachedFromVisualTree(e);
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

    void OnMinimize(object? sender, RoutedEventArgs e)
    {
        if (window is not null) window.WindowState = WindowState.Minimized;
    }

    void OnMaximize(object? sender, RoutedEventArgs e) => ToggleMaximized();

    void OnClose(object? sender, RoutedEventArgs e) => window?.Close();

    void ToggleMaximized()
    {
        if (window is not null)
            window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    /// <summary>The native move loop keeps Aero Snap working (drag to a screen edge).</summary>
    void OnDragAreaPressed(object? sender, PointerPressedEventArgs e)
    {
        if (window is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2) ToggleMaximized();
        else window.BeginMoveDrag(e);
    }
}
