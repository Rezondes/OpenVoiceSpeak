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

    const string BookmarkName = "Gilde";

    /// <summary>Clicks through <see cref="OverlayRig.Click"/> (the Click event reaches the handlers whether or not the button is enabled); tells whether a handler got it.</summary>
    static bool Click(Button button)
    {
        bool reached = false;
        button.Click += (_, _) => reached = true;
        OverlayRig.Click(button);
        return reached;
    }

    Button AddBookmarkTile(MainWindow main, MainViewModel mainViewModel)
    {
        mainViewModel.Settings.Bookmarks.Add(new Bookmark(BookmarkName, "127.0.0.1", 4711, "anna"));
        mainViewModel.Bookmarks.Refresh();
        Dispatcher.UIThread.RunJobs();
        return main.GetVisualDescendants().OfType<Button>()
            .Single(button => AutomationProperties.GetName(button) == string.Format(Strings.Ui_ConnectToFmt, BookmarkName));
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
