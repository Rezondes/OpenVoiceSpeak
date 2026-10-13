using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using OVS.Client.Animations;
using OVS.Client.ViewModels;
using OVS.Client.Views;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Behaviors;

/// <summary>Package 96 (A112): one drag and drop behaviour for every reorder list, shown on the channel tree.</summary>
[Collection(MotionCollection.Name)]
public sealed class ReorderDragTests() : TempDirectoryTest("ovs-drag-", quiet: true)
{
    /// <param name="reduced">The Windows setting "Show animations in Windows" is off, set on this window.</param>
    Tree Open(bool reduced, ServerViewModel? server = null)
    {
        var sent = new List<Request>();
        var viewModel = MainViewModelSetup.Create(dir, action => action(), server: server ?? FakeServers.Admin(sent));
        var main = new MainWindow { DataContext = viewModel, Width = 1100, Height = 700 };
        main.SetValue(Motion.ReducedProperty, reduced);
        main.Show();
        Dispatcher.UIThread.RunJobs();
        return new Tree(main, viewModel, main.FindControl<ItemsControl>("ChannelItems")!, sent);
    }

    static PreviewTransforms Transforms(Visual preview)
    {
        var group = Assert.IsType<TransformGroup>(preview.RenderTransform);
        return new(group.Children.OfType<ScaleTransform>().SingleOrDefault(), group.Children.OfType<RotateTransform>().SingleOrDefault());
    }

    [AvaloniaFact]
    public void Preview_Dragging_Scale103Tilt3ShadowPlus8()
    {
        var tree = Open(reduced: false);
        Assert.Null(tree.Preview);
        tree.DragAbove("Raid", "Lobby");

        var preview = tree.Preview;
        Assert.NotNull(preview);
        var (scale, rotate) = Transforms(preview);
        Assert.Equal(1.03, scale!.ScaleX);
        Assert.Equal(1.03, scale.ScaleY);
        Assert.Equal(3, rotate!.Angle);
        Assert.Equal(1, preview.BoxShadow.Count);
        var shadow = preview.BoxShadow[0];
        Assert.Equal(8, shadow.OffsetY); // rows rest without a shadow: 8 px more offset and blur
        Assert.Equal(8, shadow.Blur);
        Assert.Equal(1, preview.Opacity);
        Assert.False(preview.IsHitTestVisible);
        Assert.Equal(tree.Container("Raid").Bounds.Width, preview.Width, 1);
        Assert.True(tree.Container("Raid").Opacity < 1); // the original is dimmed in its slot
        tree.Main.Close();
    }

    [AvaloniaFact]
    public void Placeholder_HasDraggedItemHeight_AtTarget()
    {
        // "ich" and two more sit in the second channel, the first has two users: the dragged block is the taller one
        var tree = Open(reduced: true, FakeServers.Crowded());
        var names = tree.Vm.Server!.Tree.Channels.Select(channel => channel.Name).ToList();
        var height = tree.Container(names[1]).Bounds.Height;
        Assert.NotEqual(height, tree.Container(names[0]).Bounds.Height);
        var lobbyTop = tree.Top(names[0]);

        tree.DragAbove(names[1], names[0]);

        Assert.Equal(height, tree.Container(names[0]).Margin.Top);
        Assert.Equal(lobbyTop + height, tree.Top(names[0])); // the lobby moved aside by exactly the gap
        Assert.All(names.Skip(1), name => Assert.Equal(default, tree.Container(name).Margin));
        tree.Main.Close();
    }

    [AvaloniaFact]
    public void Placeholder_DashedFrame_FillsTheGap()
    {
        var tree = Open(reduced: true);
        var overlay = OverlayLayer.GetOverlayLayer(tree.List)!;
        var lobbyTop = tree.Container("Lobby").TranslatePoint(default, overlay)!.Value.Y; // where the gap opens
        var height = tree.Container("Raid").Bounds.Height;

        tree.DragAbove("Raid", "Lobby");

        var frame = Assert.Single(overlay.Children.OfType<Avalonia.Controls.Shapes.Rectangle>(), rectangle => rectangle.Classes.Contains("dropPlaceholder"));
        Assert.True(frame.IsVisible);
        Assert.NotEmpty(frame.StrokeDashArray!); // a dashed outline, not a plain gap
        Assert.True(frame.StrokeThickness > 0);
        Assert.Equal(height, frame.Height);
        Assert.Equal(tree.Container("Raid").Bounds.Width, frame.Width, 1);
        Assert.Equal(lobbyTop, Canvas.GetTop(frame), 1);

        tree.Main.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(overlay.Children, control => control.Classes.Contains("dropPlaceholder"));
        tree.Main.Close();
    }

    [AvaloniaFact]
    public void Drag_ToTheEnd_GapAndFrameBelowLastItem()
    {
        var tree = Open(reduced: true);
        var overlay = OverlayLayer.GetOverlayLayer(tree.List)!;
        var raid = tree.Container("Raid");
        var below = raid.TranslatePoint(new Point(raid.Bounds.Width / 2, raid.Bounds.Height - 2), tree.Main)!.Value;
        var raidBottom = raid.TranslatePoint(new Point(0, raid.Bounds.Height), overlay)!.Value.Y;
        var height = tree.Container("Lobby").Bounds.Height;

        var start = tree.Center("Lobby");
        tree.Main.MouseDown(start, MouseButton.Left);
        tree.Main.MouseMove(start + new Point(0, 10));
        tree.Main.MouseMove(below);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(height, raid.Margin.Bottom); // the gap opens below the last item
        var frame = Assert.Single(overlay.Children.OfType<Avalonia.Controls.Shapes.Rectangle>(), rectangle => rectangle.Classes.Contains("dropPlaceholder"));
        Assert.True(frame.IsVisible);
        Assert.Equal(raidBottom, Canvas.GetTop(frame), 1);

        tree.Main.MouseUp(below, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { FakeServers.Raid, FakeServers.Lobby }, tree.Sent.OfType<ReorderChannels>().Single().ChannelIds);
        tree.Main.Close();
    }

    [AvaloniaFact]
    public void Drop_ItemTakesPlaceholderSlot_NoJump()
    {
        var tree = Open(reduced: true);
        var end = tree.DragAbove("Raid", "Lobby");
        var gapTop = tree.Top("Lobby") - tree.Container("Lobby").Margin.Top;
        var lobbyTop = tree.Top("Lobby");

        tree.Main.MouseUp(end, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { FakeServers.Raid, FakeServers.Lobby }, tree.Sent.OfType<ReorderChannels>().Single().ChannelIds);
        Assert.Null(tree.Preview);
        Assert.Equal(1, tree.Container("Raid").Opacity);

        // The server confirms the new order: the item sits where the gap was, the lobby stays where it was pushed to
        tree.Vm.Server!.Apply(new ChannelUpdated(new ChannelInfo(FakeServers.Raid, "Raid", "", 0)));
        tree.Vm.Server.Apply(new ChannelUpdated(new ChannelInfo(FakeServers.Lobby, "Lobby", "Start", 1)));
        Dispatcher.UIThread.RunJobs();
        tree.Main.UpdateLayout();
        Assert.Equal(["Raid", "Lobby"], tree.Vm.Server.Tree.Channels.Select(channel => channel.Name));
        Assert.Equal(gapTop, tree.Top("Raid"));
        Assert.Equal(lobbyTop, tree.Top("Lobby"));
        Assert.Equal(default, tree.Container("Lobby").Margin);
        tree.Main.Close();
    }

    [AvaloniaFact]
    public void EscOrOutside_Cancels_NothingSent()
    {
        var tree = Open(reduced: true);
        void AssertCleared()
        {
            Assert.Null(tree.Preview);
            Assert.Equal(default, tree.Container("Lobby").Margin);
            Assert.Equal(1, tree.Container("Raid").Opacity);
        }

        var end = tree.DragAbove("Raid", "Lobby");
        tree.Main.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();
        AssertCleared();
        tree.Main.MouseMove(end + new Point(0, 2));
        tree.Main.MouseUp(end, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(tree.Sent.OfType<ReorderChannels>());

        tree.DragAbove("Raid", "Lobby");
        Assert.NotNull(tree.Preview);
        tree.Main.MouseMove(new Point(800, 400)); // into the chat, outside the list
        tree.Main.MouseUp(new Point(800, 400), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        AssertCleared();
        Assert.Empty(tree.Sent.OfType<ReorderChannels>());
        tree.Main.Close();
    }

    [AvaloniaFact]
    public void ReducedMotion_KeepsTilt_GapDoesNotSlide()
    {
        var tree = Open(reduced: true);
        tree.DragAbove("Raid", "Lobby");

        // the tilt is a static pose, not an animation: it stays with reduced animations
        var (scale, rotate) = Transforms(tree.Preview!);
        Assert.Equal(1.03, scale!.ScaleX);
        Assert.Equal(3, rotate!.Angle);
        Assert.Equal(8, tree.Preview!.BoxShadow[0].OffsetY);
        Assert.Null(tree.Container("Lobby").Transitions); // the gap opens at once instead of sliding
        tree.Main.Close();

        tree = Open(reduced: false);
        tree.DragAbove("Raid", "Lobby");
        Assert.NotEmpty(tree.Container("Lobby").Transitions!);
        tree.Main.Close();
    }

    /// <summary>Package 102: a channel folding away is no place to drop at (before, this could crash or send a wrong order).</summary>
    [AvaloniaFact]
    public void Drop_BesideFoldingChannel_Ignored()
    {
        var tree = Open(reduced: true);
        var server = tree.Vm.Server!;
        server.Leave.Delay = TimeSpan.FromMinutes(1);
        var raid = tree.Container("Raid");
        var below = raid.TranslatePoint(new Point(raid.Bounds.Width / 2, raid.Bounds.Height - 2), tree.Main)!.Value;
        server.Apply(new ChannelAdded(new ChannelInfo(Guid.NewGuid(), "Archiv", "", -1)));
        server.Apply(new ChannelRemoved(FakeServers.Raid));
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("leaving", raid.Classes);

        var start = tree.Center("Lobby");
        tree.Main.MouseDown(start, MouseButton.Left);
        tree.Main.MouseMove(start + new Point(0, 10));
        tree.Main.MouseMove(below);
        Dispatcher.UIThread.RunJobs();
        tree.Main.MouseUp(below, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.All(tree.Sent.OfType<ReorderChannels>(), request => Assert.DoesNotContain(FakeServers.Raid, request.ChannelIds));
        tree.Main.Close();
    }

    /// <summary>Package 103: after a drop the new order does not move the rows a second time.</summary>
    [AvaloniaFact]
    public void Drop_ThenServerOrder_NoSecondMove()
    {
        var tree = Open(reduced: false);
        var end = tree.DragAbove("Raid", "Lobby");
        tree.Main.MouseUp(end, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Single(tree.Sent.OfType<ReorderChannels>());
        Assert.All(tree.List.GetRealizedContainers(), control => Assert.Equal(0, MotionWait.OffsetY(control)));
        tree.Vm.Server!.Apply(new ChannelUpdated(new ChannelInfo(FakeServers.Raid, "Raid", "", 0))); // the server confirms
        tree.Vm.Server.Apply(new ChannelUpdated(new ChannelInfo(FakeServers.Lobby, "Lobby", "Start", 1)));
        Dispatcher.UIThread.RunJobs();
        tree.Main.UpdateLayout();
        Assert.All(tree.List.GetRealizedContainers(), control => Assert.Equal(0, MotionWait.OffsetY(control)));
        tree.Main.Close();
    }

    sealed record Tree(MainWindow Main, MainViewModel Vm, ItemsControl List, List<Request> Sent)
    {
        public Control Container(string name) => List.ContainerFromItem(Vm.Server!.Tree.Channels.Single(channel => channel.Name == name))!;

        public double Top(string name) => Container(name).TranslatePoint(default, List)!.Value.Y;

        public Point Center(string name) => Rows.Center(Rows.ChannelRow(Main, name), Main);

        public Border? Preview => OverlayLayer.GetOverlayLayer(List)?.Children.OfType<Border>().SingleOrDefault(border => border.Classes.Contains("dragPreview"));

        /// <summary>Presses on the row of <paramref name="from"/> and moves to the upper half of the row of <paramref name="to"/>.</summary>
        public Point DragAbove(string from, string to)
        {
            var start = Center(from);
            var end = Center(to) + new Point(0, -6);
            Main.MouseDown(start, MouseButton.Left);
            Main.MouseMove(start + new Point(0, -10));
            Main.MouseMove(end);
            Dispatcher.UIThread.RunJobs();
            return end;
        }
    }

    sealed record PreviewTransforms(ScaleTransform? Scale, RotateTransform? Rotate);
}
