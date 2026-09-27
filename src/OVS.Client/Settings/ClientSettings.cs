using System.Text.Json;
using OVS.Client.Audio;
using OVS.Client.Input;
using OVS.Client.Net;
using OVS.Shared.Protocol;

namespace OVS.Client.Settings;

public sealed record Bookmark(string Name, string Host, int Port, string Nickname);

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
    public int PttKey { get; set; } = KeyPoller.VkXButton1;
    public int LinkPttKey { get; set; } = KeyPoller.VkXButton2;
    public float VadThresholdDb { get; set; } = -40f; // -60..-10

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
            return (JsonSerializer.Deserialize<ClientSettings>(File.ReadAllBytes(path), Options) ?? new ClientSettings()).Clamp();
        }
        catch (JsonException)
        {
            File.Copy(path, path + ".bak", overwrite: true);
            warning = $"Einstellungen waren beschädigt und wurden zurückgesetzt (Sicherung: {path}.bak).";
            return new ClientSettings();
        }
    }

    public void Save(string directory)
    {
        Directory.CreateDirectory(directory);
        ClientStorage.WriteAtomic(Path.Combine(directory, FileName), JsonSerializer.SerializeToUtf8Bytes(this, Options));
    }
}
