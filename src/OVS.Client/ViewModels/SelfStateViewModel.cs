using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OVS.Client.Audio.Sounds;
using OVS.Client.Services;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Users;

namespace OVS.Client.ViewModels;

/// <summary>
/// The own mute and deafen on the connected server (Packages 53, 125 and 139), bound with the two buttons: what the user
/// chose, what a held push-to-mute key did, and the mute from before deafening. Every change goes to the server as one
/// <see cref="SetSelfState"/>.
/// </summary>
internal sealed partial class SelfStateViewModel : ObservableObject
{
    readonly Func<Request, Task> send;
    readonly Action<SoundEvent> requestSound;
    bool mutedBeforeDeafen;
    /// <summary>Package 125: push-to-mute muted, so letting go unmutes. A choice made otherwise meanwhile clears it and sticks.</summary>
    bool mutedByHold;

    [ObservableProperty] bool selfMuted;
    [ObservableProperty] bool selfDeafened;

    /// <param name="send">Sends a request to the server (the server view model's send).</param>
    /// <param name="requestSound">Plays a tone (the button tones).</param>
    internal SelfStateViewModel(Func<Request, Task> send, Action<SoundEvent> requestSound)
    {
        this.send = send;
        this.requestSound = requestSound;
    }

    /// <summary>Package 139 (review): a reconnect brings back the own choice, not the mute of a key that may be let go meanwhile.</summary>
    internal bool MutedByHold => mutedByHold;
    /// <summary>Package 139 (review): undeafening after a reconnect must not turn on a microphone that was muted before.</summary>
    internal bool MutedBeforeDeafen => mutedBeforeDeafen;

    /// <summary>Package 53: both at once and without tones, for the self test and to restore the state before it.</summary>
    internal Task SetSelfStateAsync(SelfState state)
    {
        mutedByHold = false;
        if (state.Deafened && !SelfDeafened) mutedBeforeDeafen = SelfMuted;
        SelfDeafened = state.Deafened;
        SelfMuted = state.Muted || state.Deafened;
        return send(new SetSelfState(SelfMuted, SelfDeafened));
    }

    /// <summary>
    /// Package 139 (review): the own state from the connection before, in one SetSelfState and without tones. A mute of the
    /// key still held now (Package 125) stays, so letting go unmutes as usual.
    /// </summary>
    internal Task RestoreSelfStateAsync(SelfState state, bool mutedBeforeDeafen)
    {
        if (state.IsQuiet) mutedByHold = false; // a chosen mute outlasts the key's release
        this.mutedBeforeDeafen = mutedBeforeDeafen;
        SelfDeafened = state.Deafened;
        SelfMuted = state.Muted || state.Deafened || mutedByHold;
        return send(new SetSelfState(SelfMuted, SelfDeafened));
    }

    /// <summary>
    /// Package 125: holding push-to-mute does what the mute button does (tone, server), letting go undoes exactly that.
    /// Already muted or deafened, it changes nothing.
    /// </summary>
    public Task HoldMuteAsync(bool held)
    {
        if (held ? SelfMuted : !mutedByHold) return Task.CompletedTask;
        mutedByHold = held;
        SelfMuted = held;
        requestSound(held ? SoundEvent.MicOff : SoundEvent.MicOn);
        return send(new SetSelfState(SelfMuted, SelfDeafened));
    }

    [RelayCommand]
    Task ToggleMuteAsync()
    {
        mutedByHold = false;
        if (SelfDeafened)
        {
            SelfDeafened = false;
            SelfMuted = false;
            requestSound(SoundEvent.SoundOn);
        }
        else
        {
            SelfMuted = !SelfMuted;
            requestSound(SelfMuted ? SoundEvent.MicOff : SoundEvent.MicOn);
        }
        return send(new SetSelfState(SelfMuted, SelfDeafened));
    }

    [RelayCommand]
    Task ToggleDeafenAsync()
    {
        mutedByHold = false;
        if (SelfDeafened)
        {
            SelfDeafened = false;
            SelfMuted = mutedBeforeDeafen;
        }
        else
        {
            mutedBeforeDeafen = SelfMuted;
            SelfDeafened = true;
            SelfMuted = true;
        }
        requestSound(SelfDeafened ? SoundEvent.SoundOff : SoundEvent.SoundOn);
        return send(new SetSelfState(SelfMuted, SelfDeafened));
    }
}
