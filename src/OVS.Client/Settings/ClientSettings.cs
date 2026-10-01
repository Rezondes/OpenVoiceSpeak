using OVS.Client.Localization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OVS.Client.Audio;
using OVS.Client.Input;
using OVS.Client.Net;
using OVS.Shared.Protocol;

namespace OVS.Client.Settings;

/// <param name="ProtectedPassword">Package 39: the server password, DPAPI-protected for the current Windows user.</param>
public sealed record Bookmark(string Name, string Host, int Port, string Nickname, string? ProtectedPassword = null)
{
    public bool HasSavedPassword => SavedPassword() is not null;

    /// <summary>Null when none is stored or it cannot be read here (another Windows user or PC, damaged value).</summary>
    public string? SavedPassword() => PasswordProtector.Unprotect(ProtectedPassword);
}

/// <summary>Windows DPAPI for the current user (A35): readable only by this Windows account on this PC.</summary>
public static class PasswordProtector
{
    static readonly byte[] Entropy = "OpenVoiceSpeak server password"u8.ToArray();

    public static string Protect(string password)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Passwörter lassen sich nur unter Windows speichern.");
        return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(password), Entropy, DataProtectionScope.CurrentUser));
    }

    public static string? Unprotect(string? protectedPassword)
    {
        if (string.IsNullOrEmpty(protectedPassword) || !OperatingSystem.IsWindows()) return null;
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedPassword), Entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception e) when (e is CryptographicException or FormatException)
        {
            return null;
        }
    }
}

public enum AppTheme { System, Light, Dark }

/// <summary>Package 99: "Animierte Darstellung" (default) or "Vereinfachte Darstellung" (the plain look from before).</summary>
public enum DisplayMode { Animated, Simplified }

/// <param name="File">Package 48: an own tone, a bare file name in the profile's "sounds" folder.</param>
public sealed record SoundSetting(float Volume = 1f, bool Muted = false, string? File = null)
{
    public bool IsDefault => Volume >= 1f && !Muted && File is null;
}

public sealed class ClientSettings
{
    public const string FileName = "settings.json";
    static readonly JsonSerializerOptions Options = new(ProtocolJson.Options) { WriteIndented = true };

    /// <summary>Package 114: the settings as they are saved, to tell whether two of them differ.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public List<Bookmark> Bookmarks { get; set; } = [];
    public string? InputDeviceId { get; set; }
    public string? OutputDeviceId { get; set; }
    public float InputGain { get; set; } = 1f;       // 0..2
    public float OutputVolume { get; set; } = 1f;    // 0..2 (Package 55)
    public TransmitMode Mode { get; set; } = TransmitMode.PushToTalk;
    /// <summary>No bindings by default (Package 29): a new profile starts without any key.</summary>
    public List<KeyBinding> KeyBindings { get; set; } = [];

    /// <summary>Profiles from before Package 29 stored exactly these two keys. Read once, turned into bindings, never written.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? PttKey { get; set; }

    /// <inheritdoc cref="PttKey"/>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? LinkPttKey { get; set; }

    public KeyChord? ChordFor(KeyAction action) => KeyBindings.FirstOrDefault(b => b.Action == action)?.Chord;
    public float VadThresholdDb { get; set; } = -40f; // -60..-10
    public AppTheme Theme { get; set; } = AppTheme.System;
    /// <summary>Package 45: follows Windows unless set; takes effect at the next start.</summary>
    public AppLanguage Language { get; set; } = AppLanguage.System;
    /// <summary>Package 43: look for a newer release at start (A40).</summary>
    public bool CheckForUpdates { get; set; } = true;
    /// <summary>Package 47: "Alle Sounds aus" is SoundsEnabled = false.</summary>
    public bool SoundsEnabled { get; set; } = true;
    public float SoundVolume { get; set; } = 0.8f;   // 0..1
    /// <summary>Package 48: only sounds that differ from the default are stored.</summary>
    public Dictionary<SoundEvent, SoundSetting> Sounds { get; set; } = [];

    public SoundSetting SoundFor(SoundEvent sound) => Sounds.GetValueOrDefault(sound) ?? new SoundSetting();

    /// <summary>Package 51: the volume per person by fingerprint (so on every server), only what differs from 100 %.</summary>
    public Dictionary<string, float> UserVolumes { get; set; } = [];
    public const float MaxUserVolume = 2f;

    public float VolumeFor(string fingerprint) => UserVolumes.GetValueOrDefault(fingerprint, 1f);

    public void SetVolume(string fingerprint, float volume)
    {
        volume = Math.Clamp(volume, 0f, MaxUserVolume);
        if (volume == 1f) UserVolumes.Remove(fingerprint);
        else UserVolumes[fingerprint] = volume;
    }

    /// <summary>Package 61: opacity of the large backgrounds (0 to 1) and whether what shows through is blurred.</summary>
    public float BackgroundOpacity { get; set; } = 1f;
    public bool BlurBackground { get; set; }

    /// <summary>Package 99 (A114): the animated display, or today's plain look. A file from before has no key and loads animated.</summary>
    public DisplayMode Display { get; set; } = DisplayMode.Animated;

    public ClientSettings Clamp()
    {
        BackgroundOpacity = Math.Clamp(BackgroundOpacity, 0f, 1f);
        InputGain = Math.Clamp(InputGain, 0f, 2f);
        OutputVolume = Math.Clamp(OutputVolume, 0f, 2f);
        SoundVolume = Math.Clamp(SoundVolume, 0f, 1f);
        UserVolumes ??= [];
        foreach (var (fingerprint, volume) in UserVolumes.ToList()) SetVolume(fingerprint, volume);
        VadThresholdDb = Math.Clamp(VadThresholdDb, -60f, -10f);
        return this;
    }

    /// <summary>Missing file gives defaults. A broken file is kept as .bak and replaced by defaults.</summary>
    public static ClientSettings Load(string directory, out string? warning)
    {
        warning = null;
        var path = Path.Combine(directory, FileName);
        if (!File.Exists(path)) // Package 59: a new profile gets a push-to-talk key, an existing one keeps its choice
            return new ClientSettings { KeyBindings = [new KeyBinding(KeyAction.PushToTalk, DefaultKeys.PushToTalk(KeyPoller.MouseButtonCount()))] };
        try
        {
            return (JsonSerializer.Deserialize<ClientSettings>(File.ReadAllBytes(path), Options) ?? new ClientSettings()).MigrateKeys().Clamp();
        }
        catch (JsonException)
        {
            File.Copy(path, path + ".bak", overwrite: true);
            warning = string.Format(Strings.Settings_Damaged, path);
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

    public void Save(string directory)
    {
        Directory.CreateDirectory(directory);
        ClientStorage.WriteAtomic(Path.Combine(directory, FileName), JsonSerializer.SerializeToUtf8Bytes(this, Options));
    }
}

/// <summary>Package 61: how see-through the window's backgrounds are; Package 99: and whether the display is animated.</summary>
public sealed record BackgroundAppearance(float Opacity, bool Blur, DisplayMode Display = DisplayMode.Animated)
{
    public static BackgroundAppearance From(ClientSettings settings) => new(settings.BackgroundOpacity, settings.BlurBackground, settings.Display);
}
