using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Client.Views;
using OVS.Shared.Protocol;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client;

/// <summary>Package 107: messages, notices, the send state and the unread badge move in the animated display.</summary>
public sealed class ChatMotionTests : IDisposable
{
    readonly string dir = Directory.CreateTempSubdirectory("ovs-chat-").FullName;

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
            Thread.Sleep(1);
        }
    }

    static void Frame()
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    (MainWindow Main, MainViewModel Vm, ServerViewModel Server, ChatView Chat) Open(DisplayMode display = DisplayMode.Animated, double height = 700)
    {
        new ClientSettings { Display = display }.Save(dir);
        var vm = new MainViewModel(dir, a => Dispatcher.UIThread.Post(a), useAudioDevices: false);
        var main = new MainWindow { DataContext = vm, Width = 1100, Height = height };
        main.Show();
        var server = FakeServers.Admin();
        vm.Server = server;
        Settle(700);
        Motion.Apply(main, display);
        return (main, vm, server, main.GetVisualDescendants().OfType<ChatView>().Single());
    }

    static ChatMessage From(string nick, uint id, string text) =>
        new(ChatTarget.Server, id, nick, null, null, text, DateTimeOffset.Now);

    static Control Row(ChatView chat, string text) =>
        chat.FindControl<ItemsControl>("History")!.GetRealizedContainers().Last(c => c.DataContext is ChatEntry e && e.Text == text);

    static double OffsetX(Visual v) => v.RenderTransform?.Value.M31 ?? 0;

    /// <summary>The smallest opacity and the offset furthest from 0 seen while it arrives.</summary>
    static (double Opacity, double X) Arrival(Func<Control> row)
    {
        double least = 1, far = 0;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 350)
        {
            var r = row();
            least = Math.Min(least, r.Opacity);
            if (Math.Abs(OffsetX(r)) > Math.Abs(far)) far = OffsetX(r);
            Frame();
            Thread.Sleep(1);
        }
        return (least, far);
    }

    [AvaloniaFact]
    public void NewMessage_SlidesIn_OwnFromComposer()
    {
        var (main, vm, server, chat) = Open();
        server.Apply(From("anna", 2, "Hallo zusammen"));
        Frame();
        var (opacity, x) = Arrival(() => Row(chat, "Hallo zusammen"));
        Assert.True(opacity < 1 && x < 0, $"someone else's message comes from the left ({opacity}, {x})");

        vm.Chat!.Draft = "Hallo anna";
        vm.Chat.SendCommand.Execute(null);
        Frame();
        (opacity, x) = Arrival(() => Row(chat, "Hallo anna"));
        Assert.True(opacity < 1 && x > 0, $"one's own comes from the composer side ({opacity}, {x})");
        Assert.Equal(0, OffsetX(Row(chat, "Hallo anna")));
        main.Close();
    }

    [AvaloniaFact]
    public void HistoryLoad_OneFade_NotLineByLine()
    {
        var (main, vm, server, chat) = Open();
        vm.Chat!.Selected = vm.Chat.Tabs[1];
        Settle(100);
        for (int i = 0; i < 5; i++) server.Apply(From("anna", 2, $"Nachricht {i}")); // into "Allgemein", not shown
        Settle(100);
        vm.Chat.Selected = vm.Chat.Tabs[0];
        Frame();
        Assert.All(chat.FindControl<ItemsControl>("History")!.GetRealizedContainers(), c => Assert.Equal(0, OffsetX(c)));
        main.Close();
    }

    [AvaloniaFact]
    public void ScrolledUp_NoAutoScroll_PillAppears()
    {
        var (main, vm, server, chat) = Open(height: 420);
        for (int i = 0; i < 25; i++) server.Apply(From("anna", 2, $"Zeile {i}"));
        Settle(500);
        var scroller = chat.FindControl<ScrollViewer>("Scroller")!;
        var pill = chat.FindControl<Button>("NewMessagesPill")!;
        Assert.True(scroller.Offset.Y >= scroller.Extent.Height - scroller.Viewport.Height - 1, "at the end it glided along");
        Assert.False(pill.IsVisible);

        scroller.Offset = new Vector(0, 0); // the reader goes up (it also stops a glide still on its way)
        Settle(50);
        Assert.Equal(0, scroller.Offset.Y);
        server.Apply(From("anna", 2, "Noch eine"));
        Settle(350);
        Assert.Equal(0, scroller.Offset.Y); // nothing scrolls
        Assert.True(pill.IsVisible);

        pill.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Settle(400);
        Assert.False(pill.IsVisible);
        Assert.True(scroller.Offset.Y >= scroller.Extent.Height - scroller.Viewport.Height - 1, "the pill glides to the end");
        main.Close();
    }

    [AvaloniaFact]
    public void Pending_FadesToSent_WithACheck()
    {
        var (main, vm, server, chat) = Open();
        vm.Chat!.Draft = "Bin gleich da";
        vm.Chat.SendCommand.Execute(null);
        Settle(350);
        var bubble = Row(chat, "Bin gleich da").GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("own"));
        Assert.Contains("sending", bubble.Classes);
        server.Apply(new ChatMessage(ChatTarget.Server, 1, "ich", null, null, "Bin gleich da", DateTimeOffset.Now)); // the echo
        Frame();
        Assert.DoesNotContain("sending", bubble.Classes);
        Assert.True(bubble.Opacity < 1, "it turns normal with a fade");
        Assert.Contains(bubble.GetVisualDescendants().OfType<PathIcon>(), i => i.Classes.Contains("sentMark"));
        Settle(900);
        Assert.Equal(1, bubble.Opacity, 2);
        Assert.DoesNotContain(bubble.GetVisualDescendants().OfType<PathIcon>(), i => i.Classes.Contains("sentMark"));
        main.Close();
    }

    [AvaloniaFact]
    public void Notice_SlidesInWithPulse()
    {
        var (main, vm, _, chat) = Open();
        vm.Chat!.AddNotice(new Notice(DateTime.Now, "Etwas ging schief", NoticeKind.Error));
        Frame();
        Settle(60);
        var card = Row(chat, "Etwas ging schief").GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("card"));
        Assert.True(card.BoxShadow.Count > 0 && card.BoxShadow[0].IsInset && card.BoxShadow[0].OffsetX > 0, "its left edge glows");
        Settle(700);
        Assert.True(card.BoxShadow.Count == 0 || card.BoxShadow[0].OffsetX < 0.5);
        main.Close();
    }

    [AvaloniaFact]
    public void UnreadBadge_PopsIn()
    {
        var (main, vm, server, chat) = Open();
        vm.Chat!.Selected = vm.Chat.Tabs[1];
        Settle(400);
        server.Apply(From("anna", 2, "Hallo"));
        Frame();
        Frame();
        var badge = chat.FindControl<Avalonia.Controls.Primitives.TabStrip>("Tabs")!.ContainerFromIndex(0)!
            .GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("count"));
        Assert.True(badge.IsVisible);
        Assert.True((badge.RenderTransform?.Value.M11 ?? 1) < 1 || badge.Opacity < 1, "the badge pops in");
        Settle(400);
        Assert.Equal(1, badge.RenderTransform?.Value.M11 ?? 1, 2);
        main.Close();
    }

    [AvaloniaFact]
    public void Simplified_AsToday()
    {
        var (main, vm, server, chat) = Open(DisplayMode.Simplified, height: 420);
        for (int i = 0; i < 25; i++) server.Apply(From("anna", 2, $"Zeile {i}"));
        Settle(200);
        var scroller = chat.FindControl<ScrollViewer>("Scroller")!;
        scroller.Offset = new Vector(0, 0);
        Settle(50);
        server.Apply(From("anna", 2, "Noch eine"));
        Frame();
        Assert.Equal((1d, 0d), (Row(chat, "Noch eine").Opacity, OffsetX(Row(chat, "Noch eine"))));
        Assert.False(chat.FindControl<Button>("NewMessagesPill")!.IsVisible);
        main.Close();
    }

    [AvaloniaFact]
    public void Warning_PulsesToo_AndBadgeCountTicks()
    {
        var (main, vm, server, chat) = Open();
        vm.Chat!.AddNotice(new Notice(DateTime.Now, "Achtung", NoticeKind.Warning));
        Frame();
        Settle(60);
        var card = Row(chat, "Achtung").GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("card"));
        Assert.True(card.BoxShadow.Count > 0 && card.BoxShadow[0].OffsetX > 0, "a warning's edge glows as well");

        vm.Chat.Selected = vm.Chat.Tabs[1];
        Settle(400);
        server.Apply(From("anna", 2, "Eins"));
        Settle(400);
        server.Apply(From("anna", 2, "Zwei")); // the count goes up: its number ticks
        Frame();
        Frame();
        var count = chat.FindControl<Avalonia.Controls.Primitives.TabStrip>("Tabs")!.ContainerFromIndex(0)!
            .GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("count")).Child!;
        Assert.True((count.RenderTransform?.Value.M32 ?? 0) > 0 || count.Opacity < 1, "the number ticks");
        main.Close();
    }

    [AvaloniaFact]
    public void AfterTabSwitch_NewMessageStillSlidesIn()
    {
        var (main, vm, server, chat) = Open();
        vm.Chat!.Selected = vm.Chat.Tabs[1];
        Settle(400);
        vm.Chat.Selected = vm.Chat.Tabs[0];
        Settle(400);
        server.Apply(From("anna", 2, "Wieder da"));
        Frame();
        var (opacity, x) = Arrival(() => Row(chat, "Wieder da"));
        Assert.True(opacity < 1 && x < 0, $"({opacity}, {x})");
        main.Close();
    }

    [AvaloniaFact]
    public void SwitchToSimplified_HidesPill()
    {
        var (main, vm, server, chat) = Open(height: 420);
        for (int i = 0; i < 25; i++) server.Apply(From("anna", 2, $"Zeile {i}"));
        Settle(500);
        var scroller = chat.FindControl<ScrollViewer>("Scroller")!;
        scroller.Offset = default;
        Settle(50);
        server.Apply(From("anna", 2, "Noch eine"));
        Settle(100);
        Assert.True(chat.FindControl<Button>("NewMessagesPill")!.IsVisible);
        vm.Appearance = vm.Appearance with { Display = DisplayMode.Simplified };
        Frame();
        Assert.False(chat.FindControl<Button>("NewMessagesPill")!.IsVisible);
        main.Close();
    }

    [AvaloniaFact]
    public void NotSent_Shakes()
    {
        var (main, vm, _, chat) = Open();
        vm.Chat!.Draft = "Geht nicht";
        vm.Chat.SendCommand.Execute(null);
        Settle(350);
        var entry = vm.Chat.Selected.Entries.Last(e => e.Text == "Geht nicht");
        entry.Sending!.Pending.Fail("abgelehnt"); // the server said no
        Frame();
        var notSent = Row(chat, "Geht nicht").GetVisualDescendants().OfType<WrapPanel>().First(w => w.Name == "NotSent");
        double swing = 0;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 450) // a shake swings through 0: the largest swing seen
        {
            swing = Math.Max(swing, Math.Abs(OffsetX(notSent)));
            Frame();
            Thread.Sleep(1);
        }
        Assert.True(notSent.IsVisible);
        Assert.True(swing > 2, $"\"nicht gesendet\" shakes ({swing})");
        main.Close();
    }
}
