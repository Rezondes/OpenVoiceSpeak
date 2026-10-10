using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OVS.Client.Localization;
using OVS.Client.ViewModels;
using OVS.Client.Views;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Views;

/// <summary>The start card and the header read the connection's state and commands through <c>MainViewModel.Connection</c>.</summary>
[Collection(MotionCollection.Name)]
public sealed class ConnectionBindingTests() : TempDirectoryTest("ovs-connbind-", quiet: true)
{
    MainWindow Open(ServerViewModel? server, out MainViewModel mainViewModel)
    {
        mainViewModel = MainViewModelSetup.Create(dir, action => Dispatcher.UIThread.Post(action));
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
    public void Header_ServerShown_DisconnectButtonIsTheConnectionsDisconnectCommand()
    {
        var main = Open(FakeServers.Admin(), out var mainViewModel);

        Assert.Same(mainViewModel.Connection.DisconnectCommand, FindDisconnectButton(main).Command);
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
}
