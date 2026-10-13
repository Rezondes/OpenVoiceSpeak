using System.Globalization;
using OVS.Client.Localization;
using System.Text.Json;
using System.Text.Json.Serialization;
using OVS.Client.Audio.Sounds;
using OVS.Client.Audio.Transmit;
using OVS.Client.Input;
using OVS.Shared.IO;
using OVS.Shared.Protocol.Framing;

namespace OVS.Client.Settings;

/// <summary>The client's settings, stored as settings.json in the profile.</summary>
internal sealed class ClientSettings : IJsonOnDeserializing
{
    /// <summary>The settings file's name inside the profile.</summary>
    public const string FileName = "settings.json";

    /// <summary>
    /// Package 127 (A135): steps that change a stored choice once, because a new default alone does not reach a file
    /// that stores every key. Step n takes a file from version n - 1 to n; a new step is one more entry.
    /// </summary>
    static readonly Action<ClientSettings>[] Migrations =
    [
        clientSettings => clientSettings.AutoGain = true, // 1 (Package 127, A136): automatic gain on for everyone once
        clientSettings => clientSettings.ResetVolumesAbove100(), // 2 (Package 128, A137): 200 % now means four times
        clientSettings => clientSettings.InferVadAutomatic(), // 3 (Package 129, A138): a tuned threshold stays manual
        clientSettings => clientSettings.Display = DisplayMode.Simplified, // 4 (A114): the simplified display for everyone once
    ];

    /// <summary>The number of migration steps; a new file starts there.</summary>
    public static int CurrentVersion => Migrations.Length;

    /// <summary>Package 127 (A135): the migration steps this file has had; a file without it counts as 0, a new one is current.</summary>
    public int SettingsVersion { get; set; } = CurrentVersion;

    void IJsonOnDeserializing.OnDeserializing() => SettingsVersion = 0;

    /// <summary>Package 114: the settings as they are saved, to tell whether two of them differ.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, ProtocolJson.Indented);

    /// <summary>The saved servers.</summary>
    public List<Bookmark> Bookmarks { get; set; } = [];
    /// <summary>The microphone's id, null for the system default.</summary>
    public string? InputDeviceId { get; set; }
    /// <summary>The speaker's id, null for the system default.</summary>
    public string? OutputDeviceId { get; set; }
    /// <summary>The microphone gain as a slider value, 0 to 2.</summary>
    public float InputGain { get; set; } = 1f;
    /// <summary>The output volume as a slider value, 0 to 2 (Package 55).</summary>
    public float OutputVolume { get; set; } = 1f;
    /// <summary>Push-to-talk or voice activation.</summary>
    public TransmitMode Mode { get; set; } = TransmitMode.PushToTalk;
    /// <summary>No bindings by default (Package 29): a new profile starts without any key.</summary>
    public List<KeyBinding> KeyBindings { get; set; } = [];

    /// <summary>Profiles from before Package 29 stored exactly these two keys. Read once, turned into bindings, never written.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? PttKey { get; set; }

    /// <inheritdoc cref="PttKey"/>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? LinkPttKey { get; set; }

    /// <summary>Package 130 (A139): push-to-talk and link push-to-talk keep sending this long after the key is let go (0..2000).</summary>
    public int PttReleaseDelayMs { get; set; } = 20;

    /// <summary>The chord bound to action, null when none is.</summary>
    public KeyChord? ChordFor(KeyAction action) => KeyBindings.FirstOrDefault(keyBinding => keyBinding.Action == action)?.Chord;
    /// <summary>The voice activation threshold a new profile starts with.</summary>
    public const float DefaultVadThresholdDb = -40f;
    /// <summary>The manual voice activation threshold in dB, -60 to -10.</summary>
    public float VadThresholdDb { get; set; } = DefaultVadThresholdDb;
    /// <summary>Package 129 (A138): the voice gate learns the room's noise and sets its threshold itself.</summary>
    public bool VadAutomatic { get; set; } = true;

    /// <summary>
    /// Package 129 (A138): settings that do not say whether the voice gate is automatic (a file from before, a debug body
    /// without the switch) are automatic only with the default threshold; a threshold tuned by hand stays manual.
    /// </summary>
    internal void InferVadAutomatic() => VadAutomatic = VadThresholdDb == DefaultVadThresholdDb;
    /// <summary>Package 123 (A131): RNNoise on the sent signal; on also for a file from before without the key.</summary>
    public bool NoiseSuppression { get; set; } = true;
    /// <summary>Package 123 (A131): raises a quiet microphone toward -20 dBFS; Package 127 (A136): on by default.</summary>
    public bool AutoGain { get; set; } = true;
    /// <summary>Package 140 (A149): WebRTC's echo canceller on the sent signal; on also for a file from before without the key.</summary>
    public bool EchoCancellation { get; set; } = true;
    /// <summary>Package 142 (A151): speech by default, also for a file from before without the key.</summary>
    [JsonPropertyName("transmission")] // the key of the settings file: it keeps the name the property had
    public AudioContent AudioContent { get; set; } = AudioContent.Voice;
    /// <summary>The colour theme.</summary>
    public AppTheme Theme { get; set; } = AppTheme.System;
    /// <summary>Package 45: follows Windows unless set; takes effect at the next start.</summary>
    public AppLanguage Language { get; set; } = AppLanguage.System;
    /// <summary>Package 43: look for a newer release at start (A40).</summary>
    public bool CheckForUpdates { get; set; } = true;
    /// <summary>Package 47: "Alle Sounds aus" is SoundsEnabled = false.</summary>
    public bool SoundsEnabled { get; set; } = true;
    /// <summary>The sounds' volume, 0 to 1.</summary>
    public float SoundVolume { get; set; } = 0.8f;
    /// <summary>Package 48: only sounds that differ from the default are stored.</summary>
    public Dictionary<SoundEvent, SoundSetting> Sounds { get; set; } = [];

    /// <summary>The setting of sound, the default when none is stored.</summary>
    public SoundSetting SoundFor(SoundEvent sound) => Sounds.GetValueOrDefault(sound) ?? DefaultSound(sound);

    /// <summary>Package 131 (A140): the talk key tones are off unless turned on, so nobody's client suddenly beeps.</summary>
    public static SoundSetting DefaultSound(SoundEvent sound) => new(Muted: sound is SoundEvent.TalkKeyDown or SoundEvent.TalkKeyUp);

    /// <summary>Package 51: the volume per person by fingerprint (so on every server), only what differs from 100 %.</summary>
    public Dictionary<string, float> UserVolumes { get; set; } = [];
    /// <summary>The highest per-person volume.</summary>
    public const float MaxUserVolume = 2f;

    /// <summary>The volume of the person with fingerprint, 1 by default.</summary>
    public float VolumeFor(string fingerprint) => UserVolumes.GetValueOrDefault(fingerprint, 1f);

    /// <summary>Sets a person's volume; 1 removes the entry.</summary>
    public void SetVolume(string fingerprint, float volume)
    {
        volume = Math.Clamp(volume, 0f, MaxUserVolume);
        if (volume == 1f) UserVolumes.Remove(fingerprint);
        else UserVolumes[fingerprint] = volume;
    }

    void ResetVolumesAbove100()
    {
        OutputVolume = Math.Min(OutputVolume, 1f);
        foreach (var (fingerprint, volume) in UserVolumes.ToList()) SetVolume(fingerprint, Math.Min(volume, 1f));
    }

    /// <summary>Package 61: opacity of the large backgrounds (0 to 1) and whether what shows through is blurred.</summary>
    public float BackgroundOpacity { get; set; } = 1f;
    /// <summary>What shows through the backgrounds is blurred.</summary>
    public bool BlurBackground { get; set; }

    /// <summary>Package 99 (A114): today's plain look by default, or the animated display. A file from before has no key and loads simplified.</summary>
    public DisplayMode Display { get; set; } = DisplayMode.Simplified;

    /// <summary>Brings every value into its range and returns these settings.</summary>
    public ClientSettings Clamp()
    {
        BackgroundOpacity = Math.Clamp(BackgroundOpacity, 0f, 1f);
        InputGain = Math.Clamp(InputGain, 0f, 2f);
        OutputVolume = Math.Clamp(OutputVolume, 0f, 2f);
        SoundVolume = Math.Clamp(SoundVolume, 0f, 1f);
        UserVolumes ??= [];
        foreach (var (fingerprint, volume) in UserVolumes.ToList()) SetVolume(fingerprint, volume);
        VadThresholdDb = Math.Clamp(VadThresholdDb, -60f, -10f);
        PttReleaseDelayMs = Math.Clamp(PttReleaseDelayMs, 0, 2000);
        return this;
    }

    /// <summary>Missing file gives defaults. A broken file is kept as .bak and replaced by defaults.</summary>
    public static ClientSettings Load(string directory, out string? warning)
    {
        warning = null;
        var path = Path.Combine(directory, FileName);
        if (!File.Exists(path)) // Package 59: a new profile gets a push-to-talk key, an existing one keeps its choice
            return new ClientSettings { KeyBindings = [new KeyBinding(KeyAction.PushToTalk, DefaultPushToTalk.Choose(DefaultPushToTalk.CountMouseButtons()))] };
        try
        {
            return (JsonSerializer.Deserialize<ClientSettings>(File.ReadAllBytes(path), ProtocolJson.Indented) ?? new ClientSettings()).MigrateKeys().Clamp().Migrate(directory);
        }
        catch (JsonException)
        {
            File.Copy(path, path + ".bak", overwrite: true);
            warning = TextFormat.Of(Strings.Settings_Damaged, path);
            return new ClientSettings();
        }
    }

    /// <summary>An existing profile keeps its two keys (A25), now as bindings.</summary>
    ClientSettings MigrateKeys()
    {
        if (KeyBindings.Count == 0)
        {
            if (PttKey is int ptt && ptt > 0) KeyBindings.Add(new KeyBinding(KeyAction.PushToTalk, new KeyChord(ptt)));
            if (LinkPttKey is int link && link > 0 && link != PttKey) KeyBindings.Add(new KeyBinding(KeyAction.LinkPushToTalk, new KeyChord(link)));
        }
        PttKey = null;
        LinkPttKey = null;
        return this;
    }

    /// <summary>Package 127 (A135): runs every missing step once, in order, and saves the file with the new version.</summary>
    ClientSettings Migrate(string directory)
    {
        if (SettingsVersion >= CurrentVersion) return this; // current, or written by a newer client: left as it is
        for (SettingsVersion = Math.Max(SettingsVersion, 0); SettingsVersion < CurrentVersion; SettingsVersion++) Migrations[SettingsVersion](this);
        try
        {
            Save(directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // not saved: the steps run again at the next start, with the same result
        }
        return this;
    }

    /// <summary>Writes settings.json into directory atomically.</summary>
    public void Save(string directory)
    {
        Directory.CreateDirectory(directory);
        SafeFile.WriteAtomic(Path.Combine(directory, FileName), JsonSerializer.SerializeToUtf8Bytes(this, ProtocolJson.Indented), flushToDisk: false);
    }
}
