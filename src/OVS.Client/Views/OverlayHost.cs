using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;

namespace OVS.Client.Views;

/// <summary>
/// Modal layer inside the main window (A20: the client never opens a second window). A scrim covers everything
/// and swallows clicks, the card in the middle keeps the keyboard focus (Tab cycles inside it).
/// Esc cancels, Enter runs the card's default action. Requests arriving while one is open wait their turn.
/// </summary>
public sealed class OverlayHost : Panel
{
    readonly SemaphoreSlim turn = new(1, 1);
    readonly Border card;
    TaskCompletionSource? current;
    Action? onEnter;
    IInputElement? focusBefore;
    TopLevel? top;

    public OverlayHost()
    {
        IsVisible = false;
        var scrim = new Border();
        scrim.Bind(Border.BackgroundProperty, scrim.GetResourceObservable("Ovs.Scrim"));
        card = new Border { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Width = 460, Margin = new Thickness(24) };
        card.Classes.Add("dialog");
        KeyboardNavigation.SetTabNavigation(card, KeyboardNavigationMode.Cycle);
        Children.Add(scrim);
        Children.Add(card);
    }

    public bool IsOpen => current is not null;

    /// <summary>Shows the content until <see cref="Close"/> is called.</summary>
    /// <param name="onEnter">What Enter does, usually the default button.</param>
    /// <param name="focus">Gets the keyboard focus once shown: the first input, otherwise the default button.</param>
    public async Task ShowAsync(Control content, Action onEnter, Control? focus = null)
    {
        await turn.WaitAsync();
        try
        {
            current = new TaskCompletionSource();
            this.onEnter = onEnter;
            top = TopLevel.GetTopLevel(this);
            focusBefore = top?.FocusManager?.GetFocusedElement();
            // Tunnel on the window: Esc and Enter reach the dialog even if the focus sits somewhere else.
            top?.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
            card.Child = content;
            IsVisible = true;
            Dispatcher.UIThread.Post(() => focus?.Focus(), DispatcherPriority.Loaded);
            await current.Task;
        }
        finally
        {
            top?.RemoveHandler(KeyDownEvent, OnKeyDown);
            card.Child = null;
            IsVisible = false;
            current = null;
            this.onEnter = null;
            focusBefore?.Focus();
            turn.Release();
        }
    }

    public void Close() => current?.TrySetResult();

    void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!IsOpen || e.Source is ComboBox { IsDropDownOpen: true }) return;
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        // Enter in a multi-line box is a line break, and a focused button presses itself.
        else if (e.Key == Key.Enter && e.Source is not (TextBox { AcceptsReturn: true } or Button))
        {
            onEnter?.Invoke();
            e.Handled = true;
        }
    }
}
