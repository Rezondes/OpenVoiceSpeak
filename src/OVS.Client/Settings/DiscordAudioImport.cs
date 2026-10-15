using System.Text.Json;
using OVS.Client.Models;

namespace OVS.Client.Settings;

/// <summary>
/// Package 171 (A174): Discord's desktop app (also PTB and Canary) keeps its audio settings as JSON in "MediaEngineStore"
/// (the "default" context: mode, threshold, volumes, devices, filters) and its keys in "keybinds" (an enabled
/// PUSH_TO_TALK binding of one keyboard key).
/// </summary>
internal sealed class DiscordAudioImport : ChatAppAudioImport
{
    static readonly string[] Origins = ["https://discord.com", "https://ptb.discord.com", "https://canary.discord.com", "https://discordapp.com"];

    /// <inheritdoc/>
    public override string Name => "Discord";

    /// <inheritdoc/>
    protected override IReadOnlyList<string> Folders { get; } = ["discord", "discordptb", "discordcanary"];

    /// <inheritdoc/>
    protected override ImportedAudio? FromStorage(IReadOnlyDictionary<string, byte[]> storage)
    {
        if (Text(storage, "MediaEngineStore", Origins) is not { } json) return null;
        using var document = JsonDocument.Parse(json);
        if (Child(document.RootElement, "default") is not { } media) return null;
        var options = Child(media, "modeOptions") ?? default;
        bool? plainNoise = Flag(media, "noiseSuppression"), krisp = Flag(media, "noiseCancellation");
        return new ImportedAudio(Name)
        {
            InputDeviceId = Word(media, "inputDeviceId"),
            OutputDeviceId = Word(media, "outputDeviceId"),
            VoiceActivation = Word(media, "mode") switch
            {
                "VOICE_ACTIVITY" => true,
                "PUSH_TO_TALK" => false,
                _ => null,
            },
            VadAutomatic = Flag(options, "autoThreshold"),
            VadThresholdDb = Number(options, "threshold"),
            ReleaseDelayMs = Number(options, "delay") is { } delay ? (int)Math.Round(delay) : null,
            InputPercent = Number(media, "inputVolume"),
            OutputPercent = Number(media, "outputVolume"),
            EchoCancellation = Flag(media, "echoCancellation"),
            NoiseSuppression = plainNoise is null && krisp is null ? null : plainNoise == true || krisp == true, // either kind
            AutoGain = Flag(media, "automaticGainControl"),
            PushToTalkKey = Text(storage, "keybinds", Origins) is { } keybinds ? PushToTalkKey(keybinds) : null,
        };
    }

    /// <summary>The key of an enabled PUSH_TO_TALK binding that is one keyboard key ([0, virtual key, ...]); mouse buttons and chords stay out.</summary>
    static int? PushToTalkKey(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (Child(document.RootElement, "_state") is not { } state) return null;
        foreach (var binding in state.EnumerateObject().Select(property => property.Value).Where(value => value.ValueKind == JsonValueKind.Object))
        {
            if (Word(binding, "action") != "PUSH_TO_TALK" || Flag(binding, "enabled") == false) continue;
            if (!binding.TryGetProperty("shortcut", out var shortcut) || shortcut.ValueKind != JsonValueKind.Array || shortcut.GetArrayLength() != 1) continue;
            var key = shortcut[0];
            if (key.ValueKind == JsonValueKind.Array && key.GetArrayLength() >= 2 && key[0].ValueKind == JsonValueKind.Number && key[0].GetDouble() == 0
                && key[1].ValueKind == JsonValueKind.Number && key[1].TryGetInt32(out int code) && code is > 0 and < 255)
                return code;
        }
        return null;
    }
}
