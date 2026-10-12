using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OVS.Client.Animations;
using OVS.Client.Layout;
using OVS.Client.Settings;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Layout;

/// <summary>Package 68, 105: the sidebar as a drawer below the compact width.</summary>
[Collection(MotionCollection.Name)]
public sealed class SidebarDrawerTests
{
    readonly Border sidebar = new() { Width = 200 };
    Window window = null!;

    SidebarDrawer Show(DisplayMode display, bool compact)
    {
        var scrim = new Border();
        window = new Window { Width = 400, Height = 300, Content = new Panel { Children = { scrim, sidebar } } };
        window.Show();
        Motion.Apply(window, display);
        window.Classes.Set("compact", compact);
        Dispatcher.UIThread.RunJobs();
        return new SidebarDrawer(window, sidebar, scrim);
    }

    [AvaloniaFact]
    public void Set_NotCompact_StaysClosed()
    {
        var drawer = Show(DisplayMode.Animated, compact: false);
        try
        {
            drawer.Set(true);

            Assert.False(drawer.IsOpen);
            Assert.DoesNotContain("drawer", window.Classes);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Set_CompactSimplified_OpensAndClosesAtOnce()
    {
        var drawer = Show(DisplayMode.Simplified, compact: true);
        try
        {
            drawer.Set(true);
            Assert.True(drawer.IsOpen);
            Assert.Contains("drawer", window.Classes);

            drawer.Set(false);
            Assert.False(drawer.IsOpen);
            Assert.DoesNotContain("drawer", window.Classes);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Set_ClosedAgain_KeepsItClosed()
    {
        var drawer = Show(DisplayMode.Animated, compact: true);
        try
        {
            drawer.Set(false);

            Assert.False(drawer.IsOpen);
            Assert.DoesNotContain("drawer", window.Classes);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Set_CompactAnimated_ShowsAtOnceAndClosedCountsAtOnce()
    {
        var drawer = Show(DisplayMode.Animated, compact: true);
        try
        {
            drawer.Set(true);
            Assert.True(drawer.IsOpen);
            Assert.Contains("drawer", window.Classes); // the slide starts with the drawer shown
            Assert.True(sidebar.IsHitTestVisible);

            drawer.Set(false);
            Assert.False(drawer.IsOpen); // closed counts before it has slid away
            Assert.False(sidebar.IsHitTestVisible);
        }
        finally
        {
            window.Close();
        }
    }
}
