using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OVS.Client.Animations;
using OVS.Client.Models;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Client.Views;
using OVS.Shared.Protocol.Messages.Chat;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Animations;

/// <summary>Package 107: messages, notices, the send state and the unread badge move in the animated display.</summary>
[Collection(MotionCollection.Name)]
public sealed class ChatMotionTests() : TempDirectoryTest("ovs-chat-", quiet: true)
{
    /// <summary>Lets the clock run until the condition holds (at most 5 s): on a busy machine frames come late.</summary>
    static void SettleUntil(Func<bool> done)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && watch.ElapsedMilliseconds < 5000) MotionWait.Settle(10);
    }

    static void Frame()
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    ChatScene Open(DisplayMode display = DisplayMode.Animated, double height = 700)
    {
        new ClientSettings { Display = display }.Save(dir);
        var viewModel = MainViewModelSetup.Create(dir, action => Dispatcher.UIThread.Post(action));
        var main = new MainWindow { DataContext = viewModel, Width = 1100, Height = height };
        main.Show();
        var server = FakeServers.Admin();
        MainViewModelSetup.ShowServer(viewModel, server);
        MotionWait.Settle(700);
        Motion.Apply(main, display);
        MotionWait.Connected(main); // under load connecting takes longer: no test acts while it still moves
        return new(main, viewModel, server, main.GetVisualDescendants().OfType<ChatView>().Single());
    }

    /// <summary>
    /// Under load the lines are laid out late: waits until the list is long, has stopped growing and the glide along it
    /// has reached the end, so the reader can go up and stay there.
    /// </summary>
    static void LaidOutAtTheEnd(ScrollViewer scroller)
    {
        double extent = -1;
        SettleUntil(() =>
        {
            bool settled = scroller.Extent.Height == extent && scroller.Extent.Height > scroller.Viewport.Height + 200 && AtEnd(scroller);
            extent = scroller.Extent.Height;
            MotionWait.Settle(30);
            return settled;
        });
    }

    static bool AtEnd(ScrollViewer scroller) => scroller.Offset.Y >= scroller.Extent.Height - scroller.Viewport.Height - 1;

    /// <summary>The glow on a card's left edge (an inset shadow), 0 without one.</summary>
    static double Glow(Border card) => card.BoxShadow.Count > 0 && card.BoxShadow[0].IsInset ? card.BoxShadow[0].OffsetX : 0;

    static ChatMessage From(string nick, uint id, string text) =>
        new(ChatTarget.Server, id, nick, null, null, text, DateTimeOffset.Now);

    static Control Row(ChatView chat, string text) =>
        chat.FindControl<ItemsControl>("History")!.GetRealizedContainers().Last(control => control.DataContext is ChatEntry chatEntry && chatEntry.Text == text);


    /// <summary>The smallest opacity and the offset furthest from 0 seen while it arrives.</summary>
    static ArrivalLook Arrival(Func<Control> row)
    {
        double least = 1, far = 0;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 350)
        {
            var control = row();
            least = Math.Min(least, control.Opacity);
            if (Math.Abs(MotionWait.OffsetX(control)) > Math.Abs(far)) far = MotionWait.OffsetX(control);
            Frame();
            MotionWait.Pause();
        }
        return new(least, far);
    }

    [AvaloniaFact]
    public void NewMessage_SlidesIn_OwnFromComposer()
    {
        var (main, viewModel, server, chat) = Open();
        server.Apply(From("anna", 2, "Hallo zusammen"));
        Frame();
        var (opacity, offsetX) = Arrival(() => Row(chat, "Hallo zusammen"));
        Assert.True(opacity < 1 && offsetX < 0, $"someone else's message comes from the left ({opacity}, {offsetX})");

        viewModel.Chat!.Draft = "Hallo anna";
        viewModel.Chat.SendCommand.Execute(null);
        Frame();
        (opacity, offsetX) = Arrival(() => Row(chat, "Hallo anna"));
        Assert.True(opacity < 1 && offsetX > 0, $"one's own comes from the composer side ({opacity}, {offsetX})");
        MotionWait.Eventually(() => Assert.Equal(0, MotionWait.OffsetX(Row(chat, "Hallo anna"))));
        main.Close();
    }

    [AvaloniaFact]
    public void HistoryLoad_OneFade_NotLineByLine()
    {
        var (main, viewModel, server, chat) = Open();
        viewModel.Chat!.Selected = viewModel.Chat.Tabs[1];
        MotionWait.Settle(100);
        for (int index = 0; index < 5; index++) server.Apply(From("anna", 2, $"Nachricht {index}")); // into "Allgemein", not shown
        MotionWait.Settle(100);
        viewModel.Chat.Selected = viewModel.Chat.Tabs[0];
        Frame();
        Assert.All(chat.FindControl<ItemsControl>("History")!.GetRealizedContainers(), control => Assert.Equal(0, MotionWait.OffsetX(control)));
        main.Close();
    }

    [AvaloniaFact]
    public void ScrolledUp_NoAutoScroll_PillAppears()
    {
        var (main, viewModel, server, chat) = Open(height: 420);
        for (int index = 0; index < 25; index++) server.Apply(From("anna", 2, $"Zeile {index}"));
        var scroller = chat.FindControl<ScrollViewer>("Scroller")!;
        var pill = chat.FindControl<Button>("NewMessagesPill")!;
        bool AtEnd() => ChatMotionTests.AtEnd(scroller);
        LaidOutAtTheEnd(scroller); // the reader can only go up once the list is long and has stopped growing
        Assert.True(scroller.Offset.Y >= scroller.Extent.Height - scroller.Viewport.Height - 1, "at the end it glided along");
        Assert.False(pill.IsVisible);

        scroller.Offset = new Vector(0, 0); // the reader goes up (it also stops a glide still on its way)
        MotionWait.Settle(50);
        Assert.Equal(0, scroller.Offset.Y);
        server.Apply(From("anna", 2, "Noch eine"));
        SettleUntil(() => pill.IsVisible);
        MotionWait.Settle(100);
        Assert.Equal(0, scroller.Offset.Y); // nothing scrolls
        Assert.True(pill.IsVisible);

        pill.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        SettleUntil(() => !pill.IsVisible && AtEnd());
        Assert.False(pill.IsVisible);
        Assert.True(scroller.Offset.Y >= scroller.Extent.Height - scroller.Viewport.Height - 1, "the pill glides to the end");
        main.Close();
    }

    [AvaloniaFact]
    public void Pending_FadesToSent_WithACheck()
    {
        var (main, viewModel, server, chat) = Open();
        viewModel.Chat!.Draft = "Bin gleich da";
        viewModel.Chat.SendCommand.Execute(null);
        MotionWait.Settle(350);
        var bubble = Row(chat, "Bin gleich da").GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("own"));
        Assert.Contains("sending", bubble.Classes);
        server.Apply(new ChatMessage(ChatTarget.Server, 1, "ich", null, null, "Bin gleich da", DateTimeOffset.Now)); // the echo
        Frame();
        Assert.DoesNotContain("sending", bubble.Classes);
        Assert.True(bubble.Opacity < 1, "it turns normal with a fade");
        Assert.Contains(bubble.GetVisualDescendants().OfType<PathIcon>(), pathIcon => pathIcon.Classes.Contains("sentMark"));
        MotionWait.Eventually(() =>
        {
            Assert.Equal(1, bubble.Opacity, 2);
            Assert.DoesNotContain(bubble.GetVisualDescendants().OfType<PathIcon>(), pathIcon => pathIcon.Classes.Contains("sentMark"));
        });
        main.Close();
    }

    [AvaloniaFact]
    public void Notice_Arrives_SlidesInWithPulse()
    {
        var (main, viewModel, _, chat) = Open();
        viewModel.Chat!.AddNotice(new Notice(DateTime.Now, "Etwas ging schief", NoticeKind.Error));
        Frame();
        // the glow as it happens (the pulse starts after the row is shown): under load one frame can outlast it
        var card = Row(chat, "Etwas ging schief").GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("card"));
        var glow = MotionWait.Record(card, visual => Glow((Border)visual));
        glow.Add(Glow(card));
        MotionWait.Until(() => glow.Any(value => value > 0));
        Assert.True(glow.Any(value => value > 0), "its left edge glows");
        MotionWait.Eventually(() => Assert.True(card.BoxShadow.Count == 0 || card.BoxShadow[0].OffsetX < 0.5));
        main.Close();
    }

    [AvaloniaFact]
    public void UnreadBadge_FirstUnread_PopsIn()
    {
        var (main, viewModel, server, chat) = Open();
        viewModel.Chat!.Selected = viewModel.Chat.Tabs[1];
        MotionWait.Settle(400);
        server.Apply(From("anna", 2, "Hallo"));
        Frame();
        Frame();
        var badge = chat.FindControl<Avalonia.Controls.Primitives.TabStrip>("Tabs")!.ContainerFromIndex(0)!
            .GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("count"));
        Assert.True(badge.IsVisible);
        Assert.True(MotionWait.Scale(badge) < 1 || badge.Opacity < 1, "the badge pops in");
        MotionWait.Eventually(() => Assert.Equal(1, MotionWait.Scale(badge), 2));
        main.Close();
    }

    [AvaloniaFact]
    public void UnreadBadge_Simplified_StartsNoAnimation()
    {
        var (main, viewModel, server, _) = Open(DisplayMode.Simplified);
        try
        {
            viewModel.Chat!.Selected = viewModel.Chat.Tabs[1];
            Dispatcher.UIThread.RunJobs();
            int unread = viewModel.Chat.Tabs[0].Unread, started = AnimationCounter.Started;
            server.Apply(From("anna", 2, "Hallo"));
            Dispatcher.UIThread.RunJobs(); // the badge's job too
            Assert.Equal(unread + 1, viewModel.Chat.Tabs[0].Unread);
            Assert.Equal(started, AnimationCounter.Started);
        }
        finally
        {
            main.Close();
        }
    }

    [AvaloniaFact]
    public void Motion_SimplifiedDisplay_AsToday()
    {
        var (main, viewModel, server, chat) = Open(DisplayMode.Simplified, height: 420);
        for (int index = 0; index < 25; index++) server.Apply(From("anna", 2, $"Zeile {index}"));
        MotionWait.Settle(200);
        var scroller = chat.FindControl<ScrollViewer>("Scroller")!;
        scroller.Offset = new Vector(0, 0);
        MotionWait.Settle(50);
        server.Apply(From("anna", 2, "Noch eine"));
        Frame();
        Assert.Equal((1d, 0d), (Row(chat, "Noch eine").Opacity, MotionWait.OffsetX(Row(chat, "Noch eine"))));
        Assert.False(chat.FindControl<Button>("NewMessagesPill")!.IsVisible);
        main.Close();
    }

    [AvaloniaFact]
    public void Warning_Added_EdgeGlows()
    {
        var (main, viewModel, _, chat) = Open();
        viewModel.Chat!.AddNotice(new Notice(DateTime.Now, "Achtung", NoticeKind.Warning));
        Frame();
        var card = Row(chat, "Achtung").GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("card"));
        var glow = MotionWait.Record(card, visual => Glow((Border)visual)); // as it happens: under load one frame can outlast it
        glow.Add(Glow(card));
        MotionWait.Until(() => glow.Any(value => value > 0));
        Assert.True(glow.Any(value => value > 0), "a warning's edge glows as well");
        main.Close();
    }

    [AvaloniaFact]
    public void Badge_CountGoesUp_NumberTicks()
    {
        var (main, viewModel, server, chat) = Open();
        viewModel.Chat!.Selected = viewModel.Chat.Tabs[1];
        MotionWait.Settle(400);
        server.Apply(From("anna", 2, "Eins"));
        MotionWait.Settle(400);
        server.Apply(From("anna", 2, "Zwei")); // the count goes up: its number ticks
        Frame();
        Frame();
        var count = chat.FindControl<Avalonia.Controls.Primitives.TabStrip>("Tabs")!.ContainerFromIndex(0)!
            .GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("count")).Child!;
        Assert.True(MotionWait.OffsetY(count) > 0 || count.Opacity < 1, "the number ticks");
        main.Close();
    }

    [AvaloniaFact]
    public void NewMessage_AfterTabSwitch_StillSlidesIn()
    {
        var (main, viewModel, server, chat) = Open();
        viewModel.Chat!.Selected = viewModel.Chat.Tabs[1];
        MotionWait.Settle(400);
        viewModel.Chat.Selected = viewModel.Chat.Tabs[0];
        MotionWait.Settle(400);
        server.Apply(From("anna", 2, "Wieder da"));
        Frame();
        var (opacity, offsetX) = Arrival(() => Row(chat, "Wieder da"));
        Assert.True(opacity < 1 && offsetX < 0, $"({opacity}, {offsetX})");
        main.Close();
    }

    [AvaloniaFact]
    public void Pill_SwitchToSimplified_Hidden()
    {
        var (main, viewModel, server, chat) = Open(height: 420);
        for (int index = 0; index < 25; index++) server.Apply(From("anna", 2, $"Zeile {index}"));
        var scroller = chat.FindControl<ScrollViewer>("Scroller")!;
        LaidOutAtTheEnd(scroller); // the reader can only go up once the list is long and has stopped growing
        scroller.Offset = default;
        MotionWait.Settle(50);
        server.Apply(From("anna", 2, "Noch eine"));
        SettleUntil(() => chat.FindControl<Button>("NewMessagesPill")!.IsVisible);
        Assert.True(chat.FindControl<Button>("NewMessagesPill")!.IsVisible);
        viewModel.Appearance = viewModel.Appearance with { Display = DisplayMode.Simplified };
        Frame();
        Assert.False(chat.FindControl<Button>("NewMessagesPill")!.IsVisible);
        main.Close();
    }

    [AvaloniaFact]
    public void Composer_NotSent_Shakes()
    {
        var (main, viewModel, _, chat) = Open();
        viewModel.Chat!.Draft = "Geht nicht";
        viewModel.Chat.SendCommand.Execute(null);
        MotionWait.Settle(350);
        var entry = viewModel.Chat.Selected.Entries.Last(chatEntry => chatEntry.Text == "Geht nicht");
        entry.Sending!.Pending.Fail("abgelehnt"); // the server said no
        Frame();
        var notSent = Row(chat, "Geht nicht").GetVisualDescendants().OfType<WrapPanel>().First(wrapPanel => wrapPanel.Name == "NotSent");
        var swings = MotionWait.Record(notSent, MotionWait.OffsetX); // every swing as it happens: under load one frame can outlast the shake
        MotionWait.Until(() => swings.Any(value => Math.Abs(value) > 2) && MotionWait.OffsetX(notSent) == 0); // a shake swings through 0, and ends there
        double swing = swings.Select(value => Math.Abs(value)).DefaultIfEmpty(0).Max();
        Assert.True(notSent.IsVisible);
        Assert.True(swing > 2, $"\"nicht gesendet\" shakes ({swing})");
        main.Close();
    }

    sealed record ArrivalLook(double Opacity, double X);

    sealed record ChatScene(MainWindow Main, MainViewModel Vm, ServerViewModel Server, ChatView Chat);
}
