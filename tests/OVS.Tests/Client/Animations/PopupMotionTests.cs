using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using OVS.Client.Animations;
using OVS.Client.Settings;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Animations;

/// <summary>
/// Package 105: a popup fades in and slides out of its anchor, up above it (below it: <see cref="OverlayMotionTests"/>);
/// the simplified display shows it at once.
/// </summary>
[Collection(MotionCollection.Name)]
public sealed class PopupMotionTests : IDisposable
{
    public void Dispose() => Motion.IsAnimated = true;

    static (Window Window, Popup Popup, Border Content) Show(DisplayMode display)
    {
        var anchor = new Button { Content = "Anker", VerticalAlignment = VerticalAlignment.Bottom };
        var content = new Border { Width = 120, Height = 40, Background = Brushes.Gray };
        var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Top, Child = content };
        var window = new Window { Width = 400, Height = 300, Content = new Panel { Children = { anchor, popup } } };
        window.Show();
        Motion.Apply(window, display);
        Dispatcher.UIThread.RunJobs();
        return (window, popup, content);
    }

    /// <summary>Closes in the simplified display, which puts no fading picture up: nothing runs into the next test.</summary>
    static void Close(Window window, Popup popup)
    {
        Motion.Apply(window, DisplayMode.Simplified);
        popup.IsOpen = false;
        window.Close();
    }

    [AvaloniaFact]
    public void Open_PlacedAbove_FadesInAndSlidesUpIntoPlace()
    {
        var (window, popup, content) = Show(DisplayMode.Animated);
        try
        {
            // every value as it happens: under load one frame can outlast the whole fade
            var fades = MotionWait.Record(content, v => v.Opacity);
            var slides = MotionWait.Record(content, MotionWait.OffsetY);
            int running = AnimationCounter.Running;
            popup.IsOpen = true;
            MotionWait.Until(() => AnimationCounter.Running <= running); // the fade has ended
            Assert.True(fades.Any(o => o < 1), "fades in");
            Assert.True(slides.Any(y => y > 0), $"comes up from below into its place ({string.Join(" ", slides)})");
            Assert.InRange(slides.Max(), 0, 4.001);
            Assert.Equal(1, content.Opacity, 2);
            Assert.Equal(0, MotionWait.OffsetY(content), 2);
        }
        finally
        {
            Close(window, popup);
        }
    }

    [AvaloniaFact]
    public void Open_Simplified_StartsNoAnimationShowsInPlace()
    {
        var (window, popup, content) = Show(DisplayMode.Simplified);
        try
        {
            int started = AnimationCounter.Started;
            popup.IsOpen = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(popup.IsOpen);
            Assert.Equal(started, AnimationCounter.Started);
            Assert.Equal((1d, 0d), (content.Opacity, MotionWait.OffsetY(content)));
        }
        finally
        {
            Close(window, popup);
        }
    }

    [AvaloniaFact]
    public void Register_CalledAgain_OpeningStillStartsOneFade()
    {
        var (window, popup, _) = Show(DisplayMode.Animated);
        try
        {
            int started = AnimationCounter.Started, running = AnimationCounter.Running;
            popup.IsOpen = true;
            Assert.Equal(started + 1, AnimationCounter.Started); // Motion registered the handler on its first use
            MotionWait.Until(() => AnimationCounter.Running <= running);
            Motion.Apply(window, DisplayMode.Simplified); // closes without a fading picture
            popup.IsOpen = false;
            Motion.Apply(window, DisplayMode.Animated);

            PopupMotion.Register();
            started = AnimationCounter.Started;
            popup.IsOpen = true;
            Assert.Equal(started + 1, AnimationCounter.Started); // one fade, not one per call
            MotionWait.Until(() => AnimationCounter.Running <= running);
        }
        finally
        {
            Close(window, popup);
        }
    }
}
