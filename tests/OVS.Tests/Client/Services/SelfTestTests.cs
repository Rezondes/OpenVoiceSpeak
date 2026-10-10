using OVS.Client.Services;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Services;

/// <summary>
/// Package 53: the self test starts and ends the engine's playback of the own voice, shows it on the settings page, logs it and
/// mutes and deafens at the server meanwhile, putting the state from before back at its end. Its engine, page, log and server
/// are fields here, so no engine, window, connection or network is involved.
/// </summary>
public sealed class SelfTestTests
{
    readonly List<bool> engineSwitches = [];
    readonly List<bool> shownOnPage = [];
    readonly List<string> logLines = [];
    readonly List<SelfState> writtenToServer = [];
    bool engineRunning;
    SelfState? serverState = new SelfState(Muted: false, Deafened: false);
    Task writeResult = Task.CompletedTask;

    SelfTest CreateSelfTest() => new(
        readEngineRunning: () => engineRunning,
        setEngineRunning: on =>
        {
            engineRunning = on;
            engineSwitches.Add(on);
        },
        showOnPage: shownOnPage.Add,
        log: logLines.Add,
        readServerState: () => serverState,
        writeServerState: state =>
        {
            writtenToServer.Add(state);
            return writeResult;
        });

    // ---- SetAsync: start ----

    [Fact]
    public async Task SetAsync_On_SwitchesTheEngineOnOnce()
    {
        await CreateSelfTest().SetAsync(true);

        Assert.Equal([true], engineSwitches);
    }

    [Fact]
    public async Task SetAsync_On_ShowsItRunningOnThePage()
    {
        await CreateSelfTest().SetAsync(true);

        Assert.Equal([true], shownOnPage);
    }

    [Fact]
    public async Task SetAsync_On_LogsTheStart()
    {
        await CreateSelfTest().SetAsync(true);

        Assert.Equal(["Self test started"], logLines);
    }

    [Fact]
    public async Task SetAsync_OnWithAServer_MutesAndDeafensAtTheServer()
    {
        await CreateSelfTest().SetAsync(true);

        Assert.Equal([new SelfState(Muted: true, Deafened: true)], writtenToServer);
    }

    [Fact]
    public async Task SetAsync_OnWithAServerMutedOnly_KeepsThatStateAsTheOneBefore()
    {
        serverState = new SelfState(Muted: true, Deafened: false);
        var selfTest = CreateSelfTest();

        await selfTest.SetAsync(true);

        Assert.Equal(new SelfState(Muted: true, Deafened: false), selfTest.StateBefore);
    }

    [Fact]
    public async Task SetAsync_OnWithoutAServer_KeepsNoStateBefore()
    {
        serverState = null;
        var selfTest = CreateSelfTest();

        await selfTest.SetAsync(true);

        Assert.Null(selfTest.StateBefore);
    }

    [Fact]
    public async Task SetAsync_OnWhileTheServerWriteIsPending_CompletesOnlyAfterIt()
    {
        var pending = new TaskCompletionSource();
        writeResult = pending.Task;

        var starting = CreateSelfTest().SetAsync(true);

        Assert.False(starting.IsCompleted);
        pending.SetResult();
        await HangGuard.Bound(starting);
    }

    [Fact]
    public async Task SetAsync_OnWhileAlreadyOn_ChangesNothing()
    {
        engineRunning = true;
        var selfTest = CreateSelfTest();

        await selfTest.SetAsync(true);

        Assert.Equal((0, 0, 0, 0), (engineSwitches.Count, shownOnPage.Count, logLines.Count, writtenToServer.Count));
        Assert.Null(selfTest.StateBefore);
    }

    // ---- SetAsync: end ----

    [Fact]
    public async Task SetAsync_OffAfterOn_SwitchesTheEngineOffAndShowsItOnThePage()
    {
        var selfTest = CreateSelfTest();
        await selfTest.SetAsync(true);
        int switchesBefore = engineSwitches.Count;
        int shownBefore = shownOnPage.Count;

        await selfTest.SetAsync(false);

        Assert.Equal([false], engineSwitches.Skip(switchesBefore));
        Assert.Equal([false], shownOnPage.Skip(shownBefore));
    }

    [Fact]
    public async Task SetAsync_OffAfterOn_LogsTheEnd()
    {
        var selfTest = CreateSelfTest();
        await selfTest.SetAsync(true);
        int linesBefore = logLines.Count;

        await selfTest.SetAsync(false);

        Assert.Equal(["Self test finished"], logLines.Skip(linesBefore));
    }

    [Fact]
    public async Task SetAsync_OffAfterOnWithAServer_PutsTheStateFromBeforeBackNotTheOneNow()
    {
        serverState = new SelfState(Muted: true, Deafened: false);
        var selfTest = CreateSelfTest();
        await selfTest.SetAsync(true);
        serverState = new SelfState(Muted: true, Deafened: true);
        int writesBefore = writtenToServer.Count;

        await selfTest.SetAsync(false);

        Assert.Equal([new SelfState(Muted: true, Deafened: false)], writtenToServer.Skip(writesBefore));
    }

    [Fact]
    public async Task SetAsync_OffAfterOn_ForgetsTheStateBefore()
    {
        var selfTest = CreateSelfTest();
        await selfTest.SetAsync(true);

        await selfTest.SetAsync(false);

        Assert.Null(selfTest.StateBefore);
    }

    [Fact]
    public async Task SetAsync_OffAfterOnWithoutAServer_WritesNothing()
    {
        serverState = null;
        var selfTest = CreateSelfTest();
        await selfTest.SetAsync(true);
        int writesBefore = writtenToServer.Count;

        await selfTest.SetAsync(false);

        Assert.Equal(writesBefore, writtenToServer.Count);
    }

    [Fact]
    public async Task SetAsync_OffWhileTheServerWriteIsPending_CompletesOnlyAfterIt()
    {
        var selfTest = CreateSelfTest();
        await selfTest.SetAsync(true);
        var pending = new TaskCompletionSource();
        writeResult = pending.Task;

        var ending = selfTest.SetAsync(false);

        Assert.False(ending.IsCompleted);
        pending.SetResult();
        await HangGuard.Bound(ending);
    }

    [Fact]
    public async Task SetAsync_OffWhileAlreadyOff_ChangesNothing()
    {
        await CreateSelfTest().SetAsync(false);

        Assert.Equal((0, 0, 0, 0), (engineSwitches.Count, shownOnPage.Count, logLines.Count, writtenToServer.Count));
    }

    // ---- AbandonAsync ----

    [Fact]
    public async Task AbandonAsync_SelfTestRunning_PutsNothingBackAtTheServer()
    {
        var selfTest = CreateSelfTest();
        await selfTest.SetAsync(true);
        int writesBefore = writtenToServer.Count;

        await selfTest.AbandonAsync();

        Assert.Equal(writesBefore, writtenToServer.Count);
    }

    [Fact]
    public async Task AbandonAsync_SelfTestRunning_SwitchesTheEngineOff()
    {
        var selfTest = CreateSelfTest();
        await selfTest.SetAsync(true);
        int switchesBefore = engineSwitches.Count;

        await selfTest.AbandonAsync();

        Assert.Equal([false], engineSwitches.Skip(switchesBefore));
    }

    [Fact]
    public async Task AbandonAsync_SelfTestRunning_LogsTheEnd()
    {
        var selfTest = CreateSelfTest();
        await selfTest.SetAsync(true);
        int linesBefore = logLines.Count;

        await selfTest.AbandonAsync();

        Assert.Equal(["Self test finished"], logLines.Skip(linesBefore));
    }
}
