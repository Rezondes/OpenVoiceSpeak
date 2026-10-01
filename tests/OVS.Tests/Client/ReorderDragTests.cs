using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OVS.Client.ViewModels;
using OVS.Client.Views;
using OVS.Shared.Protocol;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client;

/// <summary>Package 96 (A112): one drag and drop behaviour for every reorder list, shown on the channel tree.</summary>
public sealed class ReorderDragTests : IDisposable
{
    readonly string dir = Directory.CreateTempSubdirectory("ovs-drag-").FullName;
    readonly Func<bool> reducedMotion = ReorderDrag.ReducedMotion;

    public void Dispose()
    {
        ReorderDrag.ReducedMotion = reducedMotion;
        try
        {
            Directory.Delete(dir, true);
        }
        catch (IOException)
        {
        }
    }

    sealed record Tree(MainWindow Main, MainViewModel Vm, ItemsControl List, List<Request> Sent)
    {
        public Control Container(string name) => List.ContainerFromItem(Vm.Server!.Channels.Single(c => c.Name == name))!;

        public double Top(string name) => Container(name).TranslatePoint(default, List)!.Value.Y;

        public Point Center(string name)
        {
            var row = Main.GetVisualDescendants().OfType<Border>()
                .Single(b => b.Classes.Contains("row") && b.DataContext is ChannelViewModel c && c.Name == name);
            return row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), Main)!.Value;
        }

        public Border? Preview => OverlayLayer.GetOverlayLayer(List)?.Children.OfType<Border>().SingleOrDefault(b => b.Classes.Contains("dragPreview"));

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

    Tree Open(ServerViewModel? server = null)
    {
        var sent = new List<Request>();
        var vm = new MainViewModel(dir, a => a(), useAudioDevices: false) { Server = server ?? FakeServers.Admin(sent) };
        var main = new MainWindow { DataContext = vm, Width = 1100, Height = 700 };
        main.Show();
        Dispatcher.UIThread.RunJobs();
        return new Tree(main, vm, main.FindControl<ItemsControl>("ChannelItems")!, sent);
    }

    static (ScaleTransform? Scale, RotateTransform? Rotate) Transforms(Visual preview)
    {
        var group = Assert.IsType<TransformGroup>(preview.RenderTransform);
        return (group.Children.OfType<ScaleTransform>().SingleOrDefault(), group.Children.OfType<RotateTransform>().SingleOrDefault());
    }

    [AvaloniaFact]
    public void Preview_Scale103_Tilt3_ShadowPlus8()
    {
        ReorderDrag.ReducedMotion = () => false;
        var tree = Open();
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
        ReorderDrag.ReducedMotion = () => true;
        // "ich" and two more sit in the second channel, the first has two users: the dragged block is the taller one
        var tree = Open(FakeServers.Crowded());
        var names = tree.Vm.Server!.Channels.Select(c => c.Name).ToList();
        var height = tree.Container(names[1]).Bounds.Height;
        Assert.NotEqual(height, tree.Container(names[0]).Bounds.Height);
        var lobbyTop = tree.Top(names[0]);

        tree.DragAbove(names[1], names[0]);

        Assert.Equal(height, tree.Container(names[0]).Margin.Top);
        Assert.Equal(lobbyTop + height, tree.Top(names[0])); // the lobby moved aside by exactly the gap
        Assert.All(names.Skip(1), n => Assert.Equal(default, tree.Container(n).Margin));
        tree.Main.Close();
    }

    [AvaloniaFact]
    public void Placeholder_DashedFrame_FillsTheGap()
    {
        ReorderDrag.ReducedMotion = () => true;
        var tree = Open();
        var overlay = OverlayLayer.GetOverlayLayer(tree.List)!;
        var lobbyTop = tree.Container("Lobby").TranslatePoint(default, overlay)!.Value.Y; // where the gap opens
        var height = tree.Container("Raid").Bounds.Height;

        tree.DragAbove("Raid", "Lobby");

        var frame = Assert.Single(overlay.Children.OfType<Avalonia.Controls.Shapes.Rectangle>(), r => r.Classes.Contains("dropPlaceholder"));
        Assert.True(frame.IsVisible);
        Assert.NotEmpty(frame.StrokeDashArray!); // a dashed outline, not a plain gap
        Assert.True(frame.StrokeThickness > 0);
        Assert.Equal(height, frame.Height);
        Assert.Equal(tree.Container("Raid").Bounds.Width, frame.Width, 1);
        Assert.Equal(lobbyTop, Canvas.GetTop(frame), 1);

        tree.Main.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(overlay.Children, c => c.Classes.Contains("dropPlaceholder"));
        tree.Main.Close();
    }

    [AvaloniaFact]
    public void DragToEnd_GapAndFrameBelowLastItem()
    {
        ReorderDrag.ReducedMotion = () => true;
        var tree = Open();
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
        var frame = Assert.Single(overlay.Children.OfType<Avalonia.Controls.Shapes.Rectangle>(), r => r.Classes.Contains("dropPlaceholder"));
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
        ReorderDrag.ReducedMotion = () => true;
        var tree = Open();
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
        Assert.Equal(["Raid", "Lobby"], tree.Vm.Server.Channels.Select(c => c.Name));
        Assert.Equal(gapTop, tree.Top("Raid"));
        Assert.Equal(lobbyTop, tree.Top("Lobby"));
        Assert.Equal(default, tree.Container("Lobby").Margin);
        tree.Main.Close();
    }

    [AvaloniaFact]
    public void EscOrOutside_Cancels_NothingSent()
    {
        ReorderDrag.ReducedMotion = () => true;
        var tree = Open();
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
        ReorderDrag.ReducedMotion = () => true;
        var tree = Open();
        tree.DragAbove("Raid", "Lobby");

        // the tilt is a static pose, not an animation: it stays with reduced animations
        var (scale, rotate) = Transforms(tree.Preview!);
        Assert.Equal(1.03, scale!.ScaleX);
        Assert.Equal(3, rotate!.Angle);
        Assert.Equal(8, tree.Preview!.BoxShadow[0].OffsetY);
        Assert.Null(tree.Container("Lobby").Transitions); // the gap opens at once instead of sliding
        tree.Main.Close();

        ReorderDrag.ReducedMotion = () => false;
        tree = Open();
        tree.DragAbove("Raid", "Lobby");
        Assert.NotEmpty(tree.Container("Lobby").Transitions!);
        tree.Main.Close();
    }

    /// <summary>Package 102: a channel folding away is no place to drop at (before, this could crash or send a wrong order).</summary>
    [AvaloniaFact]
    public void DropBesideFoldingChannel_IsIgnored()
    {
        ReorderDrag.ReducedMotion = () => true;
        var tree = Open();
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
        Assert.All(tree.Sent.OfType<ReorderChannels>(), r => Assert.DoesNotContain(FakeServers.Raid, r.ChannelIds));
        tree.Main.Close();
    }
}
