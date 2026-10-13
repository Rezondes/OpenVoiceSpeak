using OVS.Client.Services;
using OVS.Client.ViewModels;
using OVS.Shared.Protocol.Messages;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Services;

/// <summary>
/// What <see cref="MainViewModel"/> hands its <see cref="SelfTest"/> (the engine, the settings page, the log and the shown
/// server) and who ends it (the page's button, leaving the settings, disconnecting): the rules themselves are in
/// <see cref="SelfTestTests"/>, the reconnect in <c>MainViewModelTests</c> (<c>Reconnect_SelfTestRunning_...</c>). Posts run at
/// once, there is no audio hardware.
/// </summary>
public sealed class SelfTestWiringTests() : TempDirectoryTest("ovs-self-test-", quiet: true)
{
    MainViewModel CreateMainViewModel() => MainViewModelSetup.Create(dir, action => action());

    /// <summary>A main view model on the settings page with a shown server that records what it is sent.</summary>
    MainViewModel CreateOnSettingsPage(List<Request> sent)
    {
        var mainViewModel = CreateMainViewModel();
        MainViewModelSetup.ShowServer(mainViewModel, FakeServers.Admin(sent));
        mainViewModel.PageFlow.OpenSettings();
        return mainViewModel;
    }

    [Fact]
    public async Task ToggleSelfTest_SettingsPageOpen_EngineStartsPlayingBackTheOwnVoice()
    {
        await using var mainViewModel = CreateOnSettingsPage([]);

        await mainViewModel.PageFlow.SettingsPage!.ToggleSelfTestCommand.ExecuteAsync(null);

        Assert.True(mainViewModel.Audio.SelfTest);
    }

    [Fact]
    public async Task ToggleSelfTest_SettingsPageOpen_PageShowsItRunning()
    {
        await using var mainViewModel = CreateOnSettingsPage([]);

        await mainViewModel.PageFlow.SettingsPage!.ToggleSelfTestCommand.ExecuteAsync(null);

        Assert.True(mainViewModel.PageFlow.SettingsPage.IsSelfTesting);
    }

    [Fact]
    public async Task ToggleSelfTest_SettingsPageOpen_LogsTheStart()
    {
        await using var mainViewModel = CreateOnSettingsPage([]);

        await mainViewModel.PageFlow.SettingsPage!.ToggleSelfTestCommand.ExecuteAsync(null);

        Assert.Contains("Self test started", MainViewModelSetup.ReadLogText(mainViewModel, dir));
    }

    [Fact]
    public async Task ToggleSelfTest_ServerShown_MutesAndDeafensAtTheServer()
    {
        var sent = new List<Request>();
        await using var mainViewModel = CreateOnSettingsPage(sent);

        await mainViewModel.PageFlow.SettingsPage!.ToggleSelfTestCommand.ExecuteAsync(null);

        Assert.Equal([new SelfState(Muted: true, Deafened: true)], MainViewModelSetup.ReadSelfStatesSent(sent));
    }

    [Fact]
    public async Task ToggleSelfTest_ToggledAgain_EngineAndPageStop()
    {
        await using var mainViewModel = CreateOnSettingsPage([]);
        await mainViewModel.PageFlow.SettingsPage!.ToggleSelfTestCommand.ExecuteAsync(null);

        await mainViewModel.PageFlow.SettingsPage.ToggleSelfTestCommand.ExecuteAsync(null);

        Assert.Equal((false, false), (mainViewModel.Audio.SelfTest, mainViewModel.PageFlow.SettingsPage.IsSelfTesting));
    }

    [Fact]
    public async Task ToggleSelfTest_ToggledAgainOnAServerMutedBefore_ServerGetsThatStateBack()
    {
        var sent = new List<Request>();
        await using var mainViewModel = CreateOnSettingsPage(sent);
        await mainViewModel.Server!.SelfState.SetSelfStateAsync(new SelfState(Muted: true, Deafened: false));
        await mainViewModel.PageFlow.SettingsPage!.ToggleSelfTestCommand.ExecuteAsync(null);
        int sentBefore = MainViewModelSetup.ReadSelfStatesSent(sent).Count;

        await mainViewModel.PageFlow.SettingsPage.ToggleSelfTestCommand.ExecuteAsync(null);

        Assert.Equal([new SelfState(true, false)], MainViewModelSetup.ReadSelfStatesSent(sent).Skip(sentBefore));
    }

    [Fact]
    public async Task ClosePage_SelfTestRunning_EngineStopsPlayingBack()
    {
        await using var mainViewModel = CreateOnSettingsPage([]);
        await mainViewModel.PageFlow.SettingsPage!.ToggleSelfTestCommand.ExecuteAsync(null);

        mainViewModel.PageFlow.CloseWithoutAsking();

        Assert.False(mainViewModel.Audio.SelfTest);
    }

    [Fact]
    public async Task ClosePage_SelfTestRunning_ServerGetsTheStateFromBefore()
    {
        var sent = new List<Request>();
        await using var mainViewModel = CreateOnSettingsPage(sent);
        await mainViewModel.PageFlow.SettingsPage!.ToggleSelfTestCommand.ExecuteAsync(null);
        int sentBefore = MainViewModelSetup.ReadSelfStatesSent(sent).Count;

        mainViewModel.PageFlow.CloseWithoutAsking();

        Assert.Equal([new SelfState(false, false)], MainViewModelSetup.ReadSelfStatesSent(sent).Skip(sentBefore));
    }

    [Fact]
    public async Task DisconnectAsync_SelfTestRunning_EngineStopsPlayingBack()
    {
        await using var mainViewModel = CreateOnSettingsPage([]);
        await mainViewModel.PageFlow.SettingsPage!.ToggleSelfTestCommand.ExecuteAsync(null);

        await mainViewModel.Connection.DisconnectAsync();

        Assert.False(mainViewModel.Audio.SelfTest);
    }

    [Fact]
    public async Task DisconnectAsync_SelfTestRunning_SendsNoStateToTheServerThatIsGoing()
    {
        var sent = new List<Request>();
        await using var mainViewModel = CreateOnSettingsPage(sent);
        await mainViewModel.PageFlow.SettingsPage!.ToggleSelfTestCommand.ExecuteAsync(null);
        int sentBefore = MainViewModelSetup.ReadSelfStatesSent(sent).Count;

        await mainViewModel.Connection.DisconnectAsync();

        Assert.Equal(sentBefore, MainViewModelSetup.ReadSelfStatesSent(sent).Count);
    }
}
