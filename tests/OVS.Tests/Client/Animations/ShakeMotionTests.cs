using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OVS.Client.Animations;
using OVS.Client.Settings;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Animations;

/// <summary>Package 105, 106: a refused input or a failed connect shakes its card once (3 times 6 px).</summary>
[Collection(MotionCollection.Name)]
public sealed class ShakeMotionTests
{
    static CardScene Show(DisplayMode display)
    {
        var card = new Border { Width = 120, Height = 60 };
        var window = new Window { Width = 400, Height = 300, Content = card };
        window.Show();
        Motion.Apply(window, display);
        Dispatcher.UIThread.RunJobs();
        return new(window, card);
    }

    [AvaloniaFact]
    public void Shake_Animated_SwingsAtMostSixPxAndEndsAtRest()
    {
        var (window, card) = Show(DisplayMode.Animated);
        try
        {
            var swings = MotionWait.Record(card, MotionWait.OffsetX); // every swing as it happens: under load one frame can outlast the shake
            int running = AnimationCounter.Running;
            ShakeMotion.Shake(card);
            MotionWait.Until(() => AnimationCounter.Running <= running); // the shake has ended
            double swing = swings.Select(Math.Abs).DefaultIfEmpty(0).Max();
            Assert.InRange(swing, 2, 6.001);
            Assert.Equal(0, MotionWait.OffsetX(card), 2);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Shake_Simplified_StartsNoAnimation()
    {
        var (window, card) = Show(DisplayMode.Simplified);
        try
        {
            int started = AnimationCounter.Started;
            ShakeMotion.Shake(card);
            Assert.Equal(started, AnimationCounter.Started);
        }
        finally
        {
            window.Close();
        }
    }

    sealed record CardScene(Window Window, Border Card);
}
