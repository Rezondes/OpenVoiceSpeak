using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OVS.Client.Settings;
using OVS.Client.Views;

namespace OVS.Tests.Client;

/// <summary>Package 100: hover, press and focus effects exist only in the animated display.</summary>
public sealed class MotionStyleTests : IDisposable
{
    public void Dispose() => Motion.IsAnimated = true;

    static Window Host(Control content, bool animated)
    {
        var window = new Window { Width = 400, Height = 300, Content = new StackPanel { Margin = new Thickness(20), Spacing = 10, Children = { content } } };
        window.Show();
        Motion.Apply(window, animated ? DisplayMode.Animated : DisplayMode.Simplified);
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    /// <summary>The animation clock runs on real time: waits until a transition of this length is over.</summary>
    static void Settle(int milliseconds = 350)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < milliseconds)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(1); // leaves the CPU to the timing tests running beside
        }
    }

    static Point Center(Control control, Visual root) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), root)!.Value;

    static Matrix Transform(Visual visual) => visual.RenderTransform?.Value ?? Matrix.Identity;

    static Matrix Expected(string operations) => TransformOperations.Parse(operations).Value;

    static bool Near(Matrix a, Matrix b) =>
        Math.Abs(a.M11 - b.M11) < 0.01 && Math.Abs(a.M12 - b.M12) < 0.01 && Math.Abs(a.M21 - b.M21) < 0.01
        && Math.Abs(a.M22 - b.M22) < 0.01 && Math.Abs(a.M31 - b.M31) < 0.05 && Math.Abs(a.M32 - b.M32) < 0.05;

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Button_Animated_HasHoverAndPressTransitions_SimplifiedHasNone(bool animated)
    {
        var button = new Button { Content = "Speichern", Classes = { "accent" } };
        var window = Host(button, animated);
        // Fluent's own short press transition is part of the simplified look; the animated one is Motion.Fast long
        Assert.Equal(animated, button.Transitions?.OfType<TransformOperationsTransition>().Any(t => t.Duration == Motion.Fast) == true);

        window.MouseMove(Center(button, window));
        Settle();
        Assert.True(Near(animated ? Expected("translateY(-1px)") : Matrix.Identity, Transform(button)), $"hover {Transform(button)}");
        var presenter = button.GetVisualDescendants().OfType<ContentPresenter>().First(p => p.Name == "PART_ContentPresenter");
        Assert.Equal(animated, presenter.BoxShadow.Count > 0 && presenter.BoxShadow[0].Blur > 0); // lifted with a soft shadow

        window.MouseDown(Center(button, window), MouseButton.Left);
        Settle();
        Assert.Equal(animated, Near(Expected("scale(0.96)"), Transform(button)));
        window.MouseUp(Center(button, window), MouseButton.Left);
        Settle();
        Assert.True(Near(animated ? Expected("translateY(-1px)") : Matrix.Identity, Transform(button)), "springs back");
    }

    [AvaloniaFact]
    public void Row_HoverAccentBarGrows()
    {
        var row = new Border { Classes = { "row" }, Height = 30, Width = 200 };
        var current = new Border { Classes = { "row", "current" }, Height = 30, Width = 200 };
        var window = Host(row, animated: true);
        ((StackPanel)window.Content!).Children.Add(current);
        Settle();
        Assert.Equal(3, current.BoxShadow[0].OffsetX); // the current channel keeps its bar
        Assert.True(current.BoxShadow[0].IsInset);
        Assert.Equal(0, row.BoxShadow[0].OffsetX);

        window.MouseMove(Center(row, window));
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        double early = row.BoxShadow[0].OffsetX;
        Settle();
        Assert.True(early > 0 && early < 3, $"the bar grows, it does not jump ({early})");
        Assert.Equal(3, row.BoxShadow[0].OffsetX, 2);
        Assert.Equal(Color.Parse("#2F6FEB"), row.BoxShadow[0].Color);
    }

    [AvaloniaFact]
    public void Row_Simplified_NoBar()
    {
        var row = new Border { Classes = { "row" }, Height = 30, Width = 200 };
        var window = Host(row, animated: false);
        window.MouseMove(Center(row, window));
        Settle();
        Assert.Equal(0, row.BoxShadow.Count);
    }

    static IEnumerable<Border> FocusRings(Window window) =>
        window.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("focusRing"));

    [AvaloniaFact]
    public void FocusRing_KeyboardOnly()
    {
        var first = new Button { Content = "Eins" };
        var second = new Button { Content = "Zwei" };
        var window = Host(first, animated: true);
        ((StackPanel)window.Content!).Children.Add(second);
        Dispatcher.UIThread.RunJobs();

        first.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
        var ring = Assert.Single(FocusRings(window));
        double start = ring.Opacity;
        Settle();
        Assert.True(start < 1, "the ring fades in");
        Assert.Equal(1, ring.Opacity, 2);
        Assert.Equal(new Thickness(-2), ring.Margin); // grown 2 px around the control

        second.Focus(NavigationMethod.Pointer);
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(FocusRings(window), r => r.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void FocusRing_Simplified_IsTheOldOne()
    {
        var button = new Button { Content = "Eins" };
        var window = Host(button, animated: false);
        button.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(FocusRings(window));
    }

    [AvaloniaFact]
    public void Inputs_HaveStateTransitions()
    {
        var text = new TextBox();
        var check = new CheckBox { Content = "Ton aus" };
        var slider = new Slider { Width = 200 };
        var window = Host(text, animated: true);
        var panel = (StackPanel)window.Content!;
        panel.Children.Add(check);
        panel.Children.Add(slider);
        Dispatcher.UIThread.RunJobs();

        var frame = text.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_BorderElement");
        Assert.Contains(frame.Transitions!, t => t is BrushTransition { Property.Name: "BorderBrush" });
        var combo = new ComboBox { Width = 200 };
        panel.Children.Add(combo);
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(combo.GetVisualDescendants().OfType<Border>().First(b => b.Name == "Background").Transitions!, t => t is BrushTransition { Property.Name: "BorderBrush" });
        Assert.Contains(check.GetVisualDescendants().OfType<Border>().First(b => b.Name == "NormalRectangle").Transitions!, t => t is BrushTransition { Property.Name: "Background" });

        var glyph = check.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().First(p => p.Name == "CheckGlyph");
        Assert.True(Near(Expected("scale(0.4)"), Transform(glyph)));
        check.IsChecked = true;
        Settle();
        Assert.True(Near(Matrix.Identity, Transform(glyph)), "the check mark pops to full size");

        var thumb = slider.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Thumb>().First();
        window.MouseMove(Center(slider, window));
        Settle();
        Assert.True(Near(Expected("scale(1.2)"), Transform(thumb)), "the thumb grows on hover");
    }

    [AvaloniaTheory]
    [InlineData("spin", "rotate(30deg)")]
    [InlineData("turn", "rotate(90deg)")]
    [InlineData("nudgeUp", "translateY(-2px)")]
    [InlineData("nudgeDown", "translateY(2px)")]
    [InlineData("nudgeRight", "translateX(2px)")]
    [InlineData("sync", "rotate(180deg)")]
    public void IconButtons_TurnOnHover(string kind, string expected)
    {
        var icon = new PathIcon { Classes = { kind }, Data = Geometry.Parse("M0,0 L10,10") };
        var button = new Button { Classes = { "icon" }, Content = icon };
        var window = Host(button, animated: true);
        window.MouseMove(Center(button, window));
        Settle();
        Assert.True(Near(Expected(expected), Transform(icon)), $"{kind}: {Transform(icon)}");

        Motion.Apply(window, DisplayMode.Simplified);
        Settle();
        Assert.True(Near(Matrix.Identity, Transform(icon)), "nothing turns in the simplified display");
    }

    static ContentPresenter Presenter(Control control) =>
        control.GetVisualDescendants().OfType<ContentPresenter>().First(p => p.Name == "PART_ContentPresenter");

    static double Bar(BoxShadows shadows) => shadows.Count > 0 && shadows[0].IsInset ? shadows[0].OffsetX : 0;

    [AvaloniaFact]
    public void Bookmarks_ListItems_SoundRows_GetTheBar_LogLinesDoNot()
    {
        var bookmark = new Button { Classes = { "bookmark" }, Content = "voice.example.org", Width = 200 };
        var groups = new ListBox { ItemsSource = new[] { "Gast", "Admin" }, Width = 200 };
        var lines = new ListBox { Classes = { "logLines" }, ItemsSource = new[] { "12:00 Start" }, Width = 200 };
        var sound = new Border { Classes = { "soundRow" }, Height = 30, Width = 200 };
        var window = Host(bookmark, animated: true);
        var panel = (StackPanel)window.Content!;
        panel.Children.Add(groups);
        panel.Children.Add(lines);
        panel.Children.Add(sound);
        Dispatcher.UIThread.RunJobs();
        var rest = sound.Background;

        window.MouseMove(Center(bookmark, window));
        Settle();
        Assert.Equal(3, Bar(Presenter(bookmark).BoxShadow), 2);
        Assert.True(Near(Matrix.Identity, Transform(bookmark)), "a bookmark is a row, it does not lift");

        var gast = groups.ContainerFromIndex(0)!;
        window.MouseMove(Center(gast, window));
        Settle();
        Assert.Equal(3, Bar(Presenter(gast).BoxShadow), 2);
        groups.SelectedIndex = 1;
        Settle();
        Assert.Equal(3, Bar(Presenter(groups.ContainerFromIndex(1)!).BoxShadow), 2); // the selected entry keeps it

        var line = lines.ContainerFromIndex(0)!;
        window.MouseMove(Center(line, window));
        Settle();
        Assert.Equal(0, Bar(Presenter(line).BoxShadow));

        window.MouseMove(Center(sound, window));
        Settle();
        Assert.Equal(3, Bar(sound.BoxShadow), 2);
        Assert.NotEqual(rest, sound.Background); // it lights up, it does not only grow the bar
    }

    [AvaloniaFact]
    public void Tabs_LiftTheirLabelOnHover()
    {
        var tabs = new TabControl { ItemsSource = new[] { new TabItem { Header = "Gruppen" }, new TabItem { Header = "Nutzer" } } };
        var window = Host(tabs, animated: true);
        var second = (TabItem)tabs.ContainerFromIndex(1)!;
        window.MouseMove(Center(second, window));
        Settle();
        Assert.True(Near(Expected("translateY(-1px)"), Transform(second)));
    }
}
