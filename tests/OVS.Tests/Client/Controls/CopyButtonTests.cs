using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using OVS.Client.Controls;
using OVS.Client.Converters;
using OVS.Client.Localization;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Controls;

/// <summary>Package 164: a full fingerprint is copied with one click.</summary>
public sealed class CopyButtonTests
{
    static readonly string Fingerprint = string.Concat(Enumerable.Repeat("0123456789abcdef", 4));

    static Shown Show(string? text, TimeProvider? time = null)
    {
        var button = new CopyButton { Text = text, Time = time ?? TimeProvider.System };
        var window = new Window { Content = button };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return new Shown(window, button);
    }

    static void Click(CopyButton button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task Click_Fingerprint_CopiesExactText()
    {
        var (window, button) = Show(Fingerprint);
        try
        {
            Assert.True(button.IsVisible);
            Click(button);
            Assert.Equal(Fingerprint, await window.Clipboard!.TryGetTextAsync()); // exactly the 64 characters
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Click_TooltipCopied_ThenBack()
    {
        var time = new ManualTimeProvider();
        var (window, button) = Show(Fingerprint, time);
        try
        {
            Assert.Equal(Strings.Ui_CopyFingerprint, ToolTip.GetTip(button));
            Click(button);
            Assert.Equal(Strings.Ui_Copied, ToolTip.GetTip(button));
            time.Advance(TimeSpan.FromSeconds(1.9));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Strings.Ui_Copied, ToolTip.GetTip(button));
            time.Advance(TimeSpan.FromSeconds(0.1));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Strings.Ui_CopyFingerprint, ToolTip.GetTip(button));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Button_NoText_Hidden()
    {
        var (window, button) = Show(null);
        try
        {
            Assert.False(button.IsVisible);
            button.Text = "";
            Assert.False(button.IsVisible);
            button.Text = Fingerprint;
            Assert.True(button.IsVisible);
            PrivacyConverters.SetHide(window, true); // Package 160: streamer mode hides the value, so nothing to copy
            Assert.False(button.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    sealed record Shown(Window Window, CopyButton Button);
}
