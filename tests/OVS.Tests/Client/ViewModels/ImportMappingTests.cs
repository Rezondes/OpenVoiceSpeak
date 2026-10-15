using OVS.Client.Audio.Devices;
using OVS.Client.Audio.Transmit;
using OVS.Client.Input;
using OVS.Client.Localization;
using OVS.Client.Models;
using OVS.Client.Services;
using OVS.Client.Settings;
using OVS.Client.ViewModels;

namespace OVS.Tests.Client.ViewModels;

/// <summary>Package 171: Discord's values onto the Audio page's scales; equal ones and ones OVS cannot hold stay out.</summary>
public sealed class ImportMappingTests
{
    static SettingsViewModel Page(ClientSettings? settings = null) =>
        new(settings ?? new ClientSettings(), [new AudioDevice("mic-1", "Headset"), new AudioDevice("mic-2", "Studio")], [new AudioDevice("out-1", "Boxen")], TimeProvider.System);

    static ImportedAudio Everything() => new("Discord")
    {
        InputDeviceId = "mic-2", OutputDeviceId = "out-1", VoiceActivation = true, VadAutomatic = false, VadThresholdDb = -50.4,
        InputPercent = 150, OutputPercent = 80, ReleaseDelayMs = 300, NoiseSuppression = false, EchoCancellation = false, AutoGain = false,
        PushToTalkKey = 0x56,
    };

    [Fact]
    public void Map_EveryValue_OntoOvsScales()
    {
        var page = Page();
        var plan = ImportMapping.Map(Everything(), page);
        Assert.Equal([Strings.Ui_Microphone, Strings.Ui_Speaker, Strings.Import_Mode, Strings.Import_VadAutomatic, Strings.Ui_ThresholdName, Strings.Ui_MicGain,
            Strings.Import_SpeakerVolume, Strings.Ui_PttReleaseDelay, Strings.Ui_NoiseSuppression, Strings.Ui_EchoCancellation, Strings.Ui_AutoGain, Strings.Import_PttKey],
            plan.Changes.Select(change => change.Name));
        Assert.Equal((Strings.Device_Default, "Studio"), (plan.Changes[0].Before, plan.Changes[0].After));

        foreach (var change in plan.Changes) change.Apply(page);

        var taken = page.ToSettings(new ClientSettings());
        Assert.Equal(("mic-2", "out-1", TransmitMode.VoiceActivation, false, -50f), (taken.InputDeviceId, taken.OutputDeviceId, taken.Mode, taken.VadAutomatic, taken.VadThresholdDb));
        Assert.Equal((1.5f, 0.8f, 300, false, false, false), (taken.InputGain, taken.OutputVolume, taken.PttReleaseDelayMs, taken.NoiseSuppression, taken.EchoCancellation, taken.AutoGain));
        Assert.Equal(new KeyChord(0x56), Assert.Single(taken.KeyBindings, binding => binding.Action == KeyAction.PushToTalk).Chord);
        Assert.Empty(ImportMapping.Map(Everything(), page).Changes); // taken over: nothing left to change
    }

    [Fact]
    public void Map_EqualValues_NoChange()
    {
        var settings = new ClientSettings { InputDeviceId = "mic-1", NoiseSuppression = true, InputGain = 1.004f };
        var plan = ImportMapping.Map(new ImportedAudio("Discord") { InputDeviceId = "mic-1", NoiseSuppression = true, InputPercent = 100.2 }, Page(settings));
        Assert.Empty(plan.Changes);
        Assert.Empty(plan.Notes);
    }

    [Fact]
    public void Map_UnknownDevice_LeftOutWithNote()
    {
        var plan = ImportMapping.Map(new ImportedAudio("Discord") { InputDeviceId = "{0.0.1.00000000}.{weg}", OutputDeviceId = ImportedAudio.DefaultDevice },
            Page(new ClientSettings { OutputDeviceId = "out-1" }));

        Assert.Equal(TextFormat.Of(Strings.Import_DeviceMissingFmt, Strings.Ui_Microphone), Assert.Single(plan.Notes));
        var speaker = Assert.Single(plan.Changes);
        Assert.Equal((Strings.Ui_Speaker, "Boxen", Strings.Device_Default), (speaker.Name, speaker.Before, speaker.After));
    }

    [Fact]
    public void Map_UnsupportedValue_LeftOut()
    {
        var plan = ImportMapping.Map(new ImportedAudio("Discord")
        {
            VadAutomatic = false, VadThresholdDb = -80, InputPercent = 250, OutputPercent = -1, ReleaseDelayMs = 5000,
        }, Page(new ClientSettings { VadAutomatic = false }));
        Assert.Empty(plan.Changes);

        // an automatic threshold makes the manual one meaningless
        Assert.DoesNotContain(ImportMapping.Map(new ImportedAudio("Discord") { VadAutomatic = true, VadThresholdDb = -30 }, Page()).Changes,
            change => change.Name == Strings.Ui_ThresholdName);
    }
}
