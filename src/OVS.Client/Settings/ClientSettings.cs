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

public sealed class ClientSettings
{
    public const string FileName = "settings.json";
    static readonly JsonSerializerOptions Options = new(ProtocolJson.Options) { WriteIndented = true };

    public List<Bookmark> Bookmarks { get; set; } = [];
    public string? InputDeviceId { get; set; }
    public string? OutputDeviceId { get; set; }
    public float InputGain { get; set; } = 1f;       // 0..2
    public float OutputVolume { get; set; } = 1f;    // 0..1
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

    public ClientSettings Clamp()
    {
        InputGain = Math.Clamp(InputGain, 0f, 2f);
        OutputVolume = Math.Clamp(OutputVolume, 0f, 1f);
        VadThresholdDb = Math.Clamp(VadThresholdDb, -60f, -10f);
        return this;
    }

    /// <summary>Missing file gives defaults. A broken file is kept as .bak and replaced by defaults.</summary>
    public static ClientSettings Load(string directory, out string? warning)
    {
        warning = null;
        var path = Path.Combine(directory, FileName);
        if (!File.Exists(path)) return new ClientSettings();
        try
        {
            return (JsonSerializer.Deserialize<ClientSettings>(File.ReadAllBytes(path), Options) ?? new ClientSettings()).MigrateKeys().Clamp();
        }
        catch (JsonException)
        {
            File.Copy(path, path + ".bak", overwrite: true);
            warning = $"Einstellungen waren beschädigt und wurden zurückgesetzt (Sicherung: {path}.bak).";
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
