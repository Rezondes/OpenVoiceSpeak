using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OVS.Client.Audio;
using OVS.Client.Audio.Devices;
using OVS.Client.Audio.Processing;
using OVS.Client.Audio.Sounds;
using OVS.Client.Input;
using OVS.Client.Logging;
using OVS.Client.Net;
using OVS.Client.Services;
using OVS.Client.Settings;
using OVS.Client.ViewModels;

namespace OVS.Client;

/// <summary>
/// Step 30d4: the client's container. The composition root (App) and the tests make the main view model through here:
/// the container makes its parts (the key poller, the audio engine, the trusted fingerprints, the logo cache, the own tones,
/// the leave timer) on the one clock and the view model on them. The view model disposes the key poller and the engine
/// itself; view models made at runtime keep their factories.
/// </summary>
internal static class ClientServices
{
    /// <summary>A main view model on the profile folder <paramref name="storageDir"/> with its parts made by the container.</summary>
    /// <param name="storageDir">The profile folder.</param>
    /// <param name="post">Runs work on the UI thread.</param>
    /// <param name="time">The client's one clock.</param>
    /// <param name="log">The client's log, also its <see cref="ILogger"/>.</param>
    /// <param name="settings">The settings as loaded.</param>
    /// <param name="settingsWarning">Why the settings file could not be read, null when it could.</param>
    /// <param name="hardware">
    /// The microphones and speakers for the engine and the settings page: the system's in the app, fakes in tests; null
    /// runs without them (--no-audio, tests, debug API with test tone).
    /// </param>
    /// <param name="keyState">The state of a virtual key: <see cref="KeyPoller.IsKeyDown"/> in the app, a fake one in tests.</param>
    /// <param name="connect">How a connection is opened; null: the container's <see cref="ServerConnector"/> (tests pass their own).</param>
    public static MainViewModel CreateMainViewModel(string storageDir, Action<Action> post, TimeProvider time, ClientLog log, ClientSettings settings,
        string? settingsWarning, IAudioHardware? hardware, Func<int, bool> keyState, OpenConnection? connect = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(time);
        services.AddSingleton(log);
        services.AddSingleton<ILogger>(log);
        services.AddSingleton(_ => new KeyPoller(time, keyState));
        services.AddSingleton(_ => new SoundLibrary(storageDir, log)); // holds no handle; the engine plays its tones
        services.AddSingleton(provider => new AudioEngine(provider.GetRequiredService<KeyPoller>(), time, hardware,
            new NativeCaptureFilters(log), provider.GetRequiredService<SoundLibrary>().Samples));
        services.AddSingleton(_ => new TrustedServerFingerprints(storageDir));
        services.AddSingleton(_ => new ServerIconCache(storageDir));
        services.AddSingleton(_ => new LeaveTimer(time, () => post));
        // The certificate question is the view model's hook, which the window sets; asked at connect time, after the view model is made.
        services.AddSingleton(provider => new ServerConnector(storageDir, provider.GetRequiredService<TrustedServerFingerprints>(),
            prompt => provider.GetRequiredService<MainViewModel>().ConfirmTofu(prompt), log, time));
        services.AddSingleton(provider => new MainViewModel(storageDir, post, time, log, settings, settingsWarning,
            provider.GetRequiredService<ServerIconCache>(), provider.GetRequiredService<SoundLibrary>(), provider.GetRequiredService<LeaveTimer>(),
            provider.GetRequiredService<KeyPoller>(), provider.GetRequiredService<AudioEngine>(), connect ?? provider.GetRequiredService<ServerConnector>().OpenAsync,
            hardware is null ? null : hardware.ListActive));
        // The provider is not disposed: the view model owns the key poller and the engine and disposes them itself.
        var provider = services.BuildServiceProvider();
        try
        {
            return provider.GetRequiredService<MainViewModel>();
        }
        catch
        {
            provider.Dispose(); // nobody else owns what was made so far
            throw;
        }
    }
}
