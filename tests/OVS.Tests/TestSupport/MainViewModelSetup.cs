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
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Users;

namespace OVS.Tests.TestSupport;

/// <summary>
/// The one place the tests build a <see cref="MainViewModel"/> (never on the system's audio devices) and give it a server, so a
/// change of its constructor or of where the server lives touches only this file.
/// </summary>
static class MainViewModelSetup
{
    /// <summary>
    /// A main view model on <paramref name="dir"/>, built by <see cref="MainViewModel.Create"/> as the composition root does,
    /// with its log and settings from that folder, on <paramref name="time"/> (the system clock by default).
    /// <paramref name="keyState"/> feeds the key poller; <paramref name="hardware"/> (none by default) the audio engine and the
    /// settings page's device list.
    /// <paramref name="trustServers"/> accepts every server certificate (the TOFU question); <paramref name="dialogs"/> and
    /// <paramref name="server"/> are set before it is returned. <paramref name="connect"/> (none by default: the real connect)
    /// is the connect seam, see <see cref="ConnectSeam"/>.
    /// </summary>
    public static MainViewModel Create(string dir, Action<Action> post, Func<int, bool>? keyState = null, IAudioHardware? hardware = null,
        DialogCallbacks? dialogs = null, bool trustServers = false, ServerViewModel? server = null, TimeProvider? time = null,
        OpenConnection? connect = null)
    {
        time ??= TimeProvider.System;
        var settings = ClientSettings.Load(dir, out var settingsWarning);
        var vm = MainViewModel.Create(dir, post, time, new ClientLog(dir, time), settings, settingsWarning, hardware, keyState, connect);
        if (dialogs is not null) vm.Dialogs = dialogs;
        if (trustServers) vm.ConfirmTofu = _ => Task.FromResult(true);
        if (server is not null) ShowServer(vm, server);
        return vm;
    }

    /// <summary>
    /// The tests' only direct call of the <see cref="MainViewModel"/> constructor, for the tests that give it parts of their
    /// own. The view model disposes <paramref name="keys"/> and <paramref name="audio"/> with itself.
    /// <paramref name="connect"/> is the connect seam (none: the real connect).
    /// </summary>
    public static MainViewModel Compose(string dir, Action<Action> post, TimeProvider time, ClientLog log, ClientSettings settings, string? settingsWarning,
        KnownServers knownServers, ServerIconCache icons, SoundLibrary soundLibrary, LeaveTimer leave, KeyPoller keys, AudioEngine audio,
        Func<AudioDirection, List<AudioDevice>>? listDevices = null, OpenConnection? connect = null) =>
        new(dir, post, time, log, settings, settingsWarning, knownServers, icons, soundLibrary, leave, keys, audio, listDevices, connect);

    /// <summary>The text of the client log files in <paramref name="dir"/>, after the lines still queued were written.</summary>
    public static string ReadLogText(MainViewModel vm, string dir)
    {
        vm.Log.Flush();
        return string.Join(Environment.NewLine, Directory.GetFiles(Path.Combine(dir, "logs"), "client-*.log").Select(path =>
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return new StreamReader(stream).ReadToEnd();
        }));
    }

    /// <summary>The own states a view model sent to its server, oldest first (the fake server records its requests in <paramref name="sent"/>).</summary>
    public static List<SelfState> ReadSelfStatesSent(List<Request> sent) =>
        sent.OfType<SetSelfState>().Select(request => new SelfState(request.Muted, request.Deafened)).ToList();

    /// <summary>Shows <paramref name="server"/> as the connected server (null: none); returns it, like an assignment.</summary>
    public static ServerViewModel? ShowServer(MainViewModel vm, ServerViewModel? server) => vm.Server = server;
}
