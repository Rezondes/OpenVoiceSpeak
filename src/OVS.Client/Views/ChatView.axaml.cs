using System.Collections.Specialized;
using System.Diagnostics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OVS.Client.ViewModels;

namespace OVS.Client.Views;

/// <summary>Chat tabs with the composer (Package 32).</summary>
public partial class ChatView : UserControl
{
    /// <summary>Package 107: how far a new message comes from its side; how long a notice's edge glows.</summary>
    public const double Arrive = 16, Lift = 10;
    public const int PulseMs = 600, SentMarkMs = 700;

    /// <summary>True while the list shows its end: new lines then scroll along, otherwise the reader keeps the place.</summary>
    bool atEnd = true;
    ChatViewModel? watched;
    ChatTab? shownTab;
    bool gliding;
    double glidSet; // where the glide put the list last: anything else is the reader's own scrolling
    readonly HashSet<ChatEntry> arriving = new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<ChatTab, System.ComponentModel.PropertyChangedEventHandler> tabsWatched = [];

    public ChatView()
    {
        InitializeComponent();
        // Tunnel: the TextBox would otherwise take Enter as a new line itself.
        Composer.AddHandler(KeyDownEvent, OnComposerKeyDown, RoutingStrategies.Tunnel);
        Scroller.ScrollChanged += OnScrollChanged;
        History.ContainerPrepared += OnEntryShown;
        DataContextChanged += (_, _) => Watch();
        // the pill belongs to the animated display only
        void OnDisplayChanged()
        {
            if (!Motion.IsAnimated) HidePill();
        }
        AttachedToVisualTree += (_, _) => Motion.Changed += OnDisplayChanged;
        DetachedFromVisualTree += (_, _) => Motion.Changed -= OnDisplayChanged;
    }

    void Watch()
    {
        if (watched is not null)
        {
            watched.PropertyChanged -= OnVmPropertyChanged;
            watched.Tabs.CollectionChanged -= OnTabsChanged;
        }
        foreach (var (tab, handler) in tabsWatched) tab.PropertyChanged -= handler; // the last connection's tabs go
        tabsWatched.Clear();
        watched = DataContext as ChatViewModel;
        if (watched is not null)
        {
            watched.PropertyChanged += OnVmPropertyChanged;
            watched.Tabs.CollectionChanged += OnTabsChanged;
            foreach (var tab in watched.Tabs) WatchTab(tab);
        }
        ShowTab(watched?.Selected);
        ScrollToEnd();
    }

    void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ChatViewModel.Selected)) return;
        ShowTab(watched?.Selected);
        ScrollToEnd(); // another tab starts at its newest line
        // Package 79: a tab scrolled out of the strip comes into view when chosen
        Dispatcher.UIThread.Post(() => (watched?.Selected is { } tab ? Tabs.ContainerFromItem(tab) : null)?.BringIntoView(), DispatcherPriority.Background);
    }

    void OnComposerKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers.HasFlag(KeyModifiers.Shift)) return;
        e.Handled = true;
        if (watched?.SendCommand.CanExecute(null) == true) watched.SendCommand.Execute(null);
    }

    void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentDelta.Y != 0 || e.ViewportDelta.Y != 0)
        {
            if (atEnd) Follow(); // content or window changed: follow if the reader was at the end
            return;
        }
        // Only the reader's own scrolling decides whether we follow; it also stops a glide on its way down.
        if (e.OffsetDelta.Y == 0) return;
        if (gliding)
        {
            if (Math.Abs(Scroller.Offset.Y - glidSet) < 1) return;
            gliding = false;
        }
        atEnd = Scroller.Offset.Y >= Scroller.Extent.Height - Scroller.Viewport.Height - 8;
        if (atEnd) HidePill();
    }

    void ScrollToEnd()
    {
        atEnd = true;
        HidePill();
        Dispatcher.UIThread.Post(() => Scroller.ScrollToEnd(), DispatcherPriority.Background);
    }

    // ---- Package 107: the chat moves in the animated display ----

    /// <summary>New lines while at the end: in the animated display the list glides there, otherwise it jumps.</summary>
    void Follow()
    {
        if (!Motion.IsAnimated || TopLevel.GetTopLevel(this) is not { } top)
        {
            Scroller.ScrollToEnd();
            return;
        }
        if (gliding) return; // the running glide follows the end as it grows
        gliding = true;
        double from = glidSet = Scroller.Offset.Y;
        var clock = Stopwatch.StartNew();
        void Frame(TimeSpan _)
        {
            if (!gliding) return; // the reader took over
            if (!Motion.IsAnimated) // the simplified display came meanwhile: straight to the end
            {
                gliding = false;
                Scroller.ScrollToEnd();
                return;
            }
            double end = Math.Max(0, Scroller.Extent.Height - Scroller.Viewport.Height);
            double t = Math.Min(1, clock.Elapsed / Motion.Normal);
            glidSet = t < 1 ? from + (end - from) * Motion.Ease.Ease(t) : end;
            Scroller.Offset = new Vector(Scroller.Offset.X, glidSet);
            if (t < 1) top.RequestAnimationFrame(Frame);
            else gliding = false;
        }
        top.RequestAnimationFrame(Frame);
    }

    void ShowTab(ChatTab? tab)
    {
        if (shownTab is not null) shownTab.Entries.CollectionChanged -= OnEntriesChanged;
        shownTab = tab;
        arriving.Clear(); // a tab's history that shows does not arrive line by line
        if (shownTab is not null) shownTab.Entries.CollectionChanged += OnEntriesChanged;
    }

    void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || e.NewItems is null) return;
        if (!Dispatcher.UIThread.CheckAccess()) return; // the list is only changed on the UI thread in the app
        foreach (ChatEntry entry in e.NewItems)
        {
            if (entry.Sending is { } sending) WatchSending(entry, sending);
            if (!Motion.IsAnimated) continue;
            arriving.Add(entry);
            if (!atEnd && !entry.IsOwn) ShowPill(); // reading further up: nothing scrolls, the pill says there is more
        }
    }

    /// <summary>A new line slides in: one's own from the composer (right, below), anyone else's from the left.</summary>
    void OnEntryShown(object? sender, ContainerPreparedEventArgs e)
    {
        if (e.Container.DataContext is not ChatEntry entry || !arriving.Remove(entry) || !Motion.IsAnimated) return;
        double side = entry.IsOwn ? Arrive : -Arrive;
        _ = new Animation
        {
            Duration = Motion.Normal,
            Easing = Motion.Ease,
            FillMode = FillMode.Backward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 0d), new Setter(TranslateTransform.XProperty, side), new Setter(TranslateTransform.YProperty, Lift) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 1d), new Setter(TranslateTransform.XProperty, 0d), new Setter(TranslateTransform.YProperty, 0d) } },
            },
        }.RunAsync(e.Container);
        if (entry.Notice is NoticeKind.Warning or NoticeKind.Error)
            Dispatcher.UIThread.Post(() => Pulse(e.Container, entry.Notice == NoticeKind.Error ? "Ovs.Danger" : "Ovs.WarningText"), DispatcherPriority.Background);
    }

    /// <summary>A warning or an error glows once on its left edge.</summary>
    static void Pulse(Control container, string colour)
    {
        if (container.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("card")) is not { } card
            || !card.TryFindResource(colour, card.ActualThemeVariant, out var brush) || brush is not ISolidColorBrush { Color: var c })
            return;
        _ = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(PulseMs),
            Easing = Motion.Ease,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Border.BoxShadowProperty, new BoxShadows(new BoxShadow { IsInset = true, OffsetX = 4, Color = c })) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Border.BoxShadowProperty, new BoxShadows(new BoxShadow { IsInset = true, OffsetX = 0, Color = Color.FromArgb(0, c.R, c.G, c.B) })) } },
            },
        }.RunAsync(card);
    }

    /// <summary>
    /// An own message: sent, a check shows in place of the clock and fades; refused, "nicht gesendet" shakes. A waiting
    /// mark ends in two steps (no longer waiting, then maybe an error), so the outcome is read once both are in.
    /// </summary>
    void WatchSending(ChatEntry entry, ChatSending sending)
    {
        bool wasWaiting = sending.IsWaiting, queued = false;
        sending.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(ChatSending.IsWaiting) && e.PropertyName != nameof(ChatSending.IsFailed) || queued) return;
            queued = true;
            Dispatcher.UIThread.Post(() =>
            {
                queued = false;
                bool ended = wasWaiting && !sending.IsWaiting;
                wasWaiting = sending.IsWaiting;
                if (!ended || !Motion.IsAnimated || History.ContainerFromItem(entry) is not { } container) return;
                if (!sending.IsFailed) ShowSent(container);
                else if (container.GetVisualDescendants().OfType<WrapPanel>().FirstOrDefault(w => w.Name == "NotSent") is { } notSent) Motion.Shake(notSent);
            }, DispatcherPriority.Background);
        };
    }

    static async void ShowSent(Control container)
    {
        if (container.GetVisualDescendants().OfType<PathIcon>().FirstOrDefault(i => i.Width == 12 && i.Parent is StackPanel { Orientation: Orientation.Horizontal })?.Parent
            is not StackPanel line || Application.Current?.TryFindResource("Icon.Checkmark", out var check) != true || check is not Geometry geometry)
            return;
        var mark = new PathIcon { Data = geometry, Width = 12, Height = 12, Classes = { "sm", "sentMark" } };
        line.Children.Insert(0, mark);
        await new Animation
        {
            Duration = TimeSpan.FromMilliseconds(SentMarkMs),
            Easing = Motion.Ease,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 1d) } },
                new KeyFrame { Cue = new Cue(0.4), Setters = { new Setter(OpacityProperty, 1d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 0d) } },
            },
        }.RunAsync(mark);
        line.Children.Remove(mark);
    }

    void ShowPill()
    {
        if (NewMessagesPill.IsVisible) return;
        NewMessagesPill.IsVisible = true;
        _ = new Animation
        {
            Duration = Motion.Normal,
            Easing = Motion.Pop,
            FillMode = FillMode.Backward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 0d), new Setter(TranslateTransform.YProperty, 20d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 1d), new Setter(TranslateTransform.YProperty, 0d) } },
            },
        }.RunAsync(NewMessagesPill);
    }

    void HidePill() => NewMessagesPill.IsVisible = false;

    void OnNewMessagesPillClick(object? sender, RoutedEventArgs e)
    {
        HidePill();
        atEnd = true;
        Follow();
    }

    // ---- the unread badge pops in and its count ticks ----

    void OnTabsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (ChatTab tab in e.OldItems ?? Array.Empty<ChatTab>())
            if (tabsWatched.Remove(tab, out var handler)) tab.PropertyChanged -= handler; // a closed private tab
        foreach (ChatTab tab in e.NewItems ?? Array.Empty<ChatTab>()) WatchTab(tab);
    }

    void WatchTab(ChatTab tab)
    {
        if (tabsWatched.ContainsKey(tab)) return;
        int before = tab.Unread;
        System.ComponentModel.PropertyChangedEventHandler handler = (_, e) =>
        {
            if (e.PropertyName != nameof(ChatTab.Unread)) return;
            int now = tab.Unread; // read here, shown on the UI thread
            bool popIn = before == 0;
            if (now > before && Motion.IsAnimated) Dispatcher.UIThread.Post(() => Badge(tab, popIn), DispatcherPriority.Background);
            before = now;
        };
        tabsWatched[tab] = handler;
        tab.PropertyChanged += handler;
    }

    void Badge(ChatTab tab, bool popIn)
    {
        if (Tabs.ContainerFromItem(tab)?.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("count")) is not { } badge) return;
        Animatable target = popIn ? badge : (Animatable?)badge.Child ?? badge;
        var start = new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 0d) } };
        var end = new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 1d) } };
        if (popIn)
        {
            start.Setters.Add(new Setter(ScaleTransform.ScaleXProperty, 0.5));
            start.Setters.Add(new Setter(ScaleTransform.ScaleYProperty, 0.5));
            end.Setters.Add(new Setter(ScaleTransform.ScaleXProperty, 1d));
            end.Setters.Add(new Setter(ScaleTransform.ScaleYProperty, 1d));
        }
        else
        {
            start.Setters.Add(new Setter(TranslateTransform.YProperty, 6d));
            end.Setters.Add(new Setter(TranslateTransform.YProperty, 0d));
        }
        _ = new Animation
        {
            Duration = popIn ? Motion.Normal : Motion.Fast,
            Easing = popIn ? Motion.Pop : Motion.Ease,
            FillMode = FillMode.Backward,
            Children = { start, end },
        }.RunAsync(target);
    }
}
