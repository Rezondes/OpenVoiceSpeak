using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Threading;

namespace OVS.Tests.TestSupport;

/// <summary>Package 68: the layout check itself finds what runs off the visible area.</summary>
public sealed class LayoutAssertTests
{
    [AvaloniaFact]
    public void DetectsClippedButton()
    {
        var button = new Button { Content = "Datei wählen", Width = 120 };
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Children = { new Border { Width = 300 }, button } };
        var window = new Window { Width = 360, Height = 200, Content = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var error = Assert.ThrowsAny<Exception>(() => LayoutAssert.FitsHorizontally(window));
        Assert.Contains("Button", error.Message);
        Assert.Contains("Datei wählen", error.Message);

        button.Width = 50; // inside now, but its text is cut off
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("needs", Assert.ThrowsAny<Exception>(() => LayoutAssert.FitsHorizontally(window)).Message);

        button.Content = "OK";
        Dispatcher.UIThread.RunJobs();
        LayoutAssert.FitsHorizontally(window);
        window.Close();
    }
}
