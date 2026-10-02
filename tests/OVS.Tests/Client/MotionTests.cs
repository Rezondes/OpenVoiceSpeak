using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Client.Views;

namespace OVS.Tests.Client;

/// <summary>Package 99 (A114, A115): the switch between the animated and the simplified display.</summary>
public sealed class MotionTests : IDisposable
{
    readonly string dir = Directory.CreateTempSubdirectory("ovs-motion-").FullName;

    public void Dispose()
    {
        Motion.IsAnimated = true;
        try
        {
            Directory.Delete(dir, true);
        }
        catch (IOException)
        {
        }
    }

    [AvaloniaFact]
    public void Apply_SetsWindowClassAndDurations()
    {
        var window = new Window();
        Motion.Apply(window, DisplayMode.Animated);
        Assert.Contains("animated", window.Classes);
        Assert.True(Motion.IsAnimated);
        Assert.Equal(Motion.Normal, Motion.Duration(Motion.Normal));

        Motion.Apply(window, DisplayMode.Simplified);
        Assert.DoesNotContain("animated", window.Classes);
        Assert.False(Motion.IsAnimated);
        Assert.Equal(TimeSpan.Zero, Motion.Duration(Motion.Slow));
        Assert.True(Motion.Fast < Motion.Normal && Motion.Normal < Motion.Slow);
    }

    [AvaloniaFact]
    public void MainWindow_FollowsTheChosenDisplay()
    {
        var vm = new MainViewModel(dir, a => a(), useAudioDevices: false);
        var main = new MainWindow { DataContext = vm };
        main.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("animated", main.Classes); // the default (A114)

        vm.Appearance = vm.Appearance with { Display = DisplayMode.Simplified };
        Assert.DoesNotContain("animated", main.Classes);
        Assert.False(Motion.IsAnimated);

        vm.Appearance = vm.Appearance with { Display = DisplayMode.Animated };
        Assert.Contains("animated", main.Classes);
        main.Close();
    }
}

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
