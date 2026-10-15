using System.Globalization;
using OVS.Client.Input;
using OVS.Client.Localization;
using OVS.Client.Models;
using OVS.Client.ViewModels;

namespace OVS.Client.Services;

/// <summary>
/// Package 171: maps the values found in Discord or Fluxer onto the Audio page. Devices by their Windows id ("default"
/// is the system default); volumes in percent (0 to 200); the threshold in dB (-60 to -10); the release delay in ms
/// (0 to 2000); the push-to-talk key as one key. A value OVS cannot hold is left out, an equal one too.
/// </summary>
internal static class ImportMapping
{
    /// <summary>The changes against what the page shows now.</summary>
    public static ImportPlan Map(ImportedAudio audio, SettingsViewModel page)
    {
        var changes = new List<ImportChange>();
        var notes = new List<string>();
        Device(Strings.Ui_Microphone, audio.InputDeviceId, page.Inputs, page.SelectedInput, (target, option) => target.SelectedInput = option, changes, notes);
        Device(Strings.Ui_Speaker, audio.OutputDeviceId, page.Outputs, page.SelectedOutput, (target, option) => target.SelectedOutput = option, changes, notes);
        if (audio.VoiceActivation is { } voice && voice != page.VoiceActivation)
            changes.Add(new ImportChange(Strings.Import_Mode, Mode(page.VoiceActivation), Mode(voice), target => target.VoiceActivation = voice));
        Switch(Strings.Import_VadAutomatic, audio.VadAutomatic, page.VadAutomatic, (target, on) => target.VadAutomatic = on, changes);
        if (audio.VadAutomatic != true && audio.VadThresholdDb is { } threshold && threshold is >= -60 and <= -10
            && Math.Round(threshold) != Math.Round(page.VadThresholdDb))
        {
            double rounded = Math.Round(threshold);
            changes.Add(new ImportChange(Strings.Ui_ThresholdName, Db(page.VadThresholdDb), Db(rounded), target => target.VadThresholdDb = rounded));
        }
        Percent(Strings.Ui_MicGain, audio.InputPercent, page.InputGainPercent, (target, value) => target.InputGainPercent = value, changes);
        Percent(Strings.Import_SpeakerVolume, audio.OutputPercent, page.OutputVolumePercent, (target, value) => target.OutputVolumePercent = value, changes);
        if (audio.ReleaseDelayMs is { } delay && delay is >= 0 and <= 2000 && delay != (int)Math.Round(page.PttReleaseDelayMs))
            changes.Add(new ImportChange(Strings.Ui_PttReleaseDelay, Ms(page.PttReleaseDelayMs), Ms(delay), target => target.PttReleaseDelayMs = delay));
        Switch(Strings.Ui_NoiseSuppression, audio.NoiseSuppression, page.NoiseSuppression, (target, on) => target.NoiseSuppression = on, changes);
        Switch(Strings.Ui_EchoCancellation, audio.EchoCancellation, page.EchoCancellation, (target, on) => target.EchoCancellation = on, changes);
        Switch(Strings.Ui_AutoGain, audio.AutoGain, page.AutoGain, (target, on) => target.AutoGain = on, changes);
        if (audio.PushToTalkKey is { } key) PushToTalk(new KeyChord(key), page, changes);
        return new ImportPlan(audio.Source, changes, notes);
    }

    static void Device(string name, string? id, IReadOnlyList<AudioDeviceOption> options, AudioDeviceOption? now, Action<SettingsViewModel, AudioDeviceOption> set,
        List<ImportChange> changes, List<string> notes)
    {
        if (id is null) return;
        var wanted = id == ImportedAudio.DefaultDevice ? options[0] : options.FirstOrDefault(option => option.Id == id);
        if (wanted is null)
        {
            notes.Add(TextFormat.Of(Strings.Import_DeviceMissingFmt, name));
            return;
        }
        if (wanted.Id != now?.Id) changes.Add(new ImportChange(name, now?.Name ?? Strings.Device_Default, wanted.Name, target => set(target, wanted)));
    }

    static void Switch(string name, bool? imported, bool now, Action<SettingsViewModel, bool> set, List<ImportChange> changes)
    {
        if (imported is { } on && on != now) changes.Add(new ImportChange(name, OnOff(now), OnOff(on), target => set(target, on)));
    }

    static void Percent(string name, double? imported, double now, Action<SettingsViewModel, double> set, List<ImportChange> changes)
    {
        if (imported is not { } value || value is < 0 or > 200) return;
        double rounded = Math.Round(value);
        if (rounded != Math.Round(now)) changes.Add(new ImportChange(name, Text(now, "0 '%'"), Text(rounded, "0 '%'"), target => set(target, rounded)));
    }

    /// <summary>The key replaces every push-to-talk key of the page; one that is already a push-to-talk key is no change.</summary>
    static void PushToTalk(KeyChord chord, SettingsViewModel page, List<ImportChange> changes)
    {
        var current = page.KeyBindings.Where(item => item.Binding.Action == KeyAction.PushToTalk).Select(item => item.Binding.Chord).ToList();
        if (current.Contains(chord)) return;
        changes.Add(new ImportChange(Strings.Import_PttKey, current.Count == 0 ? Strings.Import_NoKey : string.Join(", ", current.Select(key => key.Name)), chord.Name, target =>
        {
            foreach (var item in target.KeyBindings.Where(item => item.Binding.Action == KeyAction.PushToTalk).ToList()) target.KeyBindings.Remove(item);
            target.KeyBindings.Add(new KeyBindingViewModel(new KeyBinding(KeyAction.PushToTalk, chord), target));
        }));
    }

    static string Mode(bool voiceActivation) => voiceActivation ? Strings.Import_ModeVad : Strings.Import_ModePtt;
    static string OnOff(bool on) => on ? Strings.Import_On : Strings.Import_Off;
    static string Db(double value) => Text(value, "0 'dB'");
    static string Ms(double value) => Text(value, "0 'ms'");
    static string Text(double value, string format) => Math.Round(value).ToString(format, CultureInfo.CurrentCulture);
}
