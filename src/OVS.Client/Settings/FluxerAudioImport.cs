using System.Text.Json;
using OVS.Client.Models;

namespace OVS.Client.Settings;

/// <summary>
/// Package 171 (A175): Fluxer's desktop app keeps its audio settings as JSON in "VoiceSettings" (older ones in
/// "VoiceSettingsStore"): volumes, filters and whether the sensitivity is automatic. Its device ids are browser hashes,
/// so only "default" can match a device here; it keeps no mode or push-to-talk key there.
/// </summary>
internal sealed class FluxerAudioImport : ChatAppAudioImport
{
    static readonly string[] Origins = ["https://web.fluxer.app"];

    /// <inheritdoc/>
    public override string Name => "Fluxer";

    /// <inheritdoc/>
    protected override IReadOnlyList<string> Folders { get; } = ["fluxer"];

    /// <inheritdoc/>
    protected override ImportedAudio? FromStorage(IReadOnlyDictionary<string, byte[]> storage)
    {
        if ((Text(storage, "VoiceSettings", Origins) ?? Text(storage, "VoiceSettingsStore", Origins)) is not { } json) return null;
        using var document = JsonDocument.Parse(json);
        var voice = document.RootElement;
        if (voice.ValueKind != JsonValueKind.Object) return null;
        return new ImportedAudio(Name)
        {
            InputDeviceId = Word(voice, "inputDeviceId"),
            OutputDeviceId = Word(voice, "outputDeviceId"),
            VadAutomatic = Flag(voice, "vadAutoSensitivity"),
            InputPercent = Number(voice, "inputVolume"),
            OutputPercent = Number(voice, "outputVolume"),
            EchoCancellation = Flag(voice, "echoCancellation"),
            NoiseSuppression = Flag(voice, "noiseSuppression"),
            AutoGain = Flag(voice, "autoGainControl"),
        };
    }
}
