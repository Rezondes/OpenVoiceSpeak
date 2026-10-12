using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using OVS.Client.Controls;
using OVS.Client.Localization;
using OVS.Client.ViewModels;
using static OVS.Client.Dialogs.DialogControls;

namespace OVS.Client.Dialogs;

/// <summary>
/// The one frame of every dialog, shown as a card on the main window's overlay, never as a window of its own (A20):
/// icon and title, body, button bar. Returns null when cancelled.
/// </summary>
internal static class DialogFrame
{
    /// <summary>The width of every answer button of the bar.</summary>
    internal const double ButtonMinWidth = 96;

    /// <summary>The class every answer button of the bar carries.</summary>
    internal const string AnswerClass = "answer";

    internal static async Task<T?> ShowAsync<T>(OverlayHost host, string title, string icon, Control body, Func<T?> accept, DialogOptions<T>? options = null)
        where T : class
    {
        var (okText, okIsDefault, kind, cancelText, submit, thirdAnswer, withCancel) = options ?? new();
        T? result = null;
        var ok = new Button { Content = okText, MinWidth = ButtonMinWidth, HorizontalContentAlignment = HorizontalAlignment.Center };
        var failed = CreateText("", "danger");
        failed.IsVisible = false;
        PendingActionViewModel? sending = null;
        var closed = new TaskCompletionSource(); // cancelled meanwhile: nobody waits for the answer any more
        async void Accept()
        {
            if (sending?.IsRunning == true) return;
            if (accept() is not { } answer)
            {
                host.Shake(); // Package 105: something is missing or wrong
                return;
            }
            if (submit is null)
            {
                result = answer;
                host.Close();
                return;
            }
            failed.IsVisible = false;
            sending = submit(answer);
            void OnBusy(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
            {
                if (e.PropertyName != nameof(PendingActionViewModel.IsBusy)) return;
                ok.IsEnabled = !sending.IsBusy;
                ok.Content = sending.IsBusy
                    ? new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new BusySpinner(), new TextBlock { Text = okText } } }
                    : okText;
            }
            sending.PropertyChanged += OnBusy;
            var ended = sending.Completion;
            await Task.WhenAny(ended, closed.Task);
            sending.PropertyChanged -= OnBusy;
            if (closed.Task.IsCompleted) return; // the card is gone
            var error = ended.Result;
            if (error is null)
            {
                result = answer;
                host.Close();
                return;
            }
            failed.Text = error;
            failed.IsVisible = true;
            host.Shake(); // Package 105: the server said no
            Avalonia.Threading.Dispatcher.UIThread.Post(() => failed.BringIntoView(), Avalonia.Threading.DispatcherPriority.Background); // below a long form
        }

        ok.Classes.Add(kind == DialogKind.Danger ? "danger" : "accent");
        var cancel = new Button { Content = cancelText ?? Strings.Dlg_Cancel, MinWidth = ButtonMinWidth, HorizontalContentAlignment = HorizontalAlignment.Center };
        ok.Click += (_, _) => Accept();
        cancel.Click += (_, _) => host.Close();
        // the third answer (DialogOptions.ThirdAnswer)
        var third = thirdAnswer is { } choice ? new Button { Content = choice.Text, MinWidth = ButtonMinWidth, HorizontalContentAlignment = HorizontalAlignment.Center } : null;
        if (third is not null)
            third.Click += (_, _) =>
            {
                result = thirdAnswer!.Value.Value;
                host.Close();
            };

        var badge = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 12, 0), Child = CreateIcon(icon, kind == DialogKind.Danger ? "danger" : "accent") };
        badge.Bind(Border.BackgroundProperty, badge.GetResourceObservable(kind == DialogKind.Danger ? "Ovs.DangerSurface" : "Ovs.AccentSurface"));
        var heading = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        heading.Classes.Add("h2");
        heading.FontSize = 17;

        var buttonBar = new Border
        {
            Padding = new Thickness(24, 12),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, ItemSpacing = 8, LineSpacing = 8 },
        };
        foreach (var b in new[] { ok, cancel, third }) b?.Classes.Add(AnswerClass);
        if (third is null) ((WrapPanel)buttonBar.Child).Children.AddRange(withCancel ? [cancel, ok] : [ok]); // Package 133: a card that only informs
        else
        {
            // three answers do not fit one row of the card: one below the other, the default one first
            foreach (var b in new[] { ok, third, cancel }) b.HorizontalAlignment = HorizontalAlignment.Stretch;
            buttonBar.Child = new StackPanel { Spacing = 8, Children = { ok, third, cancel } };
        }
        buttonBar.Bind(Border.BackgroundProperty, buttonBar.GetResourceObservable("Ovs.DialogBar"));
        buttonBar.Bind(Border.BorderBrushProperty, buttonBar.GetResourceObservable("Ovs.Border"));

        var frame = new DockPanel
        {
            Children =
            {
                DockAt(buttonBar, Dock.Bottom),
                // Package 79: a dialog taller than the window scrolls its content, the button bar stays in view
                new ScrollViewer
                {
                    Content = new StackPanel
                    {
                        Margin = new Thickness(24, 20, 24, 20),
                        Spacing = 16,
                        Children =
                        {
                            new DockPanel { Children = { DockAt(badge, Dock.Left), heading } },
                            body,
                            failed, // Package 98: why the server refused it
                        },
                    },
                },
            },
        };
        // what Enter does: DialogOptions.OkIsDefault
        var firstInput = body.GetLogicalDescendants().Prepend(body).OfType<InputElement>()
            .FirstOrDefault(c => c is TextBox or ComboBox or ListBox or NumericUpDown);
        await host.ShowAsync(frame, okIsDefault ? Accept : host.Close, (Control?)firstInput ?? (okIsDefault ? ok : cancel));
        closed.SetResult();
        return result;
    }
}
