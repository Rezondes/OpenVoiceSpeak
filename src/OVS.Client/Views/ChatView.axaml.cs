using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using OVS.Client.ViewModels;

namespace OVS.Client.Views;

/// <summary>Chat tabs with the composer (Package 32).</summary>
public partial class ChatView : UserControl
{
    /// <summary>True while the list shows its end: new lines then scroll along, otherwise the reader keeps the place.</summary>
    bool atEnd = true;
    ChatViewModel? watched;

    public ChatView()
    {
        InitializeComponent();
        // Tunnel: the TextBox would otherwise take Enter as a new line itself.
        Composer.AddHandler(KeyDownEvent, OnComposerKeyDown, RoutingStrategies.Tunnel);
        Scroller.ScrollChanged += OnScrollChanged;
        DataContextChanged += (_, _) => Watch();
    }

    void Watch()
    {
        if (watched is not null) watched.PropertyChanged -= OnVmPropertyChanged;
        watched = DataContext as ChatViewModel;
        if (watched is not null) watched.PropertyChanged += OnVmPropertyChanged;
        ScrollToEnd();
    }

    void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatViewModel.Selected)) ScrollToEnd(); // another tab starts at its newest line
    }

    void OnComposerKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers.HasFlag(KeyModifiers.Shift)) return;
        e.Handled = true;
        if (watched?.SendCommand.CanExecute(null) == true) watched.SendCommand.Execute(null);
    }

    void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentDelta.Y != 0 || e.ViewportDelta.Y != 0)
        {
            if (atEnd) Scroller.ScrollToEnd(); // content or window changed: follow if the reader was at the end
            return;
        }
        // Only the reader's own scrolling decides whether we follow.
        if (e.OffsetDelta.Y != 0) atEnd = Scroller.Offset.Y >= Scroller.Extent.Height - Scroller.Viewport.Height - 8;
    }

    void ScrollToEnd()
    {
        atEnd = true;
        Dispatcher.UIThread.Post(() => Scroller.ScrollToEnd(), DispatcherPriority.Background);
    }
}
