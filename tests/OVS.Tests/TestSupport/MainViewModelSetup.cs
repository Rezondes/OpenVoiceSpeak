using OVS.Client.Audio.Devices;
using OVS.Client.Dialogs;
using OVS.Client.ViewModels;

namespace OVS.Tests.TestSupport;

/// <summary>
/// The one place the tests build a <see cref="MainViewModel"/> (always without audio devices) and give it a server, so a
/// change of its constructor or of where the server lives touches only this file.
/// </summary>
static class MainViewModelSetup
{
    /// <summary>
    /// A main view model on <paramref name="dir"/>. <paramref name="trustServers"/> accepts every server certificate
    /// (the TOFU question); <paramref name="dialogs"/> and <paramref name="server"/> are set before it is returned.
    /// </summary>
    public static MainViewModel Create(string dir, Action<Action> post, Func<int, bool>? keyState = null, IAudioDeviceFactory? audioDevices = null,
        DialogCallbacks? dialogs = null, bool trustServers = false, ServerViewModel? server = null)
    {
        var vm = new MainViewModel(dir, post, useAudioDevices: false, keyState: keyState, audioDevices: audioDevices);
        if (dialogs is not null) vm.Dialogs = dialogs;
        if (trustServers) vm.ConfirmTofu = _ => Task.FromResult(true);
        if (server is not null) ShowServer(vm, server);
        return vm;
    }

    /// <summary>Shows <paramref name="server"/> as the connected server (null: none); returns it, like an assignment.</summary>
    public static ServerViewModel? ShowServer(MainViewModel vm, ServerViewModel? server) => vm.Server = server;
}
