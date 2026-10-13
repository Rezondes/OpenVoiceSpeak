using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OVS.Client.Animations;
using OVS.Client.Behaviors;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Client.Views;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Users;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Behaviors;

/// <summary>Package 120 (A129): a user row dragged onto a channel moves that user there, my own row joins it.</summary>
[Collection(MotionCollection.Name)]
public sealed class UserDragTests : TempDirectoryTest
{
    /// <summary>Animated, as the drop flights need (A114: the default is simplified).</summary>
    public UserDragTests() : base("ovs-userdrag-", quiet: true) => new ClientSettings { Display = DisplayMode.Animated }.Save(dir);

    /// <summary>Animated, as in the app: a drop must hold a flight that would really play. Waits until the tree has built up.</summary>
    Tree Open(ServerViewModel? server = null, List<Request>? sent = null)
    {
        sent ??= [];
        var viewModel = MainViewModelSetup.Create(dir, action => Dispatcher.UIThread.Post(action));
        var main = new MainWindow { DataContext = viewModel, Width = 1100, Height = 700 };
        main.Show();
        MainViewModelSetup.ShowServer(viewModel, server ?? FakeServers.Admin(sent));
        MotionWait.Until(() => MotionWait.IsConnected(main), atLeast: 100);
        return new Tree(main, viewModel.Server!, sent);
    }

    static void Release(Tree tree, Point at)
    {
        tree.Main.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The user's row in its new list has come in and stands still.</summary>
    static bool Landed(Tree tree, string nickname)
    {
        // until the next layout the new row may not exist yet, the old one folds meanwhile
        var rows = tree.Main.GetVisualDescendants().OfType<ContentPresenter>().Where(contentPresenter => contentPresenter.DataContext is UserViewModel user && user.Nickname == nickname).ToList();
        return rows is [{ Opacity: 1 } row] && !row.Classes.Contains("entering") && !row.Classes.Contains("leaving");
    }

    [AvaloniaFact]
    public void DragUser_OntoChannel_HighlightsAndMoves()
    {
        var tree = Open();
        var anna = tree.UserRow("anna"); // in Raid, weaker than me
        var start = tree.Center(anna);
        tree.Main.MouseDown(start, MouseButton.Left);
        tree.Main.MouseMove(start + new Point(0, ReorderDrag.Threshold - 2), Tree.Held);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(tree.Preview); // under the threshold nothing lifts

        tree.Main.MouseMove(tree.Center(tree.ChannelRow("Raid")), Tree.Held);
        Dispatcher.UIThread.RunJobs();
        var preview = tree.Preview;
        Assert.NotNull(preview); // A112: lifted, scaled and tilted
        var transforms = Assert.IsType<TransformGroup>(preview.RenderTransform);
        Assert.Equal(ReorderDrag.Scale, transforms.Children.OfType<ScaleTransform>().Single().ScaleX);
        Assert.Equal(ReorderDrag.Tilt, transforms.Children.OfType<RotateTransform>().Single().Angle);
        Assert.Equal(anna.Bounds.Width, preview.Width, 1);
        Assert.True(anna.Opacity < 1); // dimmed where it is
        Assert.False(tree.Lit("Raid")); // her own channel is no target

        var end = tree.Center(tree.ChannelRow("Lobby"));
        tree.Main.MouseMove(end, Tree.Held);
        Dispatcher.UIThread.RunJobs();
        Assert.True(tree.Lit("Lobby"));
        Assert.False(tree.Lit("Raid"));
        Assert.Equal(end.Y, Canvas.GetTop(preview) + anna.Bounds.Height / 2, 1); // follows the pointer where it was grabbed

        // every flight picture put in the overlay from now on, not only one a frame finds there
        int flights = 0;
        tree.Overlay.Children.CollectionChanged += (_, args) => flights += args.NewItems?.OfType<Border>().Count(border => border.Classes.Contains(FlyGhost.GhostClass)) ?? 0;
        Release(tree, end);
        Assert.Equal(new MoveUser(2, FakeServers.Lobby), tree.Sent.OfType<MoveUser>().Single() with { RequestId = null });
        Assert.Null(tree.Preview);
        Assert.False(tree.Lit("Lobby"));
        Assert.Equal(1, anna.Opacity);

        tree.Server.Apply(new UserUpdated(tree.Server.Mirror.Users[2] with { ChannelId = FakeServers.Lobby })); // the server confirms
        // settled, and long enough that a flight queued for the change would have shown: the negative check cannot pass early
        Dispatcher.UIThread.RunJobs();
        MotionWait.Until(() => Landed(tree, "anna"), atLeast: 400);
        Assert.True(Landed(tree, "anna"));
        Assert.Equal(0, flights); // it already sits where it was dropped
        Assert.Empty(tree.Sent.OfType<ReorderChannels>()); // a user drag never reorders the channels

        // a move made elsewhere still flies: the drop held only its own
        tree.Server.Apply(new UserUpdated(tree.Server.Mirror.Users[2] with { ChannelId = FakeServers.Raid }));
        MotionWait.Until(() => flights > 0);
        Assert.Equal(1, flights);
        tree.Main.Close();
    }

    [AvaloniaFact]
    public void DragUser_DropOutsideOrEsc_SendsNothing()
    {
        var tree = Open();
        var anna = tree.UserRow("anna");
        void AssertCleared()
        {
            Assert.Null(tree.Preview);
            Assert.False(tree.Lit("Lobby"));
            Assert.Equal(1, anna.Opacity);
        }

        var end = tree.Drag("anna", "Lobby");
        Assert.True(tree.Lit("Lobby"));
        tree.Main.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();
        AssertCleared();
        tree.Main.MouseMove(end + new Point(0, 2), Tree.Held);
        Release(tree, end);
        AssertCleared();

        tree.Drag("anna", "Lobby");
        tree.Main.MouseMove(new Point(800, 400), Tree.Held); // into the chat, outside the tree
        Dispatcher.UIThread.RunJobs();
        Assert.False(tree.Lit("Lobby"));
        Release(tree, new Point(800, 400));
        AssertCleared();

        end = tree.Drag("anna", "Raid"); // her own channel
        Release(tree, end);
        AssertCleared();
        Assert.Empty(tree.Sent);
        tree.Main.Close();
    }

    /// <summary>Review: the dragged user leaves the server mid-drag; once the row is gone the drag ends and a drop sends nothing.</summary>
    [AvaloniaFact]
    public void DragUser_UserLeavesMidDrag_DropSendsNothing()
    {
        var tree = Open();
        var end = tree.Drag("anna", "Lobby");
        Assert.NotNull(tree.Preview);
        tree.Server.Apply(new UserLeft(2));
        MotionWait.Until(() => !tree.Main.GetVisualDescendants().OfType<ContentPresenter>().Any(contentPresenter => contentPresenter.DataContext is UserViewModel { Nickname: "anna" }));
        tree.Main.MouseMove(end + new Point(0, 2), Tree.Held);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(tree.Preview); // her row has left the tree: nothing is dragged any more
        Assert.False(tree.Lit("Lobby"));
        Release(tree, end);
        Assert.Empty(tree.Sent);
        tree.Main.Close();
    }

    /// <summary>Review: the button let go outside the window before the threshold; hovering later does not start a drag.</summary>
    [AvaloniaFact]
    public void DragUser_MoveWithoutButton_NoDrag()
    {
        var tree = Open();
        var start = tree.Center(tree.UserRow("anna"));
        tree.Main.MouseDown(start, MouseButton.Left);
        tree.Main.MouseMove(tree.Center(tree.ChannelRow("Lobby"))); // no button held any more
        Dispatcher.UIThread.RunJobs();
        Assert.Null(tree.Preview);
        tree.Main.MouseMove(tree.Center(tree.ChannelRow("Lobby")) + new Point(0, 2), Tree.Held);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(tree.Preview); // the press was forgotten
        Assert.Empty(tree.Sent);
        tree.Main.Close();
    }

    [AvaloniaFact]
    public void DragUser_OwnRowDropped_JoinsChannel()
    {
        var tree = Open();
        Assert.Equal(FakeServers.Lobby, tree.Server.CurrentChannel!.Id);
        var end = tree.Drag("ich", "Raid");
        Assert.True(tree.Lit("Raid"));
        Release(tree, end);
        Assert.Equal(new JoinChannel(FakeServers.Raid, null), tree.Sent.OfType<JoinChannel>().Single() with { RequestId = null });
        Assert.Empty(tree.Sent.OfType<MoveUser>()); // moving oneself is joining
        tree.Main.Close();
    }

    [AvaloniaFact]
    public void NoMoveRight_NoDrag_ChannelDragUnchanged()
    {
        // bert is not weaker than me: his row stays put. Anna is, but I lack UserMove.
        var sent = new List<Request>();
        var server = FakeServers.Admin(sent);
        server.Apply(new UserJoined(new UserInfo(3, "fp3", "bert", FakeServers.Raid, false, false, false, Permission.None, [WellKnownGroups.Guest])));
        var tree = Open(server, sent);
        Assert.False(server.Tree.Channels.SelectMany(channel => channel.Users).Single(user => user.Nickname == "bert").CanDrag);
        var end = tree.Drag("bert", "Lobby");
        Assert.Null(tree.Preview);
        Assert.False(tree.Lit("Lobby"));
        Release(tree, end);
        tree.Main.Close();

        var me = server.Mirror.Users[1];
        server.Apply(new UserUpdated(me with { Permissions = me.Permissions & ~Permission.UserMove }));
        tree = Open(server, sent);
        Assert.False(server.Tree.Channels.SelectMany(channel => channel.Users).Single(user => user.Nickname == "anna").CanDrag);
        end = tree.Drag("anna", "Lobby");
        Assert.Null(tree.Preview); // neither a user drag nor a channel drag from a user row
        Release(tree, end);
        Assert.Empty(sent);

        // a channel row still drags the channel (Package 96)
        var start = tree.Center(tree.ChannelRow("Raid"));
        var above = tree.Center(tree.ChannelRow("Lobby")) + new Point(0, -6);
        tree.Main.MouseDown(start, MouseButton.Left);
        tree.Main.MouseMove(start + new Point(0, -10));
        tree.Main.MouseMove(above);
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(tree.Preview);
        Assert.False(tree.Lit("Lobby"));
        Release(tree, above);
        Assert.Equal(new[] { FakeServers.Raid, FakeServers.Lobby }, sent.OfType<ReorderChannels>().Single().ChannelIds);
        Assert.Empty(sent.OfType<MoveUser>());
        tree.Main.Close();
    }

    sealed record Tree(MainWindow Main, ServerViewModel Server, List<Request> Sent)
    {
        public Border UserRow(string nickname) => Rows.UserRow(Main, nickname);

        public Border ChannelRow(string name) => Rows.ChannelRow(Main, name);

        public Point Center(Control row) => Rows.Center(row, Main);

        /// <summary>A move with the left button still down, as during a real drag.</summary>
        public const RawInputModifiers Held = RawInputModifiers.LeftMouseButton;

        public OverlayLayer Overlay => OverlayLayer.GetOverlayLayer(Main)!;

        public Border? Preview => Overlay.Children.OfType<Border>().SingleOrDefault(border => border.Classes.Contains("dragPreview"));

        public bool Lit(string channel) => ChannelRow(channel).Classes.Contains(UserDrag.TargetClass);

        /// <summary>Presses on the user's row and moves past the threshold onto the row of <paramref name="channel"/>.</summary>
        public Point Drag(string nickname, string channel)
        {
            var start = Center(UserRow(nickname));
            var end = Center(ChannelRow(channel));
            Main.MouseDown(start, MouseButton.Left);
            Main.MouseMove(start + new Point(0, ReorderDrag.Threshold + 4), Held);
            Main.MouseMove(end, Held);
            Dispatcher.UIThread.RunJobs();
            return end;
        }
    }
}
