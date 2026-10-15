using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OVS.Client.Localization;
using OVS.Client.Services;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Client.Views;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Views;

/// <summary>The start card and the header read the connection's state and commands through <c>MainViewModel.Connection</c>.</summary>
[Collection(MotionCollection.Name)]
public sealed class ConnectionBindingTests() : TempDirectoryTest("ovs-connbind-", quiet: true)
{
    MainWindow Open(ServerViewModel? server, out MainViewModel mainViewModel, OpenConnection? connect = null)
    {
        mainViewModel = MainViewModelSetup.Create(dir, action => Dispatcher.UIThread.Post(action), connect: connect);
        var main = new MainWindow { DataContext = mainViewModel, Width = 1100, Height = 700 };
        main.Show();
        MainViewModelSetup.ShowServer(mainViewModel, server);
        Dispatcher.UIThread.RunJobs();
        return main;
    }

    static Button FindConnectButton(MainWindow main) => main.GetVisualDescendants().OfType<Button>()
        .Single(button => button.Classes.Contains("accent") && button.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == Strings.Ui_ConnectMenu));

    static Button FindDisconnectButton(MainWindow main) => main.GetVisualDescendants().OfType<Button>()
        .Single(button => button.IsEffectivelyVisible && AutomationProperties.GetName(button) == Strings.Ui_Disconnect);

    [AvaloniaFact]
    public void StartCard_NotConnected_CancelButtonIsTheConnectionsCancelReconnectCommand()
    {
        var main = Open(null, out var mainViewModel);

        Assert.Same(mainViewModel.Connection.CancelReconnectCommand, main.FindControl<Button>("CancelReconnect")!.Command);
        main.Close();
    }

    [AvaloniaFact]
    public void StartCard_ConnectionIsConnecting_ConnectButtonIsDisabled()
    {
        var main = Open(null, out var mainViewModel);

        mainViewModel.Connection.IsConnecting = true;
        Dispatcher.UIThread.RunJobs();

        Assert.False(FindConnectButton(main).IsEnabled);
        main.Close();
    }

    [AvaloniaFact]
    public void Header_ServerShown_DisconnectButtonIsTheConnectionsConfirmDisconnectCommand()
    {
        var main = Open(FakeServers.Admin(), out var mainViewModel);

        Assert.Same(mainViewModel.Connection.ConfirmDisconnectCommand, FindDisconnectButton(main).Command);
        main.Close();
    }

    [AvaloniaFact]
    public void Header_PingTextChanges_ChipShowsIt()
    {
        var main = Open(FakeServers.Admin(), out var mainViewModel);

        mainViewModel.Connection.PingText = "Ping 23 ms";
        Dispatcher.UIThread.RunJobs();

        var wide = main.FindControl<Border>("PingChip")!.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Classes.Contains("wideOnly"));
        Assert.Equal("Ping 23 ms", wide.Text);
        main.Close();
    }

    [AvaloniaFact]
    public void Header_PingTextChanges_TooltipShowsIt()
    {
        var main = Open(FakeServers.Admin(), out var mainViewModel);

        mainViewModel.Connection.PingText = "Ping 23 ms";
        var chip = main.FindControl<Border>("PingChip")!;
        ToolTip.SetIsOpen(chip, true);
        Dispatcher.UIThread.RunJobs();

        var tip = (StackPanel)ToolTip.GetTip(chip)!;
        Assert.Equal("Ping 23 ms", tip.Children.OfType<TextBlock>().Last().Text);
        main.Close();
    }

    // ---- Package 170: server links ----

    static List<string?> Texts(MainWindow main) => main.Overlay.GetVisualDescendants().OfType<TextBox>().Select(box => box.Text).ToList();

    [AvaloniaFact]
    public void Link_OpensDialogFilled_NoConnect()
    {
        int connects = 0;
        var main = Open(null, out var mainViewModel, connect: (_, _) =>
        {
            connects++;
            return Task.FromException<OVS.Client.Net.ClientConnection>(new IOException("never"));
        });
        mainViewModel.Settings.Bookmarks.Add(new Bookmark(BookmarkName, "127.0.0.1", 4711, "anna"));
        mainViewModel.Bookmarks.Refresh();

        Assert.True(OVS.Client.Net.JoinLink.TryParse("ovs://gilde.example.org:7000", out var link));
        main.OpenJoinLink(link);
        Dispatcher.UIThread.RunJobs();
        Assert.True(main.Overlay.IsOpen);
        Assert.Contains("gilde.example.org", Texts(main));
        Assert.Contains("anna", Texts(main)); // the nickname of the first bookmark, none from the link
        Assert.Equal(7000m, main.Overlay.GetVisualDescendants().OfType<NumericUpDown>().Single().Value);
        main.Overlay.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.True(OVS.Client.Net.JoinLink.TryParse("ovs://127.0.0.1:4711", out var known)); // the address of a bookmark: that bookmark
        main.OpenJoinLink(known);
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("127.0.0.1", Texts(main));
        main.Overlay.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, connects);
        main.Close();
    }

    [AvaloniaFact]
    public void Header_ServerLinkKnown_CopyButtonCopiesIt()
    {
        var server = FakeServers.Admin();
        var main = Open(server, out _);
        var button = main.FindControl<OVS.Client.Controls.CopyButton>("CopyServerLink")!;
        Assert.False(button.IsVisible); // no address that a link can carry

        server.ServerLink = "https://rezondes.github.io/OpenVoiceSpeak/?join=gilde.example.org:7000";
        Dispatcher.UIThread.RunJobs();

        Assert.True(button.IsVisible);
        Assert.Equal((server.ServerLink, Strings.Ui_CopyServerLink), (button.Text, AutomationProperties.GetName(button)));
        main.Close();
    }

    const string BookmarkName = "Gilde";

    /// <summary>Clicks through <see cref="OverlayRig.Click"/> (the Click event reaches the handlers whether or not the button is enabled); tells whether a handler got it.</summary>
    static bool Click(Button button)
    {
        bool reached = false;
        button.Click += (_, _) => reached = true;
        OverlayRig.Click(button);
        return reached;
    }

    static Button AddBookmarkTile(MainWindow main, MainViewModel mainViewModel)
    {
        mainViewModel.Settings.Bookmarks.Add(new Bookmark(BookmarkName, "127.0.0.1", 4711, "anna"));
        mainViewModel.Bookmarks.Refresh();
        Dispatcher.UIThread.RunJobs();
        return main.GetVisualDescendants().OfType<Button>()
            .Single(button => AutomationProperties.GetName(button) == TextFormat.Of(Strings.Ui_ConnectToFmt, BookmarkName));
    }

    [AvaloniaFact]
    public void ConnectClick_NotConnecting_OpensTheConnectDialog()
    {
        var main = Open(null, out _);
        try
        {
            Assert.True(Click(FindConnectButton(main)));

            Assert.True(main.Overlay.IsOpen);
            main.Overlay.Close();
            Dispatcher.UIThread.RunJobs();
        }
        finally
        {
            main.Close();
        }
    }

    [AvaloniaFact]
    public void ConnectClick_WhileConnecting_OpensNoDialog()
    {
        var main = Open(null, out var mainViewModel);
        try
        {
            mainViewModel.Connection.IsConnecting = true;
            Dispatcher.UIThread.RunJobs();

            Assert.True(Click(FindConnectButton(main)));

            Assert.False(main.Overlay.IsOpen);
        }
        finally
        {
            main.Close();
        }
    }

    [AvaloniaFact]
    public async Task BookmarkClick_NotConnecting_StartsAnAttempt()
    {
        var seam = new ConnectSeam(dir, TimeProvider.System);
        seam.FailNext(ConnectSeam.CreateClosedPortError());
        var main = Open(null, out var mainViewModel, seam.Connect);
        try
        {
            var tile = AddBookmarkTile(main, mainViewModel);

            Assert.True(Click(tile));

            var attempt = await HangGuard.BoundAsync(seam.WaitForAttemptAsync(1));
            Assert.Equal(4711, attempt.Choice.Port);
        }
        finally
        {
            main.Close();
        }
    }

    [AvaloniaFact]
    public void BookmarkClick_WhileConnecting_StartsNoAttempt()
    {
        var seam = new ConnectSeam(dir, TimeProvider.System);
        var main = Open(null, out var mainViewModel, seam.Connect);
        try
        {
            var tile = AddBookmarkTile(main, mainViewModel);
            mainViewModel.Connection.IsConnecting = true;
            Dispatcher.UIThread.RunJobs();

            Assert.True(Click(tile));

            Assert.Empty(seam.Attempts);
        }
        finally
        {
            main.Close();
        }
    }
}
