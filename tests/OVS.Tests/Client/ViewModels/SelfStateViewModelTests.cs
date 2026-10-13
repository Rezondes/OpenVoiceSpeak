using OVS.Client.Audio.Sounds;
using OVS.Client.Localization;
using OVS.Client.Services;
using OVS.Client.ViewModels;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Users;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.ViewModels;

/// <summary>The own mute and deafen on their own (Step 27f): push-to-mute, deafening, the self test's state and a reconnect's restore.</summary>
public sealed class SelfStateViewModelTests
{
    readonly List<Request> sent = [];
    readonly List<SoundEvent> tones = [];
    readonly SelfStateViewModel state;

    public SelfStateViewModelTests()
    {
        state = new SelfStateViewModel(request =>
        {
            sent.Add(request);
            return Task.CompletedTask;
        }, tones.Add);
    }

    SetSelfState LastSent => Assert.IsType<SetSelfState>(sent[^1]);

    [Fact]
    public async Task HoldMute_ReleasedWithoutAHold_SendsNothing()
    {
        await HangGuard.BoundAsync(state.HoldMuteAsync(false));

        Assert.Empty(sent);
    }

    [Fact]
    public async Task HoldMute_AlreadyMuted_ChangesNothing()
    {
        await HangGuard.BoundAsync(state.ToggleMuteCommand.ExecuteAsync(null));
        sent.Clear();

        await HangGuard.BoundAsync(state.HoldMuteAsync(true));

        Assert.Empty(sent);
        Assert.False(state.MutedByHold);
    }

    [Fact]
    public async Task HoldMute_HeldThenToggled_ReleaseKeepsTheChoice()
    {
        await HangGuard.BoundAsync(state.HoldMuteAsync(true));
        await HangGuard.BoundAsync(state.ToggleMuteCommand.ExecuteAsync(null)); // unmutes and clears the hold
        await HangGuard.BoundAsync(state.ToggleMuteCommand.ExecuteAsync(null)); // a chosen mute
        sent.Clear();

        await HangGuard.BoundAsync(state.HoldMuteAsync(false));

        Assert.True(state.SelfMuted);
        Assert.Empty(sent);
    }

    [Fact]
    public async Task ToggleDeafen_Twice_BringsBackTheMuteFromBefore()
    {
        await HangGuard.BoundAsync(state.ToggleMuteCommand.ExecuteAsync(null));
        await HangGuard.BoundAsync(state.ToggleDeafenCommand.ExecuteAsync(null));

        await HangGuard.BoundAsync(state.ToggleDeafenCommand.ExecuteAsync(null));

        Assert.Equal((true, false), (state.SelfMuted, state.SelfDeafened));
        Assert.Equal(new SetSelfState(true, false), LastSent with { RequestId = null });
        Assert.Equal(SoundEvent.SoundOn, tones[^1]);
    }

    [Fact]
    public async Task SetSelfStateAsync_DeafenedWhileMuted_RemembersMutedBeforeDeafen()
    {
        await HangGuard.BoundAsync(state.ToggleMuteCommand.ExecuteAsync(null));
        tones.Clear();

        await HangGuard.BoundAsync(state.SetSelfStateAsync(new SelfState(false, true)));

        Assert.True(state.MutedBeforeDeafen);
        Assert.Empty(tones);
    }

    [Fact]
    public async Task RestoreSelfStateAsync_KeyStillHeld_StaysMuted()
    {
        await HangGuard.BoundAsync(state.HoldMuteAsync(true));

        await HangGuard.BoundAsync(state.RestoreSelfStateAsync(new SelfState(false, false), mutedBeforeDeafen: false));

        Assert.True(state.SelfMuted);
        Assert.True(state.MutedByHold);
    }

    [Fact]
    public async Task RestoreSelfStateAsync_QuietState_ClearsTheHold()
    {
        await HangGuard.BoundAsync(state.HoldMuteAsync(true));

        await HangGuard.BoundAsync(state.RestoreSelfStateAsync(new SelfState(true, false), mutedBeforeDeafen: false));

        Assert.False(state.MutedByHold);
        Assert.True(state.SelfMuted); // the chosen mute, no longer the key's
    }

    /// <summary>Through the server's send, which catches a lost connection: the state stays toggled and the notice shows.</summary>
    [Fact]
    public async Task ToggleMute_SendThrows_StateStaysToggledAndTheNoticeShows()
    {
        var server = FakeServers.Admin(reply: _ => throw new IOException("Verbindung weg"));
        var notices = new List<string>();
        server.Notice += notices.Add;

        await HangGuard.BoundAsync(server.SelfState.ToggleMuteCommand.ExecuteAsync(null));

        Assert.True(server.SelfState.SelfMuted);
        Assert.Equal([ErrorTexts.For(ReasonCodes.ConnectionLost)], notices);
    }
}
