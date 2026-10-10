namespace OVS.Client.Services;

/// <summary>
/// Package 53: during the self test one is muted and deafened, at the server too (others see "Ton aus"), and hears only the
/// own voice. Afterwards the state from before comes back. It reaches the engine, the settings page, the server and the log
/// through delegates, so it knows no view model.
/// </summary>
internal sealed class SelfTest
{
    readonly Func<bool> readEngineRunning;
    readonly Action<bool> setEngineRunning;
    readonly Action<bool> showOnPage;
    readonly Action<string> log;
    readonly Func<SelfState?> readServerState;
    readonly Func<SelfState, Task> writeServerState;

    /// <param name="readEngineRunning">Whether the engine plays the own voice back.</param>
    /// <param name="setEngineRunning">Switches the engine's playback of the own voice.</param>
    /// <param name="showOnPage">Shows the state on the settings page, when it is open.</param>
    /// <param name="log">Where the start and the end are written.</param>
    /// <param name="readServerState">The own mute and deafen at the connected server; null without a server.</param>
    /// <param name="writeServerState">Sets the own mute and deafen at the connected server; completes at once without a server.</param>
    internal SelfTest(Func<bool> readEngineRunning, Action<bool> setEngineRunning, Action<bool> showOnPage, Action<string> log,
        Func<SelfState?> readServerState, Func<SelfState, Task> writeServerState)
    {
        this.readEngineRunning = readEngineRunning;
        this.setEngineRunning = setEngineRunning;
        this.showOnPage = showOnPage;
        this.log = log;
        this.readServerState = readServerState;
        this.writeServerState = writeServerState;
    }

    /// <summary>The mute and deafen from before the test, while it runs and a server was there; null otherwise.</summary>
    internal SelfState? StateBefore { get; private set; }

    /// <summary>Package 53: starts or ends the test; ending it puts the state from before back at the server.</summary>
    internal async Task SetAsync(bool on)
    {
        if (on == readEngineRunning()) return;
        setEngineRunning(on);
        showOnPage(on);
        log(on ? "Self test started" : "Self test finished");
        if (on)
        {
            StateBefore = readServerState();
            await writeServerState(new SelfState(Muted: true, Deafened: true));
        }
        else
        {
            var before = StateBefore;
            StateBefore = null;
            if (before is { } state) await writeServerState(state);
        }
    }

    /// <summary>The server is gone: there is nothing to put back there.</summary>
    internal void ForgetStateBefore() => StateBefore = null;
}
