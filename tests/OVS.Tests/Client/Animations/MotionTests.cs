using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OVS.Client.Animations;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Client.Views;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Animations;

/// <summary>Package 99 (A114, A115): the switch between the animated and the simplified display, one per window.</summary>
[Collection(MotionCollection.Name)]
public sealed class MotionTests() : TempDirectoryTest("ovs-motion-", quiet: true)
{
    [AvaloniaFact]
    public void Apply_SetsWindowClassAndDurations()
    {
        var window = new Window();
        Motion.Apply(window, DisplayMode.Animated);
        Assert.Contains("animated", window.Classes);
        Assert.True(Motion.IsAnimated(window));
        Assert.Equal(Motion.Normal, Motion.ChooseDuration(window, Motion.Normal));

        Motion.Apply(window, DisplayMode.Simplified);
        Assert.DoesNotContain("animated", window.Classes);
        Assert.False(Motion.IsAnimated(window));
        Assert.Equal(TimeSpan.Zero, Motion.ChooseDuration(window, Motion.Slow));
        Assert.True(Motion.Fast < Motion.Normal && Motion.Normal < Motion.Slow);
    }

    [AvaloniaFact]
    public void Apply_TwoWindows_EachControlFollowsItsOwnWindow()
    {
        var calm = new Border();
        var lively = new Border();
        var simplified = new Window { Content = calm };
        var animated = new Window { Content = lively };
        Motion.Apply(simplified, DisplayMode.Simplified);
        Motion.Apply(animated, DisplayMode.Animated);
        Assert.Equal((false, true), (Motion.IsAnimated(calm), Motion.IsAnimated(lively)));

        Motion.Apply(animated, DisplayMode.Simplified); // each switch reaches the controls of its own window only
        Motion.Apply(simplified, DisplayMode.Animated);
        Assert.Equal((true, false), (Motion.IsAnimated(calm), Motion.IsAnimated(lively)));
    }

    [AvaloniaFact]
    public void IsAnimated_ControlInNoWindow_True() => Assert.True(Motion.IsAnimated(new Border()));

    [AvaloniaFact]
    public void IsReduced_ControlInNoWindow_False() => Assert.False(Motion.IsReduced(new Border()));

    [AvaloniaFact]
    public void IsReduced_SetOnWindow_ItsControlsInheritIt()
    {
        var row = new Border();
        var window = new Window { Content = row };
        window.SetValue(Motion.ReducedProperty, true);
        Assert.True(Motion.IsReduced(row));
    }

    /// <summary>The Windows setting is read at once and again whenever the window comes to the front.</summary>
    [AvaloniaFact]
    public void FollowReduced_SettingChangedThenWindowActivated_PropertyFollows()
    {
        bool reduced = true;
        var window = new Window();
        Motion.FollowReduced(window, () => reduced);
        Assert.True(Motion.IsReduced(window));
        window.Show();
        try
        {
            reduced = false;
            window.Activate();
            Dispatcher.UIThread.RunJobs(); // the platform reports the activation through the dispatcher
            Assert.False(Motion.IsReduced(window));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>A popup's content lives in a layer of its own, yet shows the display of the window it opens in.</summary>
    [AvaloniaFact]
    public void IsAnimated_FlyoutAndMenuOfSimplifiedWindow_FalseForTheirContent()
    {
        var anchor = new Button { Content = "Anker" };
        var window = new Window { Width = 300, Height = 200, Content = anchor };
        var content = new Border { Width = 40, Height = 20 };
        var flyout = new Flyout { Content = content };
        var item = new MenuItem { Header = "Eintrag" };
        var menu = new ContextMenu { Items = { item } };
        anchor.ContextMenu = menu;
        window.Show();
        Motion.Apply(window, DisplayMode.Simplified); // opens and closes without a fade: nothing runs into the next test
        try
        {
            flyout.ShowAt(anchor);
            menu.Open(anchor);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal((false, false), (Motion.IsAnimated(content), Motion.IsAnimated(item)));
        }
        finally
        {
            flyout.Hide();
            menu.Close();
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MainWindow_FollowsTheChosenDisplay()
    {
        var viewModel = MainViewModelSetup.Create(dir, action => action());
        var main = new MainWindow { DataContext = viewModel };
        main.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("animated", main.Classes); // the default (A114)

        viewModel.Appearance = viewModel.Appearance with { Display = DisplayMode.Simplified };
        Assert.DoesNotContain("animated", main.Classes);
        Assert.False(Motion.IsAnimated(main));

        viewModel.Appearance = viewModel.Appearance with { Display = DisplayMode.Animated };
        Assert.Contains("animated", main.Classes);
        main.Close();
    }
}
