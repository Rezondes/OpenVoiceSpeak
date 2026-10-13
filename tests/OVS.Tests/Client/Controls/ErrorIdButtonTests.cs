using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using OVS.Client.Controls;
using OVS.Client.Localization;
using OVS.Shared.Protocol;

namespace OVS.Tests.Client.Controls;

/// <summary>Package 143 (A153): the error ID shown with an error is copied with one click.</summary>
public sealed class ErrorIdButtonTests
{
    const string Id = "0123456789abcdef0123456789abcdef";

    [AvaloniaFact]
    public async Task Copy_IdInTheText_PutOnTheClipboard()
    {
        var button = new ErrorIdButton { Text = ErrorTexts.For(ReasonCodes.InvalidValue, null, Id) };
        var plain = new ErrorIdButton { Text = ErrorTexts.For(ReasonCodes.InvalidValue) };
        var window = new Window { Content = new StackPanel { Children = { button, plain } } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            Assert.True(button.IsVisible);
            Assert.False(plain.IsVisible); // without an ID there is nothing to copy

            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(Id, await window.Clipboard!.TryGetTextAsync());
        }
        finally
        {
            window.Close();
        }
    }
}
