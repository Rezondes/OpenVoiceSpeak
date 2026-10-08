using System.Reflection;
using OVS.Client.Audio;
using OVS.Client.Audio.Capture;
using OVS.Client.Audio.Transmit;
using OVS.Client.Input;
using OVS.Client.Settings;

namespace OVS.Tests.Client.Audio;

/// <summary>The engine's settings mapping: every setting the audio takes is copied in one place, for the live preview and on save alike.</summary>
public class AudioEngineSettingsTests
{
    /// <summary>The capture settings: each one's change (away from the default), where the pipeline holds it, and the value it holds then.</summary>
    static readonly Dictionary<string, (Action<ClientSettings> Change, Func<CapturePipeline, object> Read, object Value)> CaptureReadouts = new()
    {
        [nameof(ClientSettings.InputGain)] = (s => s.InputGain = 0.5f, p => p.Gain, 0.5f),
        [nameof(ClientSettings.VadThresholdDb)] = (s => s.VadThresholdDb = -25f, p => p.Vad.ThresholdDb, -25f),
        [nameof(ClientSettings.VadAutomatic)] = (s => s.VadAutomatic = false, p => p.Vad.Automatic, false),
        [nameof(ClientSettings.NoiseSuppression)] = (s => s.NoiseSuppression = false, p => p.NoiseSuppression, false),
        [nameof(ClientSettings.AutoGain)] = (s => s.AutoGain = false, p => p.AutoGain, false),
        [nameof(ClientSettings.EchoCancellation)] = (s => s.EchoCancellation = false, p => p.EchoCancellation, false),
        [nameof(ClientSettings.AudioContent)] = (s => s.AudioContent = AudioContent.Music, p => p.Music, true),
    };

    /// <summary>
    /// The settings the engine itself takes besides the capture's: each one's change, where the engine or its keys hold
    /// it, and the value it holds then, taken from the changed settings.
    /// </summary>
    static readonly Dictionary<string, (Action<ClientSettings> Change, Func<AudioEngine, KeyPoller, object> Read, Func<ClientSettings, object> Value)> EngineReadouts = new()
    {
        [nameof(ClientSettings.Mode)] = (s => s.Mode = TransmitMode.VoiceActivation, (e, _) => e.Mode, s => s.Mode),
        [nameof(ClientSettings.OutputVolume)] = (s => s.OutputVolume = 0.3f, (e, _) => e.Mixer.Volume, s => s.OutputVolume),
        [nameof(ClientSettings.PttReleaseDelayMs)] = (s => s.PttReleaseDelayMs = 300, (_, k) => k.ReleaseDelay, s => TimeSpan.FromMilliseconds(s.PttReleaseDelayMs)),
    };

    /// <summary>
    /// Settings the engine's apply leaves alone: the devices are opened by id, the sound settings are read per tone
    /// (<see cref="OVS.Client.Audio.Sounds.SoundPlayer.GainFor"/>), the rest is not the engine's.
    /// </summary>
    static readonly string[] NotApplied =
    [
        nameof(ClientSettings.SettingsVersion), nameof(ClientSettings.Bookmarks), nameof(ClientSettings.InputDeviceId),
        nameof(ClientSettings.OutputDeviceId), nameof(ClientSettings.KeyBindings), nameof(ClientSettings.PttKey),
        nameof(ClientSettings.LinkPttKey), nameof(ClientSettings.Theme), nameof(ClientSettings.Language),
        nameof(ClientSettings.CheckForUpdates), nameof(ClientSettings.SoundsEnabled), nameof(ClientSettings.SoundVolume),
        nameof(ClientSettings.Sounds), nameof(ClientSettings.UserVolumes), nameof(ClientSettings.BackgroundOpacity),
        nameof(ClientSettings.BlurBackground), nameof(ClientSettings.Display),
    ];

    public static TheoryData<string> CaptureSettingNames => new(CaptureReadouts.Keys);

    /// <summary>A new setting has to be placed: copied onto the capture, taken by the engine, or neither.</summary>
    [Fact]
    public void ClientSettings_EveryProperty_IsClassifiedOnce()
    {
        var properties = typeof(ClientSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is { IsPublic: true }).Select(p => p.Name).Order().ToList();
        var placed = CaptureReadouts.Keys.Concat(EngineReadouts.Keys).Concat(NotApplied).Order().ToList();
        Assert.Equal(properties, placed); // a name twice would show here too
    }

    [Theory]
    [MemberData(nameof(CaptureSettingNames))]
    public void ApplyCaptureSettings_EachCaptureSetting_SetsThePipelineValue(string setting)
    {
        var (change, read, value) = CaptureReadouts[setting];
        var changed = new ClientSettings();
        change(changed);
        using var pipeline = CapturePipeline.Manual();
        AudioEngine.ApplyCaptureSettings(pipeline, new ClientSettings());
        var defaults = read(pipeline);
        Assert.NotEqual(value, defaults); // the defaults hold another value, so the change below is seen

        AudioEngine.ApplyCaptureSettings(pipeline, changed);
        Assert.Equal(value, read(pipeline));

        AudioEngine.ApplyCaptureSettings(pipeline, new ClientSettings());
        Assert.Equal(defaults, read(pipeline));
    }

    /// <summary>The settings page's live preview reaches the running test tone's capture and the engine, without a restart.</summary>
    [Fact]
    public void ApplyLive_ToneRunning_TakesEveryMappedSettingWithoutRestart()
    {
        using var keys = new KeyPoller(TimeProvider.System, keyState: _ => false);
        using var engine = new AudioEngine(keys, TimeProvider.System, hardware: null);
        engine.Configure(new ClientSettings());
        engine.DeviceLifecycle.SetTone(440);
        var capture = engine.DeviceLifecycle.CaptureSource.Capture!;
        AssertHoldsNoneChanged(engine, keys); // a default changed to a value below would leave nothing to see

        engine.ApplyLive(CreateAllChanged());

        Assert.Same(capture, engine.DeviceLifecycle.CaptureSource.Capture);
        AssertTakesAllChanged(engine, keys);
    }

    /// <summary>Saved settings reach the engine and the test tone's capture that the save opens anew.</summary>
    [Fact]
    public void Configure_ToneRunning_TakesEveryMappedSetting()
    {
        using var keys = new KeyPoller(TimeProvider.System, keyState: _ => false);
        using var engine = new AudioEngine(keys, TimeProvider.System, hardware: null);
        engine.DeviceLifecycle.SetTone(440);
        AssertHoldsNoneChanged(engine, keys);

        engine.Configure(CreateAllChanged());

        AssertTakesAllChanged(engine, keys);
    }

    /// <summary>A save after the live preview counts, not the preview's values: the reopened capture takes the saved ones.</summary>
    [Fact]
    public void Configure_AfterLivePreview_TakesTheSavedSettings()
    {
        using var keys = new KeyPoller(TimeProvider.System, keyState: _ => false);
        using var engine = new AudioEngine(keys, TimeProvider.System, hardware: null);
        engine.Configure(new ClientSettings());
        engine.DeviceLifecycle.SetTone(440);
        engine.ApplyLive(new ClientSettings());
        var previewed = engine.DeviceLifecycle.CaptureSource.Capture;

        engine.Configure(CreateAllChanged());

        Assert.NotSame(previewed, engine.DeviceLifecycle.CaptureSource.Capture);
        AssertTakesAllChanged(engine, keys);
    }

    static ClientSettings CreateAllChanged()
    {
        var settings = new ClientSettings();
        foreach (var (change, _, _) in CaptureReadouts.Values) change(settings);
        foreach (var (change, _, _) in EngineReadouts.Values) change(settings);
        return settings;
    }

    static void AssertTakesAllChanged(AudioEngine engine, KeyPoller keys)
    {
        var capture = engine.DeviceLifecycle.CaptureSource.Capture!;
        var changed = CreateAllChanged();
        foreach (var (setting, (_, read, value)) in CaptureReadouts)
            Assert.Equal((setting, value), (setting, read(capture)));
        foreach (var (setting, (_, read, value)) in EngineReadouts)
            Assert.Equal((setting, value(changed)), (setting, read(engine, keys)));
    }

    static void AssertHoldsNoneChanged(AudioEngine engine, KeyPoller keys)
    {
        var capture = engine.DeviceLifecycle.CaptureSource.Capture!;
        var changed = CreateAllChanged();
        foreach (var (setting, (_, read, value)) in CaptureReadouts)
            Assert.NotEqual((setting, value), (setting, read(capture)));
        foreach (var (setting, (_, read, value)) in EngineReadouts)
            Assert.NotEqual((setting, value(changed)), (setting, read(engine, keys)));
    }
}
