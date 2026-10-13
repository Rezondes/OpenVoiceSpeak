using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OVS.Client.Animations;
using OVS.Client.Models;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Client.Views;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Animations;

/// <summary>Package 104: pages and tabs change with movement in the animated display.</summary>
[Collection(MotionCollection.Name)]
public sealed class PageMotionTests() : TempDirectoryTest("ovs-pages-", quiet: true)
{
    static void Frame()
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    MainWindow Open(DisplayMode display = DisplayMode.Animated, double height = 700)
    {
        new ClientSettings { Display = display }.Save(dir);
        var viewModel = MainViewModelSetup.Create(dir, action => Dispatcher.UIThread.Post(action));
        var main = new MainWindow { DataContext = viewModel, Width = 1100, Height = height };
        main.Closed += (_, _) => { _ = viewModel.DisposeAsync().AsTask(); }; // the window owns its view model
        main.Show();
        MainViewModelSetup.ShowServer(viewModel, FakeServers.Admin());
        MotionWait.Settle(700); // connecting builds the tree up (Package 106)
        Motion.Apply(main, display);
        MotionWait.Connected(main); // under load that takes longer: no test acts while it still moves
        return main;
    }

    /// <summary>The page change is over: the picture of the old page has gone.</summary>
    static void Switched(MainWindow main) => MotionWait.Until(() => !PageGhostShown(main));

    static Panel Host(MainWindow main) => main.FindControl<Panel>("PageHost")!;
    static bool PageGhostShown(MainWindow main) => main.FindControl<Border>("PageGhost")!.IsVisible;

    static IEnumerable<Border> Ghosts(Visual main, string kind) =>
        OverlayLayer.GetOverlayLayer(main)!.Children.OfType<Border>().Where(border => border.Classes.Contains(kind));

    [AvaloniaFact]
    public void OpenSettings_SlidesInFromRight_CloseReverses()
    {
        var main = Open();
        var viewModel = (MainViewModel)main.DataContext!;
        // every value as it happens: under load one frame can outlast the whole change
        var slides = MotionWait.Record(Host(main), MotionWait.OffsetX);
        var fades = MotionWait.Record(Host(main), visual => visual.Opacity);
        viewModel.PageFlow.OpenSettings();
        Frame();
        Assert.True(PageGhostShown(main)); // the home page leaves as a picture
        MotionWait.Eventually(() =>
        {
            Assert.False(PageGhostShown(main));
            Assert.Equal((0d, 1d), (MotionWait.OffsetX(Host(main)), Host(main).Opacity));
        });
        Assert.True(slides.Any(slide => slide > 0) && fades.Any(fade => fade < 1), "the settings come in from the right");

        slides.Clear();
        viewModel.PageFlow.CloseWithoutAsking();
        MotionWait.Eventually(() =>
        {
            Assert.True(slides.Any(slide => slide < 0), "going back comes in from the left");
            Assert.False(PageGhostShown(main));
            Assert.Equal(0, MotionWait.OffsetX(Host(main)));
        });
        main.Close();
    }

    [AvaloniaFact]
    public async Task PageSwitch_Rapid_EndsClean()
    {
        var main = Open();
        var viewModel = (MainViewModel)main.DataContext!;
        viewModel.PageFlow.OpenSettings();
        Frame();
        viewModel.PageFlow.CloseWithoutAsking();
        Frame();
        await viewModel.PageFlow.OpenAdminAsync();
        MotionWait.Eventually(() =>
        {
            Assert.False(PageGhostShown(main));
            Assert.Equal((0d, 1d), (MotionWait.OffsetX(Host(main)), Host(main).Opacity));
        });
        Assert.True(viewModel.PageFlow.IsAdminPage);
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
    public async Task PageSwitch_Animated_KeepsState(DisplayMode display)
    {
        var main = Open(display);
        var viewModel = (MainViewModel)main.DataContext!;
        await viewModel.PageFlow.OpenAdminAsync();
        MotionWait.Settle(400);
        var tabs = main.GetVisualDescendants().OfType<AdminView>().Single().FindControl<TabControl>("Tabs")!;
        tabs.SelectedIndex = 1;
        viewModel.PageFlow.CloseWithoutAsking();
        MotionWait.Settle(400);
        await viewModel.PageFlow.OpenAdminAsync();
        MotionWait.Settle(400);
        Assert.Equal(0, tabs.SelectedIndex);
        main.Close();
    }

    [AvaloniaFact]
    public async Task AdminAndChatTabs_FadeIn_MarkFlies()
    {
        var main = Open();
        var viewModel = (MainViewModel)main.DataContext!;
        await viewModel.PageFlow.OpenAdminAsync();
        MotionWait.Settle(400);
        Switched(main);
        var tabs = main.GetVisualDescendants().OfType<AdminView>().Single().FindControl<TabControl>("Tabs")!;
        // every mark put in the overlay and every value as it happens: under load one frame can outlast the whole change
        int marks = 0;
        OverlayLayer.GetOverlayLayer(main)!.Children.CollectionChanged += (_, args) =>
            marks += args.NewItems?.OfType<Border>().Count(border => border.Classes.Contains(PageMotion.PipeGhostClass)) ?? 0;
        var content = tabs.GetVisualDescendants().OfType<Control>().First(control => control.Name == "PART_SelectedContentHost");
        var fades = MotionWait.Record(content, visual => visual.Opacity);
        var slides = MotionWait.Record(content, MotionWait.OffsetX);
        var pipe = ((Control)tabs.ContainerFromIndex(1)!).GetVisualDescendants().OfType<Border>().First(border => border.Name == "PART_SelectedPipe");
        tabs.SelectedIndex = 1;
        Assert.Equal(0, pipe.Opacity); // the mark is on its way
        MotionWait.Until(() => marks > 0);
        Assert.Equal(1, marks);
        MotionWait.Eventually(() =>
        {
            Assert.True(fades.Any(fade => fade < 1) && slides.Any(slide => slide > 0), "the content comes in from the side of the new tab");
            Assert.Empty(Ghosts(main, PageMotion.PipeGhostClass));
            Assert.Equal((1d, 1d), (content.Opacity, pipe.Opacity));
        });

        viewModel.PageFlow.CloseWithoutAsking();
        MotionWait.Settle(450);
        Switched(main);
        var chat = main.GetVisualDescendants().OfType<ChatView>().Single();
        var history = chat.FindControl<ScrollViewer>("Scroller")!;
        fades = MotionWait.Record(history, visual => visual.Opacity);
        marks = 0;
        viewModel.Chat!.Selected = viewModel.Chat.Tabs[^1];
        MotionWait.Until(() => marks > 0);
        Assert.Equal(1, marks);
        MotionWait.Eventually(() =>
        {
            Assert.True(fades.Any(fade => fade < 1), "the chat history fades in");
            Assert.Equal(1, history.Opacity);
        });
        main.Close();
    }

    [AvaloniaFact]
    public async Task TabChange_ListInsideTab_NotAnimated()
    {
        var main = Open();
        var viewModel = (MainViewModel)main.DataContext!;
        await viewModel.PageFlow.OpenAdminAsync();
        MotionWait.Settle(400);
        var admin = main.GetVisualDescendants().OfType<AdminView>().Single();
        var tabs = admin.FindControl<TabControl>("Tabs")!;
        var groups = admin.FindControl<ListBox>("GroupRows")!;
        Assert.True(groups.ItemCount >= 2);
        groups.SelectedIndex = 0;
        MotionWait.Settle(400);
        groups.SelectedIndex = 1; // its SelectionChanged bubbles up to the tabs
        Frame();
        var content = tabs.GetVisualDescendants().OfType<Control>().First(control => control.Name == "PART_SelectedContentHost");
        Assert.Equal((1d, 0d), (content.Opacity, MotionWait.OffsetX(content))); // the tab's content stays where it is
        Assert.Empty(Ghosts(main, PageMotion.PipeGhostClass));
        main.Close();
    }

    [AvaloniaFact]
    public void PagePicture_TransitionEnded_NeverComesBack()
    {
        var main = Open();
        var viewModel = (MainViewModel)main.DataContext!;
        viewModel.PageFlow.OpenSettings();
        var layer = main.FindControl<Border>("PageGhost")!;
        double last = 1;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        // a while after it has gone, and as long as it is there (under load the change takes longer)
        while (watch.ElapsedMilliseconds < 1000 || PageGhostShown(main) && watch.ElapsedMilliseconds < 5000)
        {
            Frame();
            if (layer.Child is Image picture)
            {
                Assert.True(picture.Opacity <= last + 0.001, $"the old page's picture fades and stays faded ({picture.Opacity} after {last})");
                last = picture.Opacity;
            }
            MotionWait.Pause();
        }
        Assert.False(PageGhostShown(main));
        main.Close();
    }

    [AvaloniaFact]
    public void SettingsSections_Opened_FadeUpOnce()
    {
        var main = Open();
        var viewModel = (MainViewModel)main.DataContext!;
        viewModel.PageFlow.OpenSettings();
        MotionWait.Settle(500);
        Switched(main);
        var scroll = main.GetVisualDescendants().OfType<SettingsView>().Single().GetVisualDescendants().OfType<ScrollViewer>().First(scrollViewer => PageMotion.GetReveal(scrollViewer));
        var sections = ((Panel)scroll.Content!).Children;
        MotionWait.Eventually(() =>
        {
            Assert.Equal(1, sections[0].Opacity);
            Assert.Equal(0, sections[^1].Opacity); // below the fold, it waits
        });

        var fades = MotionWait.Record(sections[^1], visual => visual.Opacity); // as it happens: under load one frame can outlast the fade
        scroll.Offset = new Vector(0, scroll.Extent.Height);
        MotionWait.Eventually(() => Assert.Equal(1, sections[^1].Opacity));
        Assert.True(fades.Any(fade => fade < 1), "it fades up as it comes into view");

        scroll.Offset = default;
        MotionWait.Settle(100);
        scroll.Offset = new Vector(0, scroll.Extent.Height);
        Frame();
        Assert.Equal(1, sections[^1].Opacity); // only once
        main.Close();
    }

    [AvaloniaFact]
    public async Task Motion_SimplifiedDisplay_Instant()
    {
        var main = Open(DisplayMode.Simplified);
        var viewModel = (MainViewModel)main.DataContext!;
        viewModel.PageFlow.OpenSettings();
        Frame();
        Assert.False(PageGhostShown(main));
        Assert.Equal((0d, 1d), (MotionWait.OffsetX(Host(main)), Host(main).Opacity));
        var scroll = main.GetVisualDescendants().OfType<SettingsView>().Single().GetVisualDescendants().OfType<ScrollViewer>().First(scrollViewer => PageMotion.GetReveal(scrollViewer));
        Assert.All(((Panel)scroll.Content!).Children, control => Assert.Equal(1, control.Opacity));
        viewModel.PageFlow.CloseWithoutAsking();
        await viewModel.PageFlow.OpenAdminAsync();
        Frame();
        var tabs = main.GetVisualDescendants().OfType<AdminView>().Single().FindControl<TabControl>("Tabs")!;
        tabs.SelectedIndex = 1;
        Frame();
        Assert.Empty(Ghosts(main, PageMotion.PipeGhostClass));
        main.Close();
    }

    /// <summary>Switching to the simplified display on the settings page shows the sections still waiting below.</summary>
    [AvaloniaFact]
    public void SettingsSections_SwitchToSimplified_WaitingOnesShown()
    {
        var main = Open();
        var viewModel = (MainViewModel)main.DataContext!;
        viewModel.PageFlow.OpenSettings();
        MotionWait.Settle(500);
        Switched(main);
        var scroll = main.GetVisualDescendants().OfType<SettingsView>().Single().GetVisualDescendants().OfType<ScrollViewer>().First(scrollViewer => PageMotion.GetReveal(scrollViewer));
        var last = ((Panel)scroll.Content!).Children[^1];
        MotionWait.Eventually(() => Assert.Equal(0, last.Opacity));
        viewModel.PageFlow.SettingsPage!.SelectedDisplay = SettingsViewModel.Displays.Single(displayOption => displayOption.Value == DisplayMode.Simplified);
        Frame();
        Assert.Equal(1, last.Opacity);
        main.Close();
    }

    /// <summary>The switch itself shows the waiting sections, before any layout pass would look at them again.</summary>
    [AvaloniaFact]
    public void Reveal_SwitchToSimplified_WaitingSectionShowsAtOnce()
    {
        var sections = Enumerable.Range(0, 3).Select(_ => new Border { Height = 150 }).ToArray();
        var scroll = new ScrollViewer { Content = new StackPanel { Children = { sections[0], sections[1], sections[2] } } };
        PageMotion.SetReveal(scroll, true);
        var window = new Window { Width = 300, Height = 200, Content = scroll };
        int running = AnimationCounter.Running;
        window.Show();
        try
        {
            MotionWait.Until(() => sections[2].Opacity == 0 && AnimationCounter.Running <= running); // the third waits, the first two have faded up
            Assert.Equal(0, sections[2].Opacity); // below the fold: it waits
            Motion.Apply(window, DisplayMode.Simplified);
            Assert.Equal(1, sections[2].Opacity);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// The picture shows the page as it was: it is taken before the page model goes (closing clears it first), and
    /// Settings straight to the administration leaves the settings, not the home page shown for a moment in between.
    /// </summary>
    [AvaloniaFact]
    public async Task PagePicture_PageSwitch_TakenWhileThePageIsStillThere()
    {
        var main = Open();
        var viewModel = (MainViewModel)main.DataContext!;
        var layer = main.FindControl<Border>("PageGhost")!;
        var seen = new List<(Page Page, bool HasSettings)>();
        layer.PropertyChanged += (_, args) =>
        {
            if (args.Property == Visual.IsVisibleProperty && layer.IsVisible) seen.Add((viewModel.PageFlow.Page, viewModel.PageFlow.SettingsPage is not null));
        };
        viewModel.PageFlow.OpenSettings();
        Switched(main);
        seen.Clear();
        viewModel.PageFlow.CloseWithoutAsking();
        Assert.Equal([(Page.Settings, true)], seen);
        Switched(main);

        viewModel.PageFlow.OpenSettings();
        Switched(main);
        seen.Clear();
        var slides = MotionWait.Record(Host(main), MotionWait.OffsetX); // as it happens: under load one frame can outlast the change
        await viewModel.PageFlow.OpenAdminAsync(); // closes the settings, then opens the administration: one switch
        Assert.Equal([(Page.Settings, true)], seen);
        Switched(main);
        Assert.True(slides.Any(slide => slide > 0), "forward: the administration comes in from the right");
        Assert.False(PageGhostShown(main));
        main.Close();
    }

    /// <summary>The picture lies right above the pages: under the drawer and the dialogs, inside the main area.</summary>
    [AvaloniaFact]
    public void PagePicture_DrawerOrDialogOpen_StaysBelow()
    {
        var main = Open();
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
