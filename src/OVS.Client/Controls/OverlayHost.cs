using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using OVS.Client.Animations;

namespace OVS.Client.Controls;

/// <summary>
/// Modal layer inside the main window (A20: the client never opens a second window). A scrim covers everything
/// and swallows clicks, the card in the middle keeps the keyboard focus (Tab cycles inside it).
/// Esc cancels, Enter runs the card's default action. Requests arriving while one is open wait their turn.
/// Package 105: in the animated display the scrim fades in and the card pops (0.94 to full size); closing plays it
/// back, and the next dialog waits until it has gone. A refused input shakes the card.
/// </summary>
public sealed class OverlayHost : Panel
{
    /// <summary>The scale a dialog card pops in from.</summary>
    public const double PopFrom = 0.94;

    readonly SemaphoreSlim turn = new(1, 1);
    readonly Border card, scrim;
    TaskCompletionSource? current;
    CancellationTokenSource? closing;
    Action? onEnter;
    IInputElement? focusBefore;
    TopLevel? top;

    /// <summary>An empty overlay.</summary>
    public OverlayHost()
    {
        IsVisible = false;
        scrim = new Border();
        scrim.Bind(Border.BackgroundProperty, scrim.GetResourceObservable("Ovs.Scrim"));
        // Package 79: 460 px where there is room, otherwise the window width less 16 px on each side (stretched but capped = centered)
        card = new Border { VerticalAlignment = VerticalAlignment.Center, MaxWidth = 460, Margin = new Thickness(16) };
        card.Classes.Add("dialog");
        KeyboardNavigation.SetTabNavigation(card, KeyboardNavigationMode.Cycle);
        Children.Add(scrim);
        Children.Add(card);
    }

    /// <summary>A dialog is shown.</summary>
    public bool IsOpen => current is not null;

    /// <summary>Shows the content until <see cref="Close"/> is called.</summary>
    /// <param name="onEnter">What Enter does, usually the default button.</param>
    /// <param name="focus">Gets the keyboard focus once shown: the first input, otherwise the default button.</param>
    /// <param name="content">The dialog card.</param>
    public async Task ShowAsync(Control content, Action onEnter, Control? focus = null)
    {
#pragma warning disable CA1849 // Cancel, not CancelAsync: the closing card must finish going before this line returns, never two cards
        closing?.Cancel(); // the next dialog is here: the last one finishes going at once
#pragma warning restore CA1849
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
            ClearValue(IsHitTestVisibleProperty);
            IsVisible = true;
            if (Motion.IsAnimated(this)) _ = PlayAsync(appear: true);
            Dispatcher.UIThread.Post(() => focus?.Focus(), DispatcherPriority.Loaded);
            await current.Task;
        }
        finally
        {
            // closed for everyone at once (keys, clicks, the answer); the card only goes away visibly
            top?.RemoveHandler(KeyDownEvent, OnKeyDown);
            current = null;
            this.onEnter = null;
            focusBefore?.Focus();
            IsHitTestVisible = false; // the focus went back above: neither keys nor clicks reach the fading card
            _ = GoneAsync();
        }
    }

    /// <summary>The card goes (played back in the animated display); then the next dialog may come.</summary>
    async Task GoneAsync()
    {
        if (Motion.IsAnimated(this))
        {
            var cancel = closing = new CancellationTokenSource();
            await PlayAsync(appear: false, cancel.Token);
            closing = null;
        }
        card.Child = null;
        IsVisible = false;
        // a finished closing animation keeps its last values (faded, small); the next card starts whole
        card.ClearValue(OpacityProperty);
        card.ClearValue(RenderTransformProperty);
        scrim.ClearValue(OpacityProperty);
        turn.Release();
    }

    Task PlayAsync(bool appear, CancellationToken cancel = default)
    {
        double from = appear ? 0 : 1, to = appear ? 1 : 0;
        double scaleFrom = appear ? PopFrom : 1, scaleTo = appear ? 1 : PopFrom;
        var fill = appear ? FillMode.Backward : FillMode.Forward;
        var easing = appear ? Motion.Pop : Motion.Ease;
        var cardShown = new Animation
        {
            Duration = Motion.Normal,
            Easing = easing,
            FillMode = fill,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, from), new Setter(ScaleTransform.ScaleXProperty, scaleFrom), new Setter(ScaleTransform.ScaleYProperty, scaleFrom) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, to), new Setter(ScaleTransform.ScaleXProperty, scaleTo), new Setter(ScaleTransform.ScaleYProperty, scaleTo) } },
            },
        }.PlayAsync(card, cancel);
        var scrimShown = new Animation
        {
            Duration = Motion.Normal,
            Easing = Motion.Ease,
            FillMode = fill,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, from) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, to) } },
            },
        }.PlayAsync(scrim, cancel);
        return Task.WhenAll(cardShown, scrimShown);
    }

    /// <summary>Package 105: a refused input (a field missing, the server said no) shakes the card once.</summary>
    public void Shake()
    {
        if (IsOpen) ShakeMotion.Shake(card);
    }

    /// <summary>Closes the dialog shown.</summary>
    public void Close() => current?.TrySetResult();

    void OnKeyDown(object? sender, KeyEventArgs args)
    {
        if (!IsOpen || args.Source is ComboBox { IsDropDownOpen: true }) return;
        if (args.Key == Key.Escape)
        {
            Close();
            args.Handled = true;
        }
        // Enter in a multi-line box is a line break, and a focused button presses itself.
        else if (args.Key == Key.Enter && args.Source is not (TextBox { AcceptsReturn: true } or Button))
        {
            onEnter?.Invoke();
            args.Handled = true;
        }
    }
}
