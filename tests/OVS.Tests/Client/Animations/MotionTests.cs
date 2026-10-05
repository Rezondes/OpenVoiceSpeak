using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OVS.Client.Animations;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Client.Views;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Animations;

/// <summary>Package 99 (A114, A115): the switch between the animated and the simplified display.</summary>
[Collection(MotionCollection.Name)]
public sealed class MotionTests() : TempDirectoryTest("ovs-motion-", quiet: true)
{
    public override void Dispose()
    {
        Motion.IsAnimated = true;
        base.Dispose();
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
