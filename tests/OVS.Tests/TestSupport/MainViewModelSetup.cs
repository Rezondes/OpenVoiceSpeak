using OVS.Client.Audio;
using OVS.Client.Audio.Devices;
using OVS.Client.Audio.Sounds;
using OVS.Client.Dialogs;
using OVS.Client.Input;
using OVS.Client.Logging;
using OVS.Client.Net;
using OVS.Client.Services;
using OVS.Client.Settings;
using OVS.Client.ViewModels;

namespace OVS.Tests.TestSupport;

/// <summary>
/// The one place the tests build a <see cref="MainViewModel"/> (always without audio devices) and give it a server, so a
/// change of its constructor or of where the server lives touches only this file.
/// </summary>
static class MainViewModelSetup
{
    /// <summary>
    /// A main view model on <paramref name="dir"/>, built by <see cref="MainViewModel.Create"/> as the composition root does,
    /// with its log and settings from that folder, on <paramref name="time"/> (the system clock by default).
    /// <paramref name="keyState"/> feeds the key poller, <paramref name="audioDevices"/> the audio engine.
    /// <paramref name="trustServers"/> accepts every server certificate (the TOFU question); <paramref name="dialogs"/> and
    /// <paramref name="server"/> are set before it is returned.
    /// </summary>
    public static MainViewModel Create(string dir, Action<Action> post, Func<int, bool>? keyState = null, IAudioDeviceFactory? audioDevices = null,
        DialogCallbacks? dialogs = null, bool trustServers = false, ServerViewModel? server = null, TimeProvider? time = null)
    {
        time ??= TimeProvider.System;
        var settings = ClientSettings.Load(dir, out var settingsWarning);
        var vm = MainViewModel.Create(dir, post, time, new ClientLog(dir, time), settings, settingsWarning, useAudioDevices: false, keyState, audioDevices);
        if (dialogs is not null) vm.Dialogs = dialogs;
        if (trustServers) vm.ConfirmTofu = _ => Task.FromResult(true);
        if (server is not null) ShowServer(vm, server);
        return vm;
    }

    /// <summary>
    /// The tests' only direct call of the <see cref="MainViewModel"/> constructor, for the tests that give it parts of their
    /// own. The view model disposes <paramref name="keys"/> and <paramref name="audio"/> with itself.
    /// </summary>
    public static MainViewModel Compose(string dir, Action<Action> post, TimeProvider time, ClientLog log, ClientSettings settings, string? settingsWarning,
        KnownServers knownServers, ServerIconCache icons, SoundLibrary sounds, LeaveTimer leave, KeyPoller keys, AudioEngine audio) =>
        new(dir, post, time, log, settings, settingsWarning, knownServers, icons, sounds, leave, keys, audio);

    /// <summary>Shows <paramref name="server"/> as the connected server (null: none); returns it, like an assignment.</summary>
    public static ServerViewModel? ShowServer(MainViewModel vm, ServerViewModel? server) => vm.Server = server;
}
