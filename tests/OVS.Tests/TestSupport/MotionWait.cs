using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using OVS.Client.Views;

namespace OVS.Tests.TestSupport;

/// <summary>
/// Waiting in the motion tests. The animation clock runs on real time and frames only come when a test asks for them;
/// on a busy machine one frame can take longer than a whole animation. So a test waits for what it checks instead of a
/// fixed time (it goes on as soon as that holds, no slower when all goes well), and records the values an animation
/// passes through instead of sampling a frame.
/// </summary>
static class MotionWait
{
    /// <summary>How long a wait gives up after: far beyond any animation, so only a broken one gets there.</summary>
    const int Cap = 5000;

    public static void Frame()
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Lets the clock run for <paramref name="milliseconds"/>: the animation clock runs on real time.</summary>
    public static void Settle(int milliseconds)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < milliseconds)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(1); // leaves the CPU to the timing tests running beside
        }
    }

    /// <summary>
    /// Lets the clock run until <paramref name="done"/> holds, at most <paramref name="ms"/>; then goes on either way (a
    /// caller that needs it checks it, a caller with a condition that never holds just waits).
    /// </summary>
    public static void WaitFor(Func<bool> done, int ms)
    {
        var watch = Stopwatch.StartNew();
        while (!done() && watch.ElapsedMilliseconds < ms)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(1);
        }
    }

    /// <summary>
    /// Frames until <paramref name="done"/> holds and at least <paramref name="atLeast"/> ms have passed (at most 5 s).
    /// <paramref name="done"/> is asked after every frame, so it may also note what the frame showed.
    /// </summary>
    public static void Until(Func<bool> done, int atLeast = 0)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            bool holds = done();
            if (holds && watch.ElapsedMilliseconds >= atLeast || watch.ElapsedMilliseconds >= Cap) return;
            Frame();
            Thread.Sleep(1); // leaves the CPU to the timing tests running beside
        }
    }

    /// <summary>Frames until the checks pass (at most 5 s), then checks once more, so a failure shows its message.</summary>
    public static void Eventually(Action check)
    {
        Until(() =>
        {
            try
            {
                check();
                return true;
            }
            catch (Xunit.Sdk.XunitException)
            {
                return false;
            }
        });
        check();
    }

    /// <summary>
    /// Every value <paramref name="value"/> reads from <paramref name="visual"/> after any change of it from now on, also
    /// changes inside its render transform (an animation keeps an offset or a scale in a transform of a group).
    /// </summary>
    public static List<double> Record(Visual visual, Func<Visual, double> value)
    {
        var seen = new List<double>();
        void Track(AvaloniaObject transform)
        {
            if (transform is TransformGroup group)
            {
                foreach (var child in group.Children) Track(child);
                group.Children.CollectionChanged += (_, e) =>
                {
                    foreach (var added in e.NewItems?.OfType<AvaloniaObject>() ?? []) Track(added);
                };
            }
            transform.PropertyChanged += (_, _) => seen.Add(value(visual));
        }
        if (visual.RenderTransform is AvaloniaObject now) Track(now);
        visual.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.RenderTransformProperty && e.NewValue is AvaloniaObject transform) Track(transform);
            seen.Add(value(visual));
        };
        return seen;
    }

    /// <summary>How far the render transform of <paramref name="visual"/> moves it sideways (0 without one).</summary>
    public static double OffsetX(Visual visual) => visual.RenderTransform?.Value.M31 ?? 0;

    /// <summary>How far the render transform of <paramref name="visual"/> moves it down (0 without one).</summary>
    public static double OffsetY(Visual visual) => visual.RenderTransform?.Value.M32 ?? 0;

    /// <summary>The connect sequence is over: the pictures have gone, the server view and every channel row stand still.</summary>
    public static bool IsConnected(MainWindow main)
    {
        var host = main.FindControl<Panel>("PageHost")!;
        return !main.FindControl<Border>("PageGhost")!.IsVisible && !main.FindControl<Border>("TreeGhost")!.IsVisible
            && main.FindControl<Border>("ServerHeader")!.Opacity == 1 && host.Opacity == 1 && (host.RenderTransform?.Value.M32 ?? 0) == 0
            && main.FindControl<ItemsControl>("ChannelItems")!.GetRealizedContainers().All(r => r.Opacity == 1 && !r.Classes.Contains("entering"));
    }

    /// <summary>
    /// Waits until the connect sequence is over (call it after a first settle, so the sequence has begun): a test that
    /// acts while the tree still builds up would see the build-up's movements as its own.
    /// </summary>
    public static void Connected(MainWindow main) => Until(() => IsConnected(main));
}
