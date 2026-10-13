using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OVS.Client.Animations;
using OVS.Client.Controls;
using OVS.Client.Dialogs;
using OVS.Client.Settings;

namespace OVS.Tests.TestSupport;

/// <summary>An overlay alone in a window in the simplified display (a card is there at once), and the buttons of its dialog.</summary>
static class OverlayRig
{
    /// <summary>Shows the window; the caller closes it in <c>finally</c>.</summary>
    public static OverlayScene Open()
    {
        var overlay = new OverlayHost();
        var window = new Window { Width = 600, Height = 500, Content = overlay };
        window.Show();
        Motion.Apply(window, DisplayMode.Simplified);
        Dispatcher.UIThread.RunJobs();
        return new(window, overlay);
    }

    /// <summary>The answer buttons of the open card's bar, in the order they are laid out.</summary>
    public static List<Button> FindButtons(OverlayHost overlay) =>
        overlay.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains(DialogFrame.AnswerClass)).ToList();

    /// <summary>The default answer: the one in the accent colour, or red where it is risky.</summary>
    public static Button FindMainButton(OverlayHost overlay) =>
        FindButtons(overlay).Single(button => button.Classes.Contains("danger") || button.Classes.Contains("accent"));

    public static string? ReadText(Button button) => button.Content as string;

    /// <summary>Clicks <paramref name="button"/> and runs what the click started on the UI thread.</summary>
    public static void Click(Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }
}
