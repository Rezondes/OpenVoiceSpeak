using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace OVS.Client.Views;

/// <summary>
/// Package 104: in the animated display pages and tabs change with movement.
/// <list type="bullet">
/// <item>A page (home, settings, administration) leaves as a picture that slides 24 px away and fades, while the new
/// one comes in from the other side; opening goes to the right, going back to the left. The pages stay what they are
/// (their state, scroll positions, the chosen tab), only a picture moves.</item>
/// <item>Tabs: the selection mark flies from the old tab to the new one, the content fades in from the side the new
/// tab lies on (<see cref="TabsProperty"/>, <see cref="ContentProperty"/>).</item>
/// <item>Long pages: their sections fade up once when they first scroll into view (<see cref="RevealProperty"/>).</item>
/// </list>
/// </summary>
public static class PageMotion
{
    public const double Slide = 24, TabSlide = 12, RevealRise = 16;
    public const string PageGhostClass = "pageGhost", PipeGhostClass = "pipeGhost";

    // ---- pages ----

    sealed class Switching
    {
        public CancellationTokenSource Cancel = new();
        public bool Pending;
    }

    static readonly ConditionalWeakTable<Border, Switching> switching = [];

    /// <summary>
    /// The page is about to change (call it before anything of the page goes, its model included): a picture of it
    /// covers the page area at once, so nothing of the change shows. Once the change is complete (the end of this round
    /// of the dispatcher, several changes in a row count as one) the picture slides away and the new page comes in.
    /// </summary>
    /// <param name="layer">An empty border right above <paramref name="host"/> that holds the picture.</param>
    /// <param name="motion">Asked at the end: where the new content comes in from and where the picture goes, or null
    /// when nothing changed after all.</param>
    public static void Leave(Control host, Border layer, Func<(Vector In, Vector Away)?> motion)
    {
        if (!Motion.IsAnimated || host.Bounds.Width <= 0) return;
        var state = switching.GetValue(layer, _ => new Switching());
        if (state.Pending) return; // the picture of the page that was there at the start of the change already lies on top
        Stop(host, layer); // a switch in the middle of a switch: the old one ends at once
        layer.Child = new Image { Source = ReorderDrag.Snapshot(host), Width = host.Bounds.Width, Height = host.Bounds.Height };
        layer.Classes.Set(PageGhostClass, true);
        layer.IsVisible = true;
        state.Pending = true;
        Dispatcher.UIThread.Post(() =>
        {
            state.Pending = false;
            if (motion() is { } way) Play(host, layer, state, way.In, way.Away);
            else Stop(host, layer);
        }, DispatcherPriority.Background);
    }

    /// <summary>Opening a page comes in from the right, going back from the left.</summary>
    public static (Vector In, Vector Away) Sideways(bool forward) =>
        forward ? (new Vector(Slide, 0), new Vector(-Slide, 0)) : (new Vector(-Slide, 0), new Vector(Slide, 0));

    static async void Play(Control host, Border layer, Switching state, Vector from, Vector away)
    {
        if (layer.Child is not Image image) return;
        var token = state.Cancel.Token;
        var outgoing = Run(image, token, (Visual.OpacityProperty, 1d, 0d), (TranslateTransform.XProperty, 0d, away.X), (TranslateTransform.YProperty, 0d, away.Y));
        var incoming = Run(host, token, (Visual.OpacityProperty, 0d, 1d), (TranslateTransform.XProperty, from.X, 0d), (TranslateTransform.YProperty, from.Y, 0d));
        await Task.WhenAll(outgoing, incoming);
        if (layer.Child == image) Stop(host, layer);
    }

    static void Stop(Control host, Border layer)
    {
        if (!switching.TryGetValue(layer, out var state)) return;
        state.Cancel.Cancel();
        state.Cancel = new CancellationTokenSource();
        ((layer.Child as Image)?.Source as IDisposable)?.Dispose();
        layer.Child = null;
        layer.IsVisible = false;
        layer.Classes.Set(PageGhostClass, false);
    }

    static Task Run(Animatable target, CancellationToken token, params (AvaloniaProperty Property, double From, double To)[] values)
    {
        var start = new KeyFrame { Cue = new Cue(0) };
        var end = new KeyFrame { Cue = new Cue(1) };
        foreach (var (property, from, to) in values)
        {
            start.Setters.Add(new Setter(property, from));
            end.Setters.Add(new Setter(property, to));
        }
        return new Animation { Duration = Motion.Slow, Easing = Motion.Ease, FillMode = FillMode.Backward, Children = { start, end } }.RunAsync(target, token);
    }

    // ---- tabs ----

    /// <summary>On a TabControl or TabStrip: the selection mark flies to the new tab, the content fades in.</summary>
    public static readonly AttachedProperty<bool> TabsProperty =
        AvaloniaProperty.RegisterAttached<SelectingItemsControl, bool>("Tabs", typeof(PageMotion));

    /// <summary>What shows the selected tab's content, when it is not the TabControl's own (the chat history).</summary>
    public static readonly AttachedProperty<Control?> ContentProperty =
        AvaloniaProperty.RegisterAttached<SelectingItemsControl, Control?>("Content", typeof(PageMotion));

    public static bool GetTabs(SelectingItemsControl tabs) => tabs.GetValue(TabsProperty);
    public static void SetTabs(SelectingItemsControl tabs, bool value) => tabs.SetValue(TabsProperty, value);
    public static Control? GetContent(SelectingItemsControl tabs) => tabs.GetValue(ContentProperty);
    public static void SetContent(SelectingItemsControl tabs, Control? value) => tabs.SetValue(ContentProperty, value);

    static readonly ConditionalWeakTable<Control, CancellationTokenSource> fading = [];

    static PageMotion()
    {
        TabsProperty.Changed.AddClassHandler<SelectingItemsControl>((tabs, e) =>
        {
            if (e.GetNewValue<bool>()) tabs.SelectionChanged += OnTabChanged;
        });
        RevealProperty.Changed.AddClassHandler<ScrollViewer>((scroll, e) =>
        {
            if (e.GetNewValue<bool>()) _ = new Revealer(scroll);
        });
    }

    static Border? Pipe(Control? tab) => tab?.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "PART_SelectedPipe");

    static void OnTabChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!Motion.IsAnimated || sender is not SelectingItemsControl tabs || e.RemovedItems.Count == 0 || e.AddedItems.Count == 0
            || e.RemovedItems[0] is not { } removed || !tabs.IsEffectivelyVisible)
            return;
        int from = tabs.Items.IndexOf(removed), to = tabs.SelectedIndex;
        // the mark: from the old tab's place to the new tab, which shows its own only once it has arrived
        var oldPipe = Pipe(tabs.ContainerFromItem(removed));
        var newPipe = Pipe(tabs.ContainerFromIndex(to));
        // both ends in view (the narrow chat tab strip scrolls); the mark takes the new tab's width on the way
        if (oldPipe is not null && newPipe is not null && OverlayLayer.GetOverlayLayer(tabs) is { } overlay
            && Seen(tabs.ContainerFromItem(removed), tabs, overlay) is not null && Seen(tabs.ContainerFromIndex(to), tabs, overlay) is not null
            && oldPipe.TranslatePoint(default, overlay) is { } corner)
        {
            var start = new Rect(corner, oldPipe.Bounds.Size);
            var mark = new Border { Classes = { PipeGhostClass }, CornerRadius = oldPipe.CornerRadius };
            mark.Bind(Border.BackgroundProperty, mark.GetResourceObservable("SystemControlHighlightAccentBrush"));
            newPipe.Opacity = 0;
            _ = Arrive(newPipe, FlyGhost.Fly(overlay, mark, start, () => newPipe.TranslatePoint(default, overlay) is { } at ? new Rect(at, newPipe.Bounds.Size) : null));
        }
        // the content: from the side the new tab lies on
        var content = GetContent(tabs) ?? tabs.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Name == "PART_SelectedContentHost");
        if (content is null) return;
        if (fading.TryGetValue(content, out var running)) running.Cancel();
        var cancel = new CancellationTokenSource();
        fading.AddOrUpdate(content, cancel);
        double side = to >= from ? TabSlide : -TabSlide;
        _ = new Animation
        {
            Duration = Motion.Normal,
            Easing = Motion.Ease,
            FillMode = FillMode.Backward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 0d), new Setter(TranslateTransform.XProperty, side) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 1d), new Setter(TranslateTransform.XProperty, 0d) } },
            },
        }.RunAsync(content, cancel.Token);
    }

    /// <summary>The tab's rectangle in the overlay when it lies inside what the tab strip shows.</summary>
    static Rect? Seen(Control? tab, Control tabs, Visual overlay)
    {
        if (tab is null || tab.TranslatePoint(default, overlay) is not { } at || tab.Bounds.Width <= 0) return null;
        var rect = new Rect(at, tab.Bounds.Size);
        var shown = tabs.FindAncestorOfType<ScrollViewer>() is { } scroll && scroll.TranslatePoint(default, overlay) is { } corner
            ? new Rect(corner, scroll.Bounds.Size)
            : tabs.TranslatePoint(default, overlay) is { } origin ? new Rect(origin, tabs.Bounds.Size) : rect;
        return shown.Contains(rect.Center) ? rect : null;
    }

    static async Task Arrive(Control pipe, Task flight)
    {
        await flight;
        pipe.ClearValue(Visual.OpacityProperty);
    }

    // ---- sections that fade up once ----

    /// <summary>On a page's ScrollViewer: the sections of its content fade up once when they first come into view.</summary>
    public static readonly AttachedProperty<bool> RevealProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, bool>("Reveal", typeof(PageMotion));

    public static bool GetReveal(ScrollViewer scroll) => scroll.GetValue(RevealProperty);
    public static void SetReveal(ScrollViewer scroll, bool value) => scroll.SetValue(RevealProperty, value);

    sealed class Revealer
    {
        readonly ScrollViewer scroll;
        readonly HashSet<Control> shown = [];

        public Revealer(ScrollViewer scroll)
        {
            this.scroll = scroll;
            scroll.ScrollChanged += (_, _) => Look();
            scroll.LayoutUpdated += (_, _) => Look();
            scroll.AttachedToVisualTree += (_, _) => Motion.Changed += Look;
            scroll.DetachedFromVisualTree += (_, _) => Motion.Changed -= Look;
        }

        IEnumerable<Control> Sections() => (scroll.Content as Panel)?.Children ?? (IEnumerable<Control>)[];

        void Look()
        {
            if (!scroll.IsEffectivelyVisible) return;
            foreach (var section in Sections())
            {
                if (shown.Contains(section)) continue;
                if (!Motion.IsAnimated)
                {
                    section.ClearValue(Visual.OpacityProperty); // the simplified display shows everything as it is
                    shown.Add(section); // and back in the animated display it is not shown a second time
                    continue;
                }
                if (section.TranslatePoint(default, scroll) is not { } at || section.Bounds.Height <= 0) continue;
                if (at.Y >= scroll.Viewport.Height)
                {
                    section.Opacity = 0; // waits below the fold
                    continue;
                }
                shown.Add(section);
                section.ClearValue(Visual.OpacityProperty);
                _ = new Animation
                {
                    Duration = Motion.Normal,
                    Easing = Motion.Ease,
                    FillMode = FillMode.Backward,
                    Children =
                    {
                        new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 0d), new Setter(TranslateTransform.YProperty, RevealRise) } },
                        new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 1d), new Setter(TranslateTransform.YProperty, 0d) } },
                    },
                }.RunAsync(section);
            }
        }
    }
}
