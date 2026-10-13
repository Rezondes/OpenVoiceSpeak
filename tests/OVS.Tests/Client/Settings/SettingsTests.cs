using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging.Abstractions;
using OVS.Client.Audio;
using OVS.Client.Audio.Devices;
using OVS.Client.Audio.Processing;
using OVS.Client.Audio.Sounds;
using OVS.Client.Audio.Transmit;
using OVS.Client.Input;
using OVS.Client.Localization;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Shared.Protocol;
using OVS.Tests.Client.Audio.Processing;
using OVS.Tests.TestSupport;
using Key = Avalonia.Input.Key;
using PhysicalKey = Avalonia.Input.PhysicalKey;
using RawInputModifiers = Avalonia.Input.RawInputModifiers;

namespace OVS.Tests.Client.Settings;

public sealed class SettingsTests() : TempDirectoryTest("ovs-settings-", quiet: false)
{
    [Fact]
    public void Load_Missing_Defaults()
    {
        var clientSettings = ClientSettings.Load(dir, out var warning);
        Assert.Null(warning);
        Assert.Equal(TransmitMode.PushToTalk, clientSettings.Mode);
        Assert.Equal(-40f, clientSettings.VadThresholdDb);
        Assert.Empty(clientSettings.Bookmarks);
    }

    /// <summary>Package 39: the password is stored DPAPI-protected, never as plain text.</summary>
    [Fact]
    public void Password_Saved_ProtectedWithoutPlainText()
    {
        var clientSettings = new ClientSettings();
        clientSettings.Bookmarks.Add(new Bookmark("Gilde", "voice.example.org", 7000, "ich", PasswordProtector.Protect("streng-geheim")));
        clientSettings.Save(dir);
        Assert.DoesNotContain("streng-geheim", File.ReadAllText(Path.Combine(dir, ClientSettings.FileName)));

        var loaded = ClientSettings.Load(dir, out _).Bookmarks.Single();
        Assert.Equal("streng-geheim", loaded.SavedPassword());
        Assert.True(loaded.HasSavedPassword);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("kein base64 !")]
    [InlineData("AAAAAAAA")]
    public void Password_Garbage_TreatedAsNone(string? stored)
    {
        var bookmark = new Bookmark("Gilde", "h", 1, "ich", stored);
        Assert.Null(bookmark.SavedPassword());
        Assert.False(bookmark.HasSavedPassword);
    }

    [Fact]
    public void Sounds_GlobalSettings_RoundTrip()
    {
        var settings = Vm(new ClientSettings());
        Assert.Equal((false, 80.0), (settings.AllSoundsOff, Math.Round(settings.SoundVolumePercent)));
        settings.AllSoundsOff = true;
        settings.SoundVolumePercent = 150; // clamped
        var saved = settings.ToSettings(new ClientSettings());
        saved.Save(dir);
        var loaded = ClientSettings.Load(dir, out _);
        Assert.Equal((false, 1f), (loaded.SoundsEnabled, loaded.SoundVolume));
    }

    /// <summary>Package 48: per sound volume, mute and own file survive a restart; "Zurücksetzen" lets the copy go.</summary>
    [Fact]
    public void Sounds_PerEventSettings_RoundTripAndResetDeletesCopy()
    {
        var wav = Path.Combine(dir, "eigener.wav");
        using (var writer = new NAudio.Wave.WaveFileWriter(wav, NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(48000, 1)))
            writer.WriteSamples(Enumerable.Repeat(0.2f, 4800).ToArray(), 0, 4800);
        var settings = Vm(new ClientSettings());
        settings.ImportSound = (path, sound) => SoundImport.Prepare(path, dir, sound);
        Assert.Equal(Enum.GetValues<SoundEvent>().Length, settings.SoundRows.Count);
        Assert.Equal("Standardton", settings.SoundRows[0].SourceText);

        var joined = settings.SoundRows.Single(soundRow => soundRow.Event == SoundEvent.UserJoined);
        joined.Import(wav);
        joined.VolumePercent = 50;
        settings.SoundRows.Single(soundRow => soundRow.Event == SoundEvent.UserLeft).Muted = true;
        Assert.Equal("Eigene Datei", joined.SourceText);
        var saved = settings.ToSettings(new ClientSettings());
        Assert.Equal(2, saved.Sounds.Count); // only what differs from the default
        saved.Save(dir);
        var loaded = ClientSettings.Load(dir, out _);
        Assert.Equal((0.5f, false), (loaded.SoundFor(SoundEvent.UserJoined).Volume, loaded.SoundFor(SoundEvent.UserJoined).Muted));
        Assert.True(loaded.SoundFor(SoundEvent.UserLeft).Muted);
        var copy = Path.Combine(dir, SoundLibrary.Folder, loaded.SoundFor(SoundEvent.UserJoined).File!);
        Assert.True(File.Exists(copy));

        var again = Vm(loaded);
        again.SoundRows.Single(soundRow => soundRow.Event == SoundEvent.UserJoined).ResetCommand.Execute(null);
        var reset = again.ToSettings(loaded);
        Assert.Null(reset.SoundFor(SoundEvent.UserJoined).File);
        new SoundLibrary(dir, NullLogger.Instance).CleanUp(reset); // what saving the settings does
        Assert.False(File.Exists(copy));
    }

    /// <summary>Package 51: per person by fingerprint, only what differs from 100 %, bounded to 0 to 200 %.</summary>
    [Fact]
    public void UserVolumes_Saved_OnlyNonDefaultAndClamped()
    {
        var clientSettings = new ClientSettings();
        clientSettings.SetVolume("fpA", 1.5f);
        clientSettings.SetVolume("fpB", 1f);
        clientSettings.SetVolume("fpD", 0f);
        clientSettings.UserVolumes["fpC"] = 7f; // e.g. edited by hand
        clientSettings.Clamp().Save(dir);
        var json = File.ReadAllText(Path.Combine(dir, ClientSettings.FileName));
        Assert.DoesNotContain("fpB", json);

        var loaded = ClientSettings.Load(dir, out _);
        Assert.Equal(1.5f, loaded.VolumeFor("fpA"));
        Assert.Equal(2f, loaded.VolumeFor("fpC"));
        Assert.Equal(0f, loaded.VolumeFor("fpD"));
        Assert.Equal(1f, loaded.VolumeFor("fpB"));
        Assert.Equal(1f, loaded.VolumeFor("unbekannt"));
        loaded.SetVolume("fpA", 1f); // back to 100 %: the entry goes
        Assert.False(loaded.UserVolumes.ContainsKey("fpA"));
        Assert.Equal(2f, Vm(loaded).ToSettings(loaded).VolumeFor("fpC")); // saving the settings page keeps them
    }

    /// <summary>Package 56: each chat sound has its own row; muting one leaves the others alone.</summary>
    [Fact]
    public void Sounds_ChatRows_Independent()
    {
        var settings = Vm(new ClientSettings());
        var rows = settings.SoundRows.ToDictionary(soundRow => soundRow.Event);
        Assert.Equal(("Nachricht in Allgemein", "Nachricht im Channel"), (rows[SoundEvent.ServerMessage].Label, rows[SoundEvent.ChannelMessage].Label));
        rows[SoundEvent.ServerMessage].Muted = true;
        rows[SoundEvent.ChannelMessage].VolumePercent = 40;
        var saved = settings.ToSettings(new ClientSettings());
        Assert.Equal(0f, SoundPlayer.GainFor(saved, SoundEvent.ServerMessage));
        Assert.Equal(saved.SoundVolume * 0.4f, SoundPlayer.GainFor(saved, SoundEvent.ChannelMessage), 3);
        Assert.Equal(saved.SoundVolume, SoundPlayer.GainFor(saved, SoundEvent.PrivateMessage), 3);
    }

    /// <summary>Package 60: the own link tone has its own row, independent of the one for others.</summary>
    [Fact]
    public void Sounds_LinkRows_Independent()
    {
        var settings = Vm(new ClientSettings());
        var rows = settings.SoundRows.ToDictionary(soundRow => soundRow.Event);
        Assert.Equal(("Sprache über Link", "Eigene Sprache über Link"), (rows[SoundEvent.LinkVoice].Label, rows[SoundEvent.OwnLinkVoice].Label));
        rows[SoundEvent.OwnLinkVoice].Muted = true;
        var saved = settings.ToSettings(new ClientSettings());
        Assert.Equal(0f, SoundPlayer.GainFor(saved, SoundEvent.OwnLinkVoice));
        Assert.Equal(saved.SoundVolume, SoundPlayer.GainFor(saved, SoundEvent.LinkVoice), 3);
    }

    /// <summary>Package 73: the two group tones have their own rows, independent of each other.</summary>
    [Fact]
    public void Sounds_GroupRows_Independent()
    {
        var settings = Vm(new ClientSettings());
        var rows = settings.SoundRows.ToDictionary(soundRow => soundRow.Event);
        Assert.Equal(("Eigene Gruppe geändert", "Gruppe vergeben oder entzogen"), (rows[SoundEvent.GroupChanged].Label, rows[SoundEvent.GroupChangedByMe].Label));
        rows[SoundEvent.GroupChanged].Muted = true;
        rows[SoundEvent.GroupChangedByMe].VolumePercent = 30;
        var saved = settings.ToSettings(new ClientSettings());
        Assert.Equal(0f, SoundPlayer.GainFor(saved, SoundEvent.GroupChanged));
        Assert.Equal(saved.SoundVolume * 0.3f, SoundPlayer.GainFor(saved, SoundEvent.GroupChangedByMe), 3);
    }

    [Fact]
    public void Language_RoundTrip_ShownInSettings()
    {
        new ClientSettings { Language = OVS.Client.Localization.AppLanguage.English }.Save(dir);
        var loaded = ClientSettings.Load(dir, out _);
        Assert.Equal(OVS.Client.Localization.AppLanguage.English, loaded.Language);
        var settings = Vm(loaded);
        Assert.Equal("English", settings.SelectedLanguage.Label);
        settings.SelectedLanguage = SettingsViewModel.Languages.First(); // "Wie Windows"
        Assert.Equal(OVS.Client.Localization.AppLanguage.System, settings.ToSettings(loaded).Language);
    }

    [Fact]
    public void FullSettings_RoundTrip_IncludingBookmarks()
    {
        var clientSettings = new ClientSettings
        {
            Bookmarks = [new Bookmark("Clan", "voice.example.org", 7000, "anna")],
            InputDeviceId = "in",
            OutputDeviceId = "out",
            InputGain = 1.5f,
            OutputVolume = 0.5f,
            Mode = TransmitMode.VoiceActivation,
            KeyBindings =
            [
                new KeyBinding(KeyAction.PushToTalk, new KeyChord(0x70)),
                new KeyBinding(KeyAction.ToggleMute, new KeyChord(0x4D, ChordModifiers.Ctrl | ChordModifiers.Shift)),
            ],
            VadThresholdDb = -30,
        };
        clientSettings.Save(dir);
        var loaded = ClientSettings.Load(dir, out _);
        Assert.Equal(clientSettings.Bookmarks, loaded.Bookmarks);
        Assert.Equal((clientSettings.InputDeviceId, clientSettings.OutputDeviceId, clientSettings.InputGain, clientSettings.OutputVolume, clientSettings.Mode, clientSettings.VadThresholdDb),
            (loaded.InputDeviceId, loaded.OutputDeviceId, loaded.InputGain, loaded.OutputVolume, loaded.Mode, loaded.VadThresholdDb));
        Assert.Equal(clientSettings.KeyBindings, loaded.KeyBindings);
    }

    [Fact]
    public void Load_Corrupt_DefaultsAndBackup()
    {
        var path = Path.Combine(dir, ClientSettings.FileName);
        File.WriteAllText(path, "{ kaputt");
        var clientSettings = ClientSettings.Load(dir, out var warning);
        Assert.NotNull(warning);
        Assert.Equal(TransmitMode.PushToTalk, clientSettings.Mode);
        Assert.Equal("{ kaputt", File.ReadAllText(path + ".bak"));
    }

    [Fact]
    public void Load_OutOfRange_Clamped()
    {
        var clientSettings = new ClientSettings { InputGain = 5, OutputVolume = -1, VadThresholdDb = 0 }.Clamp();
        Assert.Equal((2f, 0f, -10f), (clientSettings.InputGain, clientSettings.OutputVolume, clientSettings.VadThresholdDb));
        Assert.Equal(2f, new ClientSettings { OutputVolume = 5 }.Clamp().OutputVolume); // Package 55: up to 200 %
    }

    /// <summary>Package 55: the volume goes up to 200 %, 100 % by default.</summary>
    [Fact]
    public void OutputVolume_UpTo200_RoundTripsClamped()
    {
        Assert.Equal(1f, ClientSettings.Load(dir, out _).OutputVolume);
        var settings = Vm(new ClientSettings());
        Assert.Equal(100, settings.OutputVolumePercent);
        settings.OutputVolumePercent = 200;
        Assert.Equal(200, settings.OutputVolumePercent);
        settings.ToSettings(new ClientSettings()).Save(dir);
        var loaded = ClientSettings.Load(dir, out _);
        Assert.Equal(2f, loaded.OutputVolume);
        Assert.Equal(200, Vm(loaded).OutputVolumePercent);
        settings.OutputVolumePercent = 250;
        Assert.Equal(200, settings.OutputVolumePercent);
    }

    static SettingsViewModel Vm(ClientSettings? clientSettings = null, params AudioDevice[] inputs) => new(clientSettings ?? new ClientSettings(), inputs, [], TimeProvider.System);

    /// <summary>Package 29: a new profile starts without any key.</summary>
    [Fact]
    public void Load_NoFile_DefaultPttOnly()
    {
        // Package 59 (replaces "no keys at all" from Package 29): a new profile can talk right away
        var fresh = ClientSettings.Load(dir, out _);
        Assert.Equal([new KeyBinding(KeyAction.PushToTalk, DefaultPushToTalk.Choose(DefaultPushToTalk.CountMouseButtons()))], fresh.KeyBindings);
        Assert.Equal(TransmitMode.PushToTalk, fresh.Mode);

        new ClientSettings().Save(dir); // an existing profile without keys keeps them that way
        Assert.Empty(ClientSettings.Load(dir, out _).KeyBindings);
        Assert.Empty(Vm().KeyBindings);
        Assert.False(Vm().HasKeyBindings);
    }

    /// <summary>A profile from before Package 29 keeps mouse buttons 4 and 5, now as bindings.</summary>
    [Fact]
    public void Load_OldFileWithPttKeys_KeepsThem()
    {
        File.WriteAllText(Path.Combine(dir, ClientSettings.FileName), """{"mode":"PushToTalk","pttKey":5,"linkPttKey":6}""");
        var clientSettings = ClientSettings.Load(dir, out _);
        Assert.Equal(new KeyChord(VirtualKeys.XButton1), clientSettings.ChordFor(KeyAction.PushToTalk));
        Assert.Equal(new KeyChord(VirtualKeys.XButton2), clientSettings.ChordFor(KeyAction.LinkPushToTalk));

        clientSettings.Save(dir);
        var json = File.ReadAllText(Path.Combine(dir, ClientSettings.FileName));
        Assert.DoesNotContain("pttKey", json); // the old fields are read once, never written again
        Assert.Equal(2, ClientSettings.Load(dir, out _).KeyBindings.Count);
    }

    // ---- Package 41: a free list of key bindings ----

    /// <summary>A settings page whose overlay answers with the next binding from the queue.</summary>
    static SettingsViewModel ListVm(Queue<KeyBinding?> answers, ClientSettings? clientSettings = null)
    {
        var settings = Vm(clientSettings);
        settings.EditKeyBinding = (_, _) => Task.FromResult(answers.Dequeue());
        return settings;
    }

    static KeyBinding Bind(KeyAction action, int key) => new(action, new KeyChord(key));

    [Fact]
    public async Task KeyList_StartsEmpty_AddEditRemove()
    {
        var answers = new Queue<KeyBinding?>([Bind(KeyAction.PushToMute, VirtualKeys.XButton1), null, Bind(KeyAction.ToggleMute, 0x70)]);
        var settings = ListVm(answers);
        await settings.AddKeyBindingCommand.ExecuteAsync(null);
        Assert.Equal("Push-to-Mute (stumm, solange gedrückt)", settings.KeyBindings.Single().Label);
        await settings.AddKeyBindingCommand.ExecuteAsync(null); // cancelled: nothing added
        Assert.Single(settings.KeyBindings);

        await settings.KeyBindings[0].EditCommand.ExecuteAsync(null);
        Assert.Equal(Bind(KeyAction.ToggleMute, 0x70), settings.KeyBindings[0].Binding);
        Assert.Equal("F1", settings.KeyBindings[0].ChordName);

        settings.KeyBindings[0].RemoveCommand.Execute(null);
        Assert.Empty(settings.ToSettings(new ClientSettings()).KeyBindings);
    }

    [Fact]
    public async Task KeyList_SameActionTwoKeys_Saves()
    {
        var settings = ListVm(new([Bind(KeyAction.PushToMute, VirtualKeys.XButton1), Bind(KeyAction.PushToMute, VirtualKeys.XButton2)]));
        await settings.AddKeyBindingCommand.ExecuteAsync(null);
        await settings.AddKeyBindingCommand.ExecuteAsync(null);
        Assert.True(settings.CanSave);
        Assert.Equal(2, settings.ToSettings(new ClientSettings()).KeyBindings.Count(keyBinding => keyBinding.Action == KeyAction.PushToMute));
    }

    [Fact]
    public async Task KeyList_SameKeyTwoActions_BlocksSave()
    {
        var settings = ListVm(new([Bind(KeyAction.PushToTalk, 0x70), Bind(KeyAction.ToggleDeafen, 0x70), Bind(KeyAction.PushToTalk, 0x70)]));
        await settings.AddKeyBindingCommand.ExecuteAsync(null);
        await settings.AddKeyBindingCommand.ExecuteAsync(null);
        Assert.False(settings.CanSave);
        Assert.Contains("liegen auf derselben Taste", settings.Error);
        bool? closed = null;
        settings.CloseRequested += ok => closed = ok;
        settings.SaveAndCloseCommand.Execute(null);
        Assert.Null(closed);

        settings.KeyBindings[1].RemoveCommand.Execute(null);
        Assert.True(settings.CanSave);
        await settings.AddKeyBindingCommand.ExecuteAsync(null); // the very same line twice
        Assert.Contains("doppelt", settings.Error);
    }

    [Fact]
    public void KeyList_ExistingProfile_ShownAsList()
    {
        var clientSettings = new ClientSettings { KeyBindings = [Bind(KeyAction.PushToTalk, VirtualKeys.XButton1), Bind(KeyAction.LinkPushToTalk, VirtualKeys.XButton2)] };
        var settings = Vm(clientSettings);
        Assert.Equal(["Maustaste 4", "Maustaste 5"], settings.KeyBindings.Select(keyBinding => keyBinding.ChordName));
        Assert.Equal(clientSettings.KeyBindings, settings.ToSettings(new ClientSettings()).KeyBindings);
    }

    [Fact]
    public void Device_Missing_FallbackWithHint()
    {
        var settings = Vm(new ClientSettings { InputDeviceId = "weg" }, new AudioDevice("da", "Headset"));
        Assert.NotNull(settings.DeviceHint);
        Assert.Null(settings.SelectedInput.Id);
        Assert.Equal(["Standardgerät", "Headset"], settings.Inputs.Select(audioDeviceOption => audioDeviceOption.Name));

        var ok = Vm(new ClientSettings { InputDeviceId = "da" }, new AudioDevice("da", "Headset"));
        Assert.Null(ok.DeviceHint);
        Assert.Equal("da", ok.SelectedInput.Id);
    }

    /// <summary>Package 52: the page opens before the device list is there; the hint only comes once it really is missing.</summary>
    [Fact]
    public void Devices_LoadedLater_HintOnlyIfMissingAndChoiceKept()
    {
        var settings = new SettingsViewModel(new ClientSettings { InputDeviceId = "weg", OutputDeviceId = "box" }, null, null, TimeProvider.System);
        Assert.Equal(["Standardgerät", "Geräte werden geladen ..."], settings.Inputs.Select(audioDeviceOption => audioDeviceOption.Name));
        Assert.Equal(("weg", "box"), (settings.SelectedInput.Id, settings.SelectedOutput.Id));
        Assert.Null(settings.DeviceHint);
        Assert.Equal(("weg", "box"), (settings.ToSettings(new ClientSettings()).InputDeviceId, settings.ToSettings(new ClientSettings()).OutputDeviceId)); // saving early keeps them

        settings.ShowDevices([new AudioDevice("da", "Headset")], [new AudioDevice("box", "Lautsprecher")]);
        Assert.Equal(["Standardgerät", "Headset"], settings.Inputs.Select(audioDeviceOption => audioDeviceOption.Name));
        Assert.Null(settings.SelectedInput.Id); // "weg" is really gone
        Assert.Equal("box", settings.SelectedOutput.Id);
        Assert.NotNull(settings.DeviceHint);

        var chosen = new SettingsViewModel(new ClientSettings { InputDeviceId = "weg" }, null, null, TimeProvider.System);
        chosen.SelectedInput = chosen.Inputs[0]; // the user picks "Standard" while loading
        chosen.ShowDevices([new AudioDevice("da", "Headset")], []);
        Assert.Null(chosen.SelectedInput.Id);
        Assert.Null(chosen.DeviceHint); // nothing went missing: the user chose

        var reload = Vm(new ClientSettings { InputDeviceId = "da" }, new AudioDevice("da", "Headset"));
        reload.ShowDevices([new AudioDevice("neu", "Webcam"), new AudioDevice("da", "Headset")], []);
        Assert.Equal("da", reload.SelectedInput.Id);
        Assert.Equal(3, reload.Inputs.Count);
    }

    /// <summary>Package 61: background opacity and blur survive a restart and stay within 0 to 100 %.</summary>
    [Fact]
    public void BackgroundAppearance_RoundTrip_Clamped()
    {
        var fresh = ClientSettings.Load(dir, out _);
        Assert.Equal((1f, false), (fresh.BackgroundOpacity, fresh.BlurBackground));
        var settings = Vm(fresh);
        Assert.Equal((100d, false), (settings.BackgroundOpacityPercent, settings.BlurBackground));
        settings.BackgroundOpacityPercent = 40;
        settings.BlurBackground = true;
        settings.ToSettings(fresh).Save(dir);
        var loaded = ClientSettings.Load(dir, out _);
        Assert.Equal((0.4f, true), (loaded.BackgroundOpacity, loaded.BlurBackground));
        Assert.Equal(1f, new ClientSettings { BackgroundOpacity = 3 }.Clamp().BackgroundOpacity);
        Assert.Equal(0f, new ClientSettings { BackgroundOpacity = -1 }.Clamp().BackgroundOpacity);
        settings.BackgroundOpacityPercent = 150;
        Assert.Equal(100, settings.BackgroundOpacityPercent);
    }

    /// <summary>Package 99 (A114): animated by default, also for a settings file from before; "Vereinfacht" survives a restart.</summary>
    [Fact]
    public void Display_NewOrOldFileOrSimplified_AnimatedByDefaultAndRoundTrips()
    {
        Assert.Equal(DisplayMode.Animated, new ClientSettings().Display);
        File.WriteAllText(Path.Combine(dir, ClientSettings.FileName), """{ "theme": "dark" }""");
        Assert.Equal(DisplayMode.Animated, ClientSettings.Load(dir, out _).Display);
        var settings = Vm(new ClientSettings());
        Assert.Equal(DisplayMode.Animated, settings.SelectedDisplay.Value);
        settings.SelectedDisplay = SettingsViewModel.Displays.Single(displayOption => displayOption.Value == DisplayMode.Simplified);
        settings.ToSettings(new ClientSettings()).Save(dir);
        Assert.Equal(DisplayMode.Simplified, ClientSettings.Load(dir, out _).Display);
        Assert.Equal(DisplayMode.Simplified, Vm(ClientSettings.Load(dir, out _)).SelectedDisplay.Value);
    }

    /// <summary>Package 123 (A131): noise suppression on, also for a file from before; Package 127 (A136): automatic gain on too. Both survive a restart.</summary>
    [Fact]
    public void VoiceFilters_NewOrOldFile_OnByDefaultAndRoundTrip()
    {
        Assert.Equal((true, true), (new ClientSettings().NoiseSuppression, new ClientSettings().AutoGain));
        File.WriteAllText(Path.Combine(dir, ClientSettings.FileName), """{ "theme": "dark" }""");
        var old = ClientSettings.Load(dir, out _);
        Assert.Equal((true, true), (old.NoiseSuppression, old.AutoGain));
        var settings = Vm(old);
        Assert.Equal((true, true), (settings.NoiseSuppression, settings.AutoGain));
        settings.NoiseSuppression = false;
        settings.AutoGain = false;
        settings.ToSettings(old).Save(dir);
        var loaded = ClientSettings.Load(dir, out _);
        Assert.Equal((false, false), (loaded.NoiseSuppression, loaded.AutoGain));
        Assert.Equal((false, false), (Vm(loaded).NoiseSuppression, Vm(loaded).AutoGain));
    }

    /// <summary>
    /// Package 127 (A135): a file without a version (from before) runs migration step 1 once and is saved with the
    /// new version; a new file starts at the newest version; a migrated file keeps the user's choice.
    /// </summary>
    [Fact]
    public void Migration_OldFile_AutoGainTurnedOnOnce()
    {
        var path = Path.Combine(dir, ClientSettings.FileName);
        var fresh = ClientSettings.Load(dir, out _);
        Assert.Equal((true, ClientSettings.CurrentVersion), (fresh.AutoGain, fresh.SettingsVersion));
        Assert.True(ClientSettings.CurrentVersion >= 1);

        File.WriteAllText(path, """{ "theme": "dark", "autoGain": false }""");
        var old = ClientSettings.Load(dir, out _);
        Assert.Equal((true, ClientSettings.CurrentVersion), (old.AutoGain, old.SettingsVersion));
        Assert.Contains("\"settingsVersion\"", File.ReadAllText(path)); // saved with the new version

        old.AutoGain = false; // the user turns it off again
        old.Save(dir);
        var migrated = ClientSettings.Load(dir, out _);
        Assert.Equal((false, ClientSettings.CurrentVersion), (migrated.AutoGain, migrated.SettingsVersion));

        File.WriteAllText(path, """{ "settingsVersion": 1, "autoGain": false }""");
        Assert.False(ClientSettings.Load(dir, out _).AutoGain);
    }

    /// <summary>Package 127 (A135): saving the settings page keeps the loaded version, so a newer client's file is not set back.</summary>
    [Fact]
    public void ToSettings_LoadedVersion_Kept()
    {
        var newer = new ClientSettings { SettingsVersion = ClientSettings.CurrentVersion + 5 };
        Assert.Equal(ClientSettings.CurrentVersion + 5, Vm(newer).ToSettings(newer).SettingsVersion);
    }

    /// <summary>Package 128 (A135 step 2): volumes above 100 % go back to 100 % once, since 200 % now means four times.</summary>
    [Fact]
    public void Migration_VolumesAbove100_ResetOnce()
    {
        var path = Path.Combine(dir, ClientSettings.FileName);
        File.WriteAllText(path, """{ "settingsVersion": 1, "outputVolume": 1.8, "userVolumes": { "fpA": 2, "fpB": 0.5, "fpC": 1.2 } }""");
        var old = ClientSettings.Load(dir, out _);
        Assert.Equal((1f, ClientSettings.CurrentVersion), (old.OutputVolume, old.SettingsVersion));
        Assert.Equal((1f, 0.5f, 1f), (old.VolumeFor("fpA"), old.VolumeFor("fpB"), old.VolumeFor("fpC")));
        Assert.Equal(["fpB"], old.UserVolumes.Keys); // entries back at 100 % are dropped, not stored

        File.WriteAllText(path, """{ "outputVolume": 0.7 }"""); // up to 100 % is kept, also from version 0
        Assert.Equal(0.7f, ClientSettings.Load(dir, out _).OutputVolume);

        old.OutputVolume = 1.8f; // the user turns it up again
        old.SetVolume("fpA", 2f);
        old.Save(dir);
        var migrated = ClientSettings.Load(dir, out _);
        Assert.Equal((1.8f, 2f), (migrated.OutputVolume, migrated.VolumeFor("fpA")));
    }

    /// <summary>Package 129 (A135 step 3): only a threshold still at the old default -40 becomes automatic; new files are automatic.</summary>
    [Fact]
    public void Migration_ThresholdAtOldDefault_OnlyThenAutomatic()
    {
        var path = Path.Combine(dir, ClientSettings.FileName);
        Assert.True(ClientSettings.Load(dir, out _).VadAutomatic);
        Assert.True(new ClientSettings().VadAutomatic);

        File.WriteAllText(path, """{ "settingsVersion": 2, "vadThresholdDb": -40 }""");
        var old = ClientSettings.Load(dir, out _);
        Assert.Equal((true, ClientSettings.CurrentVersion), (old.VadAutomatic, old.SettingsVersion));

        foreach (var file in new[] { """{ "settingsVersion": 2, "vadThresholdDb": -48 }""", """{ "vadThresholdDb": -30 }""" })
        {
            File.WriteAllText(path, file); // tuned by hand: stays manual, also from version 0
            var tuned = ClientSettings.Load(dir, out _);
            Assert.False(tuned.VadAutomatic);
            Assert.Equal(ClientSettings.CurrentVersion, tuned.SettingsVersion);
        }

        old.VadAutomatic = false; // the user turns it off again
        old.Save(dir);
        Assert.False(ClientSettings.Load(dir, out _).VadAutomatic);
    }

    /// <summary>
    /// Package 129 (A138): "Automatisch" is heard at once, the threshold slider is disabled while it is on, and the
    /// meter marks the threshold the gate uses (reported by the engine) instead of the slider's value.
    /// </summary>
    [AvaloniaFact]
    public void VadAuto_On_LiveAndSliderDisabled()
    {
        var settings = Vm(new ClientSettings { Mode = TransmitMode.VoiceActivation, VadThresholdDb = -40 });
        var live = new List<ClientSettings>();
        settings.LivePreview = live.Add;
        var window = new Window { Width = 900, Height = 900, Content = new OVS.Client.Views.SettingsView { DataContext = settings } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var slider = window.GetVisualDescendants().OfType<Slider>().Single(otherSlider => AutomationProperties.GetName(otherSlider) == Strings.Ui_ThresholdName);
        Assert.True(settings.VadAutomatic);
        Assert.False(slider.IsEnabled);

        settings.ThresholdInUseDb = -47; // what the engine reports
        settings.InputLevelDb = -45;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(-47, slider.Value);
        Assert.True(settings.IsAboveThreshold); // above the threshold in use, below the manual -40

        settings.VadAutomatic = false;
        Dispatcher.UIThread.RunJobs();
        Assert.False(live[^1].VadAutomatic);
        Assert.True(slider.IsEnabled);
        Assert.Equal(-40, slider.Value);
        Assert.False(settings.IsAboveThreshold);
        slider.Value = -50; // dragged
        Assert.Equal(-50, settings.VadThresholdDb);
        Assert.False(settings.ToSettings(new ClientSettings()).VadAutomatic);
        window.Close();

        using var keys = new KeyPoller(TimeProvider.System); // the engine takes the switch without restarting the microphone
        using var engine = new AudioEngine(keys, TimeProvider.System, hardware: null);
        engine.Configure(new ClientSettings());
        engine.DeviceLifecycle.SetTone(440);
        var capture = engine.DeviceLifecycle.CaptureSource.Capture!;
        Assert.True(capture.Vad.Automatic);
        engine.ApplyLive(new ClientSettings { VadAutomatic = false });
        Assert.Same(capture, engine.DeviceLifecycle.CaptureSource.Capture);
        Assert.False(capture.Vad.Automatic);
        Assert.Equal(capture.Vad.ThresholdInUseDb, engine.InputThresholdDb);
    }

    /// <summary>
    /// Package 130 (A139): the release delay defaults to 20 ms, is clamped to 0 to 2000 and saved; its slider shows in
    /// push-to-talk mode or with a link push-to-talk key and acts at once, on the key poller the engine, the self test and the "Sendet" text read.
    /// </summary>
    [AvaloniaFact]
    public void PttReleaseDelay_DefaultOrOutOfRange_ClampedAndLive()
    {
        Assert.Equal(20, new ClientSettings().PttReleaseDelayMs);
        Assert.Equal(20, ClientSettings.Load(dir, out _).PttReleaseDelayMs);
        new ClientSettings { PttReleaseDelayMs = 5000 }.Save(dir);
        Assert.Equal(2000, ClientSettings.Load(dir, out _).PttReleaseDelayMs);
        new ClientSettings { PttReleaseDelayMs = -5 }.Save(dir);
        Assert.Equal(0, ClientSettings.Load(dir, out _).PttReleaseDelayMs);
        new ClientSettings { PttReleaseDelayMs = 300 }.Save(dir);
        var loaded = ClientSettings.Load(dir, out _);
        Assert.Equal(300, loaded.PttReleaseDelayMs);

        var settings = Vm(loaded);
        var live = new List<ClientSettings>();
        settings.LivePreview = live.Add;
        var window = new Window { Width = 900, Height = 900, Content = new OVS.Client.Views.SettingsView { DataContext = settings } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var slider = window.GetVisualDescendants().OfType<Slider>().Single(otherSlider => AutomationProperties.GetName(otherSlider) == Strings.Ui_PttReleaseDelay);
        Assert.True(slider.IsEffectivelyVisible);
        Assert.Equal(300, slider.Value);
        slider.Value = 500; // dragged
        Assert.Equal(500, live[^1].PttReleaseDelayMs);
        Assert.Equal(500, settings.ToSettings(loaded).PttReleaseDelayMs);
        Assert.Equal(loaded.SettingsVersion, settings.ToSettings(loaded).SettingsVersion);
        settings.PttReleaseDelayMs = 2500;
        Assert.Equal(2000, settings.PttReleaseDelayMs);
        settings.VoiceActivation = true;
        Dispatcher.UIThread.RunJobs();
        Assert.False(slider.IsEffectivelyVisible); // nothing it would act on
        settings.KeyBindings.Add(new KeyBindingViewModel(new KeyBinding(KeyAction.LinkPushToTalk, new KeyChord(0x71)), settings));
        Dispatcher.UIThread.RunJobs();
        Assert.True(slider.IsEffectivelyVisible); // link push-to-talk works with voice activation too
        window.Close();

        using var keys = new KeyPoller(TimeProvider.System);
        using var engine = new AudioEngine(keys, TimeProvider.System, hardware: null);
        engine.Configure(new ClientSettings());
        Assert.Equal(TimeSpan.FromMilliseconds(20), keys.ReleaseDelay);
        engine.ApplyLive(new ClientSettings { PttReleaseDelayMs = 700 });
        Assert.Equal(TimeSpan.FromMilliseconds(700), keys.ReleaseDelay);
    }

    /// <summary>Package 128 (A137): the sliders keep 0 to 200 % and the file stores the slider value, not the gain.</summary>
    [Fact]
    public void Volume_Saved_SliderValueStored()
    {
        var settings = Vm(new ClientSettings());
        settings.OutputVolumePercent = 150;
        var clientSettings = settings.ToSettings(new ClientSettings());
        clientSettings.SetVolume("fpA", 1.5f);
        clientSettings.Save(dir);
        var loaded = ClientSettings.Load(dir, out _);
        Assert.Equal((1.5f, 1.5f), (loaded.OutputVolume, loaded.VolumeFor("fpA")));
        Assert.Equal(150, Vm(loaded).OutputVolumePercent, 3);
        settings.OutputVolumePercent = 250;
        Assert.Equal(200, settings.OutputVolumePercent);
    }

    /// <summary>
    /// Package 140 (A149, AC4): echo cancellation is on by default, also for a file without the key; it round-trips, is
    /// previewed live and reaches the running capture without a restart; a missing library turns it off (its one log line:
    /// <c>NativeCaptureFiltersTests.AttachTo_LibrariesMissing_NoFiltersAndOneLogLineEach</c>).
    /// </summary>
    [Fact]
    public async Task EchoCancellation_DefaultLiveOrLibMissing_Followed()
    {
        Assert.True(new ClientSettings().EchoCancellation);
        await File.WriteAllTextAsync(Path.Combine(dir, ClientSettings.FileName), """{ "theme": "dark" }""");
        var old = ClientSettings.Load(dir, out _);
        Assert.True(old.EchoCancellation);
        var settings = Vm(old);
        Assert.True(settings.EchoCancellation);
        var live = new List<ClientSettings>();
        settings.LivePreview = live.Add;
        settings.EchoCancellation = false;
        Assert.False(live[^1].EchoCancellation);
        Assert.True(settings.HasChanges);
        settings.ToSettings(old).Save(dir);
        Assert.False(ClientSettings.Load(dir, out _).EchoCancellation);

        using var keys = new KeyPoller(TimeProvider.System);
        using (var engine = new AudioEngine(keys, TimeProvider.System, hardware: null))
        {
            engine.Configure(new ClientSettings());
            engine.DeviceLifecycle.SetTone(440);
            var capture = engine.DeviceLifecycle.CaptureSource.Capture!;
            Assert.NotNull(capture.Canceller);
            Assert.True(capture.EchoCancellation);
            engine.ApplyLive(live[^1]); // switched off on the page: the same capture, the switch off
            Assert.Same(capture, engine.DeviceLifecycle.CaptureSource.Capture);
            Assert.False(capture.EchoCancellation);
        }

        using var missing = new AudioEngine(keys, TimeProvider.System, hardware: null, new NativeCaptureFilters(log: null, echoLibrary: NativeCaptureFiltersTests.NoEchoLibrary));
        missing.Configure(new ClientSettings());
        missing.DeviceLifecycle.SetTone(440);
        int levels = 0;
        missing.InputLevel += _ => Interlocked.Increment(ref levels);
        await AsyncWait.UntilAsync(() => Volatile.Read(ref levels) > 0);
        Assert.True(Volatile.Read(ref levels) > 0, "the capture stopped");
        Assert.Null(missing.DeviceLifecycle.CaptureSource.Capture!.Canceller);
    }

    /// <summary>
    /// Package 142 (A151, AC6): "Übertragungsart" is speech by default, also for a file without the key; music is saved,
    /// previewed live and reaches the running capture without a restart; while it is chosen the filter switches are
    /// disabled (they keep their values for speech).
    /// </summary>
    [AvaloniaFact]
    public void TransmissionMode_DefaultOrChanged_AppliedLive()
    {
        Assert.Equal(AudioContent.Voice, new ClientSettings().AudioContent);
        File.WriteAllText(Path.Combine(dir, ClientSettings.FileName), """{ "theme": "dark" }""");
        var old = ClientSettings.Load(dir, out _);
        Assert.Equal(AudioContent.Voice, old.AudioContent);

        var settings = Vm(old);
        var live = new List<ClientSettings>();
        settings.LivePreview = live.Add;
        var window = new Window { Width = 900, Height = 900, Content = new OVS.Client.Views.SettingsView { DataContext = settings } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var radios = window.GetVisualDescendants().OfType<RadioButton>().ToList();
        var voice = radios.Single(radioButton => radioButton.Content as string == Strings.Ui_TransmissionVoice);
        var music = radios.Single(radioButton => radioButton.Content as string == Strings.Ui_TransmissionMusic);
        var filters = window.GetVisualDescendants().OfType<CheckBox>()
            .Where(checkBox => checkBox.Content as string is { } text && (text == Strings.Ui_NoiseSuppression || text == Strings.Ui_AutoGain || text == Strings.Ui_EchoCancellation)).ToList();
        Assert.Equal(3, filters.Count);
        Assert.True(voice.IsChecked);
        Assert.All(filters, value => Assert.True(value.IsEffectivelyEnabled));

        music.IsChecked = true; // clicked
        Dispatcher.UIThread.RunJobs();
        Assert.True(settings.IsMusic);
        Assert.Equal(AudioContent.Music, live[^1].AudioContent);
        Assert.True(settings.HasChanges);
        Assert.All(filters, value => Assert.False(value.IsEffectivelyEnabled));
        Assert.All(filters, value => Assert.True(value.IsChecked)); // still on for speech
        window.Close();
        settings.ToSettings(old).Save(dir);
        Assert.Equal(AudioContent.Music, ClientSettings.Load(dir, out _).AudioContent);

        using var keys = new KeyPoller(TimeProvider.System);
        using var engine = new AudioEngine(keys, TimeProvider.System, hardware: null);
        engine.Configure(new ClientSettings());
        engine.DeviceLifecycle.SetTone(440);
        var capture = engine.DeviceLifecycle.CaptureSource.Capture!;
        Assert.False(capture.Music);
        engine.ApplyLive(live[^1]);
        Assert.Same(capture, engine.DeviceLifecycle.CaptureSource.Capture); // no restart
        Assert.True(capture.Music);
        engine.ApplyLive(new ClientSettings()); // "Abbrechen"
        Assert.False(capture.Music);
    }

    /// <summary>Package 123: the switches are heard at once and count as a change; switched back, nothing is left to save.</summary>
    [Fact]
    public void VoiceFilters_ToggledAndBack_LiveAndNoChangeLeft()
    {
        var settings = Vm(new ClientSettings());
        var live = new List<ClientSettings>();
        settings.LivePreview = live.Add;
        settings.NoiseSuppression = false;
        Assert.False(live[^1].NoiseSuppression);
        Assert.True(settings.HasChanges);
        settings.AutoGain = false;
        Assert.False(live[^1].AutoGain);
        Assert.False(live[^1].NoiseSuppression); // every preview carries all values
        settings.NoiseSuppression = true;
        settings.AutoGain = true;
        Assert.Equal((true, true), (live[^1].NoiseSuppression, live[^1].AutoGain));
        Assert.False(settings.HasChanges);
        // "Abbrechen" applies the saved settings to the engine again (MainViewModel.CloseSettings), see SendPathTests
    }

    /// <summary>Package 99: both choices have a name and a hint.</summary>
    [Fact]
    public void Display_Choices_HaveNamesAndHints()
    {
        Assert.Equal([DisplayMode.Animated, DisplayMode.Simplified], SettingsViewModel.Displays.Select(displayOption => displayOption.Value));
        Assert.All(SettingsViewModel.Displays, displayOption => Assert.False(string.IsNullOrWhiteSpace(displayOption.Name) || string.IsNullOrWhiteSpace(displayOption.Hint)));
    }

    /// <summary>Package 53: the sliders are heard at once, not only after "Speichern".</summary>
    [Fact]
    public void Sliders_Moved_AppliedLive()
    {
        var settings = Vm(new ClientSettings());
        var live = new List<ClientSettings>();
        settings.LivePreview = live.Add;
        settings.InputGainPercent = 50;
        Assert.Equal(0.5f, live[^1].InputGain);
        settings.OutputVolumePercent = 30;
        Assert.Equal(0.3f, live[^1].OutputVolume, 3);
        settings.VadThresholdDb = -25;
        Assert.Equal(-25f, live[^1].VadThresholdDb);
        settings.VoiceActivation = true;
        Assert.Equal(TransmitMode.VoiceActivation, live[^1].Mode);
        Assert.Equal(0.5f, live[^1].InputGain); // every preview carries all values
        settings.BackgroundOpacityPercent = 30; // Package 61: the background is seen at once as well
        Assert.Equal(0.3f, live[^1].BackgroundOpacity, 3);
        settings.BlurBackground = true;
        Assert.True(live[^1].BlurBackground);
        settings.SelectedDisplay = SettingsViewModel.Displays.Single(displayOption => displayOption.Value == DisplayMode.Simplified); // Package 99
        Assert.Equal(DisplayMode.Simplified, live[^1].Display);
    }

    /// <summary>Package 59: push-to-talk without a key cannot work; the settings say so and offer to set one.</summary>
    [Fact]
    public void PttHint_PushToTalkWithoutKey_OnlyThenShown()
    {
        var settings = Vm(new ClientSettings());
        var changes = new List<string?>();
        settings.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        Assert.True(settings.ShowPttHint);
        settings.VoiceActivation = true;
        Assert.False(settings.ShowPttHint);
        settings.VoiceActivation = false;
        Assert.True(settings.ShowPttHint);
        var ptt = new KeyBindingViewModel(new KeyBinding(KeyAction.PushToTalk, new KeyChord(VirtualKeys.XButton1)), settings);
        settings.KeyBindings.Add(ptt);
        Assert.False(settings.ShowPttHint);
        settings.KeyBindings.Remove(ptt);
        settings.KeyBindings.Add(new KeyBindingViewModel(new KeyBinding(KeyAction.ToggleMute, new KeyChord(0x4D)), settings));
        Assert.True(settings.ShowPttHint); // another key does not help
        Assert.True(changes.Count(text => text == nameof(SettingsViewModel.ShowPttHint)) >= 4);
    }

    /// <summary>The key dialog captures through the chord capture the page was given (the key poller's in the app).</summary>
    [Fact]
    public async Task AddKeyBinding_CaptureGiven_DialogGetsItsChord()
    {
        var chord = new KeyChord(0x70, ChordModifiers.Ctrl);
        var settings = new SettingsViewModel(new ClientSettings(), [], [], TimeProvider.System, () => Task.FromResult(chord));
        KeyChord? captured = null;
        settings.EditKeyBinding = async (_, capture) => { captured = await capture(KeyAction.ToggleMute); return null; };
        await settings.AddKeyBindingCommand.ExecuteAsync(null);
        Assert.Equal(chord, captured);
    }

    [Fact]
    public async Task AddKeyBinding_NoCapture_DialogGetsNoChord()
    {
        var settings = Vm();
        bool asked = false;
        KeyChord? captured = new(0x70);
        settings.EditKeyBinding = async (_, capture) => { asked = true; captured = await capture(KeyAction.ToggleMute); return null; };
        await settings.AddKeyBindingCommand.ExecuteAsync(null);
        Assert.True(asked);
        Assert.Null(captured);
    }

    [Fact]
    public async Task SetPttKey_Clicked_OpensDialogWithPushToTalk()
    {
        var settings = Vm(new ClientSettings());
        KeyBinding? shown = null;
        settings.EditKeyBinding = (current, _) =>
        {
            shown = current;
            return Task.FromResult<KeyBinding?>(new KeyBinding(KeyAction.PushToTalk, new KeyChord(VirtualKeys.RControl)));
        };
        await settings.SetPttKeyCommand.ExecuteAsync(null);
        Assert.Equal(KeyAction.PushToTalk, shown!.Action);
        Assert.Equal(0, shown.Chord.Key); // no key yet: the user presses one
        Assert.Equal([new KeyBinding(KeyAction.PushToTalk, new KeyChord(VirtualKeys.RControl))], settings.KeyBindings.Select(keyBinding => keyBinding.Binding));
        Assert.False(settings.ShowPttHint);
    }

    /// <summary>
    /// Package 121: a push-to-talk key that is also a key of the focused self test button (Space, Enter) talks and
    /// does not stop the test. Without such a binding the button still works with the keyboard.
    /// </summary>
    [AvaloniaFact]
    public void SelfTest_SpaceAsPttKey_DoesNotToggleButton()
    {
        static FocusedSettings Focused(ClientSettings clientSettings)
        {
            var settings = Vm(clientSettings);
            var window = new Window { Width = 900, Height = 900, Content = new OVS.Client.Views.SettingsView { DataContext = settings } };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var button = window.GetVisualDescendants().OfType<Button>().Single(otherButton => otherButton.Command == settings.ToggleSelfTestCommand);
            settings.IsSelfTesting = true;
            Assert.True(button.Focus());
            return new(window, settings);
        }
        static void Press(Window window, Key key, PhysicalKey physical)
        {
            window.KeyPress(key, RawInputModifiers.None, physical, null);
            window.KeyRelease(key, RawInputModifiers.None, physical, null);
            Dispatcher.UIThread.RunJobs();
        }

        var (bound, viewModel) = Focused(new ClientSettings
        {
            KeyBindings = [Bind(KeyAction.PushToTalk, 0x20), Bind(KeyAction.LinkPushToTalk, 0x0D)],
        });
        Press(bound, Key.Space, PhysicalKey.Space);
        Assert.True(viewModel.IsSelfTesting);
        Press(bound, Key.Enter, PhysicalKey.Enter); // checked on its own: two toggles would cancel out
        Assert.True(viewModel.IsSelfTesting);
        bound.Close();

        var (unbound, plain) = Focused(new ClientSettings());
        Press(unbound, Key.Space, PhysicalKey.Space);
        Assert.False(plain.IsSelfTesting);
        unbound.Close();
    }

    [Fact]
    public void LevelMeter_Threshold_Marked()
    {
        var settings = Vm(new ClientSettings { VadThresholdDb = -40, VadAutomatic = false }); // Package 129: the manual threshold
        settings.InputLevelDb = -30;
        Assert.True(settings.IsAboveThreshold);
        settings.VadThresholdDb = -20;
        Assert.False(settings.IsAboveThreshold);
    }

    [Fact]
    public void ToSettings_KeepsBookmarks_MapsMode()
    {
        var basis = new ClientSettings { Bookmarks = [new Bookmark("a", "h", 1, "n")] };
        var settings = Vm(basis);
        settings.VoiceActivation = true;
        var result = settings.ToSettings(basis);
        Assert.Same(basis.Bookmarks, result.Bookmarks);
        Assert.Equal(TransmitMode.VoiceActivation, result.Mode);
    }

    [Fact]
    public void Theme_DefaultSystem_SavedAndMapped()
    {
        Assert.Equal(AppTheme.System, ClientSettings.Load(dir, out _).Theme);
        new ClientSettings { Theme = AppTheme.Dark }.Save(dir);
        var loaded = ClientSettings.Load(dir, out _);
        Assert.Equal(AppTheme.Dark, loaded.Theme);

        var settings = Vm(loaded);
        Assert.Equal(AppTheme.Dark, settings.SelectedTheme.Value);
        settings.SelectedTheme = SettingsViewModel.Themes.Single(choice => choice.Value == AppTheme.Light);
        Assert.Equal(AppTheme.Light, settings.ToSettings(loaded).Theme);
    }

    [Fact]
    public void PushToTalkRadio_Toggled_OppositeOfVoiceActivation()
    {
        var settings = Vm(new ClientSettings());
        Assert.True(settings.IsPushToTalk);
        settings.IsPushToTalk = false;
        Assert.True(settings.VoiceActivation);
        settings.VoiceActivation = false;
        Assert.True(settings.IsPushToTalk);
    }

    [Fact]
    public void ErrorTexts_EveryCode_HasText()
    {
        foreach (var code in ReasonCodes.All()) Assert.True(ErrorTexts.Has(code), code);
        Assert.Equal("Dafür fehlt dir das Recht.", ErrorTexts.For(ReasonCodes.PermissionDenied, "x")); // server details stay out of the UI (A45)
    }

    /// <summary>Package 98 (AC5): "Speichern" is busy while the audio devices restart and closes afterwards; the update check spins.</summary>
    [Fact]
    public async Task Save_DevicesRestart_BusyUntilDone()
    {
        var time = new OVS.Tests.TestSupport.ManualTimeProvider();
        var settings = new SettingsViewModel(new ClientSettings(), [], [], time: time);
        var restart = new TaskCompletionSource();
        settings.ApplySaved = () => restart.Task;
        bool? closed = null;
        settings.CloseRequested += ok => closed = ok;
        var saving = settings.SaveAndCloseCommand.ExecuteAsync(null);
        time.Advance(PendingActionViewModel.ShowAfter);
        Assert.True(settings.Saving.IsBusy);
        Assert.Null(closed);
        restart.SetResult();
        await saving;
        Assert.False(settings.Saving.IsBusy);
        Assert.True(closed);

        var check = new TaskCompletionSource<string>();
        settings.CheckNow = () => check.Task;
        var checking = settings.CheckUpdatesCommand.ExecuteAsync(null);
        time.Advance(PendingActionViewModel.ShowAfter);
        Assert.True(settings.Checking.IsBusy);
        check.SetResult("Aktuell");
        await checking;
        Assert.False(settings.Checking.IsBusy);
        Assert.Equal("Aktuell", settings.UpdateStatus);
    }

    [Fact]
    public void Detach_EndPreviewSet_RunsItOnce()
    {
        int ended = 0;
        var page = new SettingsViewModel(new ClientSettings(), [], [], new ManualTimeProvider()) { EndPreview = () => ended++ };

        page.Detach();

        Assert.Equal(1, ended);
    }

    [Fact]
    public void Detach_NoEndPreview_DoesNotThrow()
    {
        var page = new SettingsViewModel(new ClientSettings(), [], [], new ManualTimeProvider());

        Assert.Null(Record.Exception(page.Detach));
    }

    /// <summary>Step 28b: the one rule of migration step 3 and the debug API's /settings (Package 129, A138).</summary>
    [Theory]
    [InlineData(ClientSettings.DefaultVadThresholdDb, true)]
    [InlineData(-55f, false)]
    [InlineData(float.NaN, false)]
    public void InferVadAutomatic_Threshold_AutomaticOnlyAtTheDefault(float thresholdDb, bool automatic)
    {
        var settings = new ClientSettings { VadThresholdDb = thresholdDb, VadAutomatic = !automatic };

        settings.InferVadAutomatic();

        Assert.Equal(automatic, settings.VadAutomatic);
    }

    sealed record FocusedSettings(Window Window, SettingsViewModel Vm);
}