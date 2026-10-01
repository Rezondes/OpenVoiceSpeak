using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Client.Views;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client;

/// <summary>Package 104: pages and tabs change with movement in the animated display.</summary>
public sealed class PageMotionTests : IDisposable
{
    readonly string dir = Directory.CreateTempSubdirectory("ovs-pages-").FullName;

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

    static void Settle(int milliseconds)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < milliseconds)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(1); // leaves the CPU to the timing tests running beside
        }
    }

    /// <summary>Lets the clock run until the condition holds (at most 2 s): under load a frame may take its time.</summary>
    static void Until(Func<bool> done)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && watch.ElapsedMilliseconds < 2000)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(1); // leaves the CPU to the timing tests running beside
        }
    }

    static void Frame()
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    MainWindow Open(out MainViewModel vm, DisplayMode display = DisplayMode.Animated, double height = 700)
    {
        new ClientSettings { Display = display }.Save(dir);
        vm = new MainViewModel(dir, a => Dispatcher.UIThread.Post(a), useAudioDevices: false);
        var main = new MainWindow { DataContext = vm, Width = 1100, Height = height };
        main.Show();
        vm.Server = FakeServers.Admin();
        Settle(400);
        Motion.Apply(main, display); // the display is one for the app: pin it against what other tests left queued
        return main;
    }

    static Panel Host(MainWindow main) => main.FindControl<Panel>("PageHost")!;
    static double OffsetX(Visual v) => v.RenderTransform?.Value.M31 ?? 0;
    static bool PageGhostShown(MainWindow main) => main.FindControl<Border>("PageGhost")!.IsVisible;

    static IEnumerable<Border> Ghosts(Visual main, string kind) =>
        OverlayLayer.GetOverlayLayer(main)!.Children.OfType<Border>().Where(b => b.Classes.Contains(kind));

    [AvaloniaFact]
    public void OpenSettings_SlidesInFromRight_CloseReverses()
    {
        var main = Open(out var vm);
        vm.OpenSettings();
        Frame();
        Assert.True(PageGhostShown(main)); // the home page leaves as a picture
        Assert.True(OffsetX(Host(main)) > 0 && Host(main).Opacity < 1, "the settings come in from the right");
        Settle(450);
        Assert.False(PageGhostShown(main));
        Assert.Equal((0d, 1d), (OffsetX(Host(main)), Host(main).Opacity));

        vm.ClosePage();
        Frame();
        Assert.True(OffsetX(Host(main)) < 0, "going back comes in from the left");
        Settle(450);
        Assert.Equal(0, OffsetX(Host(main)));
        main.Close();
    }

    [AvaloniaFact]
    public async Task RapidSwitch_EndsClean()
    {
        var main = Open(out var vm);
        vm.OpenSettings();
        Frame();
        vm.ClosePage();
        Frame();
        await vm.OpenAdminAsync();
        Until(() => !PageGhostShown(main));
        Settle(100);
        Assert.False(PageGhostShown(main));
        Assert.Equal((0d, 1d), (OffsetX(Host(main)), Host(main).Opacity));
        Assert.True(vm.IsAdminPage);
        Assert.True(main.GetVisualDescendants().OfType<AdminView>().Single().IsEffectivelyVisible);
        main.Close();
    }

    /// <summary>
    /// The pages behave as before in both displays: a reopened administration starts on its first tab again (a new
    /// page model), the animation only moves a picture.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(DisplayMode.Animated)]
    [InlineData(DisplayMode.Simplified)]
    public async Task PageSwitch_KeepsState(DisplayMode display)
    {
        var main = Open(out var vm, display);
        await vm.OpenAdminAsync();
        Settle(400);
        var tabs = main.GetVisualDescendants().OfType<AdminView>().Single().FindControl<TabControl>("Tabs")!;
        tabs.SelectedIndex = 1;
        vm.ClosePage();
        Settle(400);
        await vm.OpenAdminAsync();
        Settle(400);
        Assert.Equal(0, tabs.SelectedIndex);
        main.Close();
    }

    [AvaloniaFact]
    public async Task AdminAndChatTabs_FadeIn_MarkFlies()
    {
        var main = Open(out var vm);
        await vm.OpenAdminAsync();
        Settle(400);
        var tabs = main.GetVisualDescendants().OfType<AdminView>().Single().FindControl<TabControl>("Tabs")!;
        tabs.SelectedIndex = 1;
        Frame();
        var content = tabs.GetVisualDescendants().OfType<Control>().First(c => c.Name == "PART_SelectedContentHost");
        Assert.True(content.Opacity < 1 && OffsetX(content) > 0, "the content comes in from the side of the new tab");
        Assert.Single(Ghosts(main, PageMotion.PipeGhostClass));
        var pipe = ((Control)tabs.ContainerFromIndex(1)!).GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_SelectedPipe");
        Assert.Equal(0, pipe.Opacity); // the mark is on its way
        Settle(450);
        Assert.Empty(Ghosts(main, PageMotion.PipeGhostClass));
        Assert.Equal((1d, 1d), (content.Opacity, pipe.Opacity));

        vm.ClosePage();
        Settle(450);
        var chat = main.GetVisualDescendants().OfType<ChatView>().Single();
        var history = chat.FindControl<ScrollViewer>("Scroller")!;
        vm.Chat!.Selected = vm.Chat.Tabs[^1];
        Frame();
        Assert.True(history.Opacity < 1, "the chat history fades in");
        Assert.Single(Ghosts(main, PageMotion.PipeGhostClass));
        Settle(450);
        Assert.Equal(1, history.Opacity);
        main.Close();
    }

    [AvaloniaFact]
    public void SettingsSections_FadeUpOnce()
    {
        var main = Open(out var vm);
        vm.OpenSettings();
        Settle(500);
        var scroll = main.GetVisualDescendants().OfType<SettingsView>().Single().GetVisualDescendants().OfType<ScrollViewer>().First(s => PageMotion.GetReveal(s));
        var sections = ((Panel)scroll.Content!).Children;
        Assert.Equal(1, sections[0].Opacity);
        Assert.Equal(0, sections[^1].Opacity); // below the fold, it waits

        scroll.Offset = new Vector(0, scroll.Extent.Height);
        Frame();
        Frame();
        Assert.True(sections[^1].Opacity < 1, "it fades up as it comes into view");
        Settle(400);
        Assert.Equal(1, sections[^1].Opacity);

        scroll.Offset = default;
        Settle(100);
        scroll.Offset = new Vector(0, scroll.Extent.Height);
        Frame();
        Assert.Equal(1, sections[^1].Opacity); // only once
        main.Close();
    }

    [AvaloniaFact]
    public async Task Simplified_Instant()
    {
        var main = Open(out var vm, DisplayMode.Simplified);
        vm.OpenSettings();
        Frame();
        Assert.False(PageGhostShown(main));
        Assert.Equal((0d, 1d), (OffsetX(Host(main)), Host(main).Opacity));
        var scroll = main.GetVisualDescendants().OfType<SettingsView>().Single().GetVisualDescendants().OfType<ScrollViewer>().First(s => PageMotion.GetReveal(s));
        Assert.All(((Panel)scroll.Content!).Children, s => Assert.Equal(1, s.Opacity));
        vm.ClosePage();
        await vm.OpenAdminAsync();
        Frame();
        var tabs = main.GetVisualDescendants().OfType<AdminView>().Single().FindControl<TabControl>("Tabs")!;
        tabs.SelectedIndex = 1;
        Frame();
        Assert.Empty(Ghosts(main, PageMotion.PipeGhostClass));
        main.Close();
    }

    /// <summary>Switching to the simplified display on the settings page shows the sections still waiting below.</summary>
    [AvaloniaFact]
    public void SwitchToSimplified_ShowsWaitingSections()
    {
        var main = Open(out var vm);
        vm.OpenSettings();
        Settle(500);
        var scroll = main.GetVisualDescendants().OfType<SettingsView>().Single().GetVisualDescendants().OfType<ScrollViewer>().First(s => PageMotion.GetReveal(s));
        var last = ((Panel)scroll.Content!).Children[^1];
        Assert.Equal(0, last.Opacity);
        vm.SettingsPage!.SelectedDisplay = SettingsViewModel.Displays.Single(d => d.Value == DisplayMode.Simplified);
        Frame();
        Assert.Equal(1, last.Opacity);
        main.Close();
    }

    /// <summary>
    /// The picture shows the page as it was: it is taken before the page model goes (closing clears it first), and
    /// Settings straight to the administration leaves the settings, not the home page shown for a moment in between.
    /// </summary>
    [AvaloniaFact]
    public async Task Picture_IsTakenWhileThePageIsStillThere()
    {
        var main = Open(out var vm);
        var layer = main.FindControl<Border>("PageGhost")!;
        var seen = new List<(Page Page, bool HasSettings)>();
        layer.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.IsVisibleProperty && layer.IsVisible) seen.Add((vm.Page, vm.SettingsPage is not null));
        };
        vm.OpenSettings();
        Settle(450);
        seen.Clear();
        vm.ClosePage();
        Assert.Equal([(Page.Settings, true)], seen);
        Settle(450);

        vm.OpenSettings();
        Settle(450);
        seen.Clear();
        await vm.OpenAdminAsync(); // closes the settings, then opens the administration: one switch
        Assert.Equal([(Page.Settings, true)], seen);
        Frame();
        Assert.True(OffsetX(Host(main)) > 0, "forward: the administration comes in from the right");
        Settle(450);
        Assert.False(PageGhostShown(main));
        main.Close();
    }

    /// <summary>The picture lies right above the pages: under the drawer and the dialogs, inside the main area.</summary>
    [AvaloniaFact]
    public void Picture_StaysBelowDrawerAndDialogs()
    {
        var main = Open(out _);
        var layer = main.FindControl<Border>("PageGhost")!;
        var area = (Panel)layer.Parent!;
        Assert.Same(Host(main).Parent, area);
        Assert.True(area.ClipToBounds);
        var root = (Panel)area.Parent!;
        Assert.True(root.Children.IndexOf(area) < root.Children.IndexOf(main.FindControl<Border>("DrawerScrim")!));
        Assert.True(root.Children.IndexOf(area) < root.Children.IndexOf(main.Overlay));
        main.Close();
    }
}
