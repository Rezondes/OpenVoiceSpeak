using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
    public void Missing_GivesDefaults()
    {
        var s = ClientSettings.Load(dir, out var warning);
        Assert.Null(warning);
        Assert.Equal(TransmitMode.PushToTalk, s.Mode);
        Assert.Equal(-40f, s.VadThresholdDb);
        Assert.Empty(s.Bookmarks);
    }

    /// <summary>Package 39: the password is stored DPAPI-protected, never as plain text.</summary>
    [Fact]
    public void Password_Protected_RoundTrip_NoPlainTextInFile()
    {
        var s = new ClientSettings();
        s.Bookmarks.Add(new Bookmark("Gilde", "voice.example.org", 7000, "ich", PasswordProtector.Protect("streng-geheim")));
        s.Save(dir);
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
        var vm = Vm(new ClientSettings());
        Assert.Equal((false, 80.0), (vm.AllSoundsOff, Math.Round(vm.SoundVolumePercent)));
        vm.AllSoundsOff = true;
        vm.SoundVolumePercent = 150; // clamped
        var saved = vm.ToSettings(new ClientSettings());
        saved.Save(dir);
        var loaded = ClientSettings.Load(dir, out _);
        Assert.Equal((false, 1f), (loaded.SoundsEnabled, loaded.SoundVolume));
    }

    /// <summary>Package 48: per sound volume, mute and own file survive a restart; "Zurücksetzen" lets the copy go.</summary>
    [Fact]
    public void Sounds_PerEvent_RoundTrip_ResetDeletesCopy()
    {
        var wav = Path.Combine(dir, "eigener.wav");
        using (var writer = new NAudio.Wave.WaveFileWriter(wav, NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(48000, 1)))
            writer.WriteSamples(Enumerable.Repeat(0.2f, 4800).ToArray(), 0, 4800);
        var vm = Vm(new ClientSettings());
        vm.ImportSound = (path, sound) => SoundImport.Prepare(path, dir, sound);
        Assert.Equal(Enum.GetValues<SoundEvent>().Length, vm.SoundRows.Count);
        Assert.Equal("Standardton", vm.SoundRows[0].SourceText);

        var joined = vm.SoundRows.Single(r => r.Event == SoundEvent.UserJoined);
        joined.Import(wav);
        joined.VolumePercent = 50;
        vm.SoundRows.Single(r => r.Event == SoundEvent.UserLeft).Muted = true;
        Assert.Equal("Eigene Datei", joined.SourceText);
        var saved = vm.ToSettings(new ClientSettings());
        Assert.Equal(2, saved.Sounds.Count); // only what differs from the default
        saved.Save(dir);
        var loaded = ClientSettings.Load(dir, out _);
        Assert.Equal((0.5f, false), (loaded.SoundFor(SoundEvent.UserJoined).Volume, loaded.SoundFor(SoundEvent.UserJoined).Muted));
        Assert.True(loaded.SoundFor(SoundEvent.UserLeft).Muted);
        var copy = Path.Combine(dir, SoundLibrary.Folder, loaded.SoundFor(SoundEvent.UserJoined).File!);
        Assert.True(File.Exists(copy));

        var again = Vm(loaded);
        again.SoundRows.Single(r => r.Event == SoundEvent.UserJoined).ResetCommand.Execute(null);
        var reset = again.ToSettings(loaded);
        Assert.Null(reset.SoundFor(SoundEvent.UserJoined).File);
        new SoundLibrary(dir, _ => { }).CleanUp(reset); // what saving the settings does
        Assert.False(File.Exists(copy));
    }

    /// <summary>Package 51: per person by fingerprint, only what differs from 100 %, bounded to 0 to 200 %.</summary>
    [Fact]
    public void UserVolumes_RoundTrip_OnlyNonDefault_Clamped()
    {
        var s = new ClientSettings();
        s.SetVolume("fpA", 1.5f);
        s.SetVolume("fpB", 1f);
        s.SetVolume("fpD", 0f);
        s.UserVolumes["fpC"] = 7f; // e.g. edited by hand
        s.Clamp().Save(dir);
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
        var vm = Vm(new ClientSettings());
        var rows = vm.SoundRows.ToDictionary(r => r.Event);
        Assert.Equal(("Nachricht in Allgemein", "Nachricht im Channel"), (rows[SoundEvent.ServerMessage].Label, rows[SoundEvent.ChannelMessage].Label));
        rows[SoundEvent.ServerMessage].Muted = true;
        rows[SoundEvent.ChannelMessage].VolumePercent = 40;
        var saved = vm.ToSettings(new ClientSettings());
        Assert.Equal(0f, SoundPlayer.GainFor(saved, SoundEvent.ServerMessage));
        Assert.Equal(saved.SoundVolume * 0.4f, SoundPlayer.GainFor(saved, SoundEvent.ChannelMessage), 3);
        Assert.Equal(saved.SoundVolume, SoundPlayer.GainFor(saved, SoundEvent.PrivateMessage), 3);
    }

    /// <summary>Package 60: the own link tone has its own row, independent of the one for others.</summary>
    [Fact]
    public void Sounds_LinkRows_Independent()
    {
        var vm = Vm(new ClientSettings());
        var rows = vm.SoundRows.ToDictionary(r => r.Event);
        Assert.Equal(("Sprache über Link", "Eigene Sprache über Link"), (rows[SoundEvent.LinkVoice].Label, rows[SoundEvent.OwnLinkVoice].Label));
        rows[SoundEvent.OwnLinkVoice].Muted = true;
        var saved = vm.ToSettings(new ClientSettings());
        Assert.Equal(0f, SoundPlayer.GainFor(saved, SoundEvent.OwnLinkVoice));
        Assert.Equal(saved.SoundVolume, SoundPlayer.GainFor(saved, SoundEvent.LinkVoice), 3);
    }

    /// <summary>Package 73: the two group tones have their own rows, independent of each other.</summary>
    [Fact]
    public void Sounds_GroupRows_Independent()
    {
        var vm = Vm(new ClientSettings());
        var rows = vm.SoundRows.ToDictionary(r => r.Event);
        Assert.Equal(("Eigene Gruppe geändert", "Gruppe vergeben oder entzogen"), (rows[SoundEvent.GroupChanged].Label, rows[SoundEvent.GroupChangedByMe].Label));
        rows[SoundEvent.GroupChanged].Muted = true;
        rows[SoundEvent.GroupChangedByMe].VolumePercent = 30;
        var saved = vm.ToSettings(new ClientSettings());
        Assert.Equal(0f, SoundPlayer.GainFor(saved, SoundEvent.GroupChanged));
        Assert.Equal(saved.SoundVolume * 0.3f, SoundPlayer.GainFor(saved, SoundEvent.GroupChangedByMe), 3);
    }

    [Fact]
    public void Language_RoundTrip_ShownInSettings()
    {
        new ClientSettings { Language = OVS.Client.Localization.AppLanguage.English }.Save(dir);
        var loaded = ClientSettings.Load(dir, out _);
        Assert.Equal(OVS.Client.Localization.AppLanguage.English, loaded.Language);
        var vm = Vm(loaded);
        Assert.Equal("English", vm.SelectedLanguage.Name);
        vm.SelectedLanguage = SettingsViewModel.Languages.First(); // "Wie Windows"
        Assert.Equal(OVS.Client.Localization.AppLanguage.System, vm.ToSettings(loaded).Language);
    }

    [Fact]
    public void FullSettings_RoundTrip_IncludingBookmarks()
    {
        var s = new ClientSettings
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
        s.Save(dir);
        var loaded = ClientSettings.Load(dir, out _);
        Assert.Equal(s.Bookmarks, loaded.Bookmarks);
        Assert.Equal((s.InputDeviceId, s.OutputDeviceId, s.InputGain, s.OutputVolume, s.Mode, s.VadThresholdDb),
            (loaded.InputDeviceId, loaded.OutputDeviceId, loaded.InputGain, loaded.OutputVolume, loaded.Mode, loaded.VadThresholdDb));
        Assert.Equal(s.KeyBindings, loaded.KeyBindings);
    }

    [Fact]
    public void Corrupt_DefaultsAndBackup()
    {
        var path = Path.Combine(dir, ClientSettings.FileName);
        File.WriteAllText(path, "{ kaputt");
        var s = ClientSettings.Load(dir, out var warning);
        Assert.NotNull(warning);
        Assert.Equal(TransmitMode.PushToTalk, s.Mode);
        Assert.Equal("{ kaputt", File.ReadAllText(path + ".bak"));
    }

    [Fact]
    public void Clamp_OutOfRange()
    {
        var s = new ClientSettings { InputGain = 5, OutputVolume = -1, VadThresholdDb = 0 }.Clamp();
        Assert.Equal((2f, 0f, -10f), (s.InputGain, s.OutputVolume, s.VadThresholdDb));
        Assert.Equal(2f, new ClientSettings { OutputVolume = 5 }.Clamp().OutputVolume); // Package 55: up to 200 %
    }

    /// <summary>Package 55: the volume goes up to 200 %, 100 % by default.</summary>
    [Fact]
    public void OutputVolume_UpTo200_RoundTrip_Clamped()
    {
        Assert.Equal(1f, ClientSettings.Load(dir, out _).OutputVolume);
        var vm = Vm(new ClientSettings());
        Assert.Equal(100, vm.OutputVolumePercent);
        vm.OutputVolumePercent = 200;
        Assert.Equal(200, vm.OutputVolumePercent);
        vm.ToSettings(new ClientSettings()).Save(dir);
        var loaded = ClientSettings.Load(dir, out _);
        Assert.Equal(2f, loaded.OutputVolume);
        Assert.Equal(200, Vm(loaded).OutputVolumePercent);
        vm.OutputVolumePercent = 250;
        Assert.Equal(200, vm.OutputVolumePercent);
    }

    static SettingsViewModel Vm(ClientSettings? s = null, params AudioDevice[] inputs) => new(s ?? new ClientSettings(), inputs, [], TimeProvider.System);

    /// <summary>Package 29: a new profile starts without any key.</summary>
    [Fact]
    public void Load_NoFile_DefaultPttOnly()
    {
        // Package 59 (replaces "no keys at all" from Package 29): a new profile can talk right away
        var fresh = ClientSettings.Load(dir, out _);
        Assert.Equal([new KeyBinding(KeyAction.PushToTalk, DefaultKeys.PushToTalk(KeyPoller.MouseButtonCount()))], fresh.KeyBindings);
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
        var s = ClientSettings.Load(dir, out _);
        Assert.Equal(new KeyChord(KeyPoller.VkXButton1), s.ChordFor(KeyAction.PushToTalk));
        Assert.Equal(new KeyChord(KeyPoller.VkXButton2), s.ChordFor(KeyAction.LinkPushToTalk));

        s.Save(dir);
        var json = File.ReadAllText(Path.Combine(dir, ClientSettings.FileName));
        Assert.DoesNotContain("pttKey", json); // the old fields are read once, never written again
        Assert.Equal(2, ClientSettings.Load(dir, out _).KeyBindings.Count);
    }

    // ---- Package 41: a free list of key bindings ----

    /// <summary>A settings page whose overlay answers with the next binding from the queue.</summary>
    static SettingsViewModel ListVm(Queue<KeyBinding?> answers, ClientSettings? s = null)
    {
        var vm = Vm(s);
        vm.EditKeyBinding = (_, _) => Task.FromResult(answers.Dequeue());
        return vm;
    }

    static KeyBinding Bind(KeyAction action, int key) => new(action, new KeyChord(key));

    [Fact]
    public async Task KeyList_StartsEmpty_AddEditRemove()
    {
        var answers = new Queue<KeyBinding?>([Bind(KeyAction.PushToMute, KeyPoller.VkXButton1), null, Bind(KeyAction.ToggleMute, 0x70)]);
        var vm = ListVm(answers);
        await vm.AddKeyBindingCommand.ExecuteAsync(null);
        Assert.Equal("Push-to-Mute (stumm, solange gedrückt)", vm.KeyBindings.Single().Label);
        await vm.AddKeyBindingCommand.ExecuteAsync(null); // cancelled: nothing added
        Assert.Single(vm.KeyBindings);

        await vm.KeyBindings[0].EditCommand.ExecuteAsync(null);
        Assert.Equal(Bind(KeyAction.ToggleMute, 0x70), vm.KeyBindings[0].Binding);
        Assert.Equal("F1", vm.KeyBindings[0].ChordName);

        vm.KeyBindings[0].RemoveCommand.Execute(null);
        Assert.Empty(vm.ToSettings(new ClientSettings()).KeyBindings);
    }

    [Fact]
    public async Task KeyList_SameActionTwoKeys_Saves()
    {
        var vm = ListVm(new([Bind(KeyAction.PushToMute, KeyPoller.VkXButton1), Bind(KeyAction.PushToMute, KeyPoller.VkXButton2)]));
        await vm.AddKeyBindingCommand.ExecuteAsync(null);
        await vm.AddKeyBindingCommand.ExecuteAsync(null);
        Assert.True(vm.CanSave);
        Assert.Equal(2, vm.ToSettings(new ClientSettings()).KeyBindings.Count(b => b.Action == KeyAction.PushToMute));
    }

    [Fact]
    public async Task KeyList_SameKeyTwoActions_BlocksSave()
    {
        var vm = ListVm(new([Bind(KeyAction.PushToTalk, 0x70), Bind(KeyAction.ToggleDeafen, 0x70), Bind(KeyAction.PushToTalk, 0x70)]));
        await vm.AddKeyBindingCommand.ExecuteAsync(null);
        await vm.AddKeyBindingCommand.ExecuteAsync(null);
        Assert.False(vm.CanSave);
        Assert.Contains("liegen auf derselben Taste", vm.Error);
        bool? closed = null;
        vm.CloseRequested += ok => closed = ok;
        vm.SaveCommand.Execute(null);
        Assert.Null(closed);

        vm.KeyBindings[1].RemoveCommand.Execute(null);
        Assert.True(vm.CanSave);
        await vm.AddKeyBindingCommand.ExecuteAsync(null); // the very same line twice
        Assert.Contains("doppelt", vm.Error);
    }

    [Fact]
    public void KeyList_ExistingProfile_ShownAsList()
    {
        var s = new ClientSettings { KeyBindings = [Bind(KeyAction.PushToTalk, KeyPoller.VkXButton1), Bind(KeyAction.LinkPushToTalk, KeyPoller.VkXButton2)] };
        var vm = Vm(s);
        Assert.Equal(["Maustaste 4", "Maustaste 5"], vm.KeyBindings.Select(b => b.ChordName));
        Assert.Equal(s.KeyBindings, vm.ToSettings(new ClientSettings()).KeyBindings);
    }

    [Fact]
    public void MissingDevice_FallbackWithHint()
    {
        var vm = Vm(new ClientSettings { InputDeviceId = "weg" }, new AudioDevice("da", "Headset"));
        Assert.NotNull(vm.DeviceHint);
        Assert.Null(vm.SelectedInput.Id);
        Assert.Equal(["Standardgerät", "Headset"], vm.Inputs.Select(i => i.Name));

        var ok = Vm(new ClientSettings { InputDeviceId = "da" }, new AudioDevice("da", "Headset"));
        Assert.Null(ok.DeviceHint);
        Assert.Equal("da", ok.SelectedInput.Id);
    }

    /// <summary>Package 52: the page opens before the device list is there; the hint only comes once it really is missing.</summary>
    [Fact]
    public void Devices_LoadedLater_HintOnlyIfReallyMissing_KeepsUserChoice()
    {
        var vm = new SettingsViewModel(new ClientSettings { InputDeviceId = "weg", OutputDeviceId = "box" }, null, null, TimeProvider.System);
        Assert.Equal(["Standardgerät", "Geräte werden geladen ..."], vm.Inputs.Select(i => i.Name));
        Assert.Equal(("weg", "box"), (vm.SelectedInput.Id, vm.SelectedOutput.Id));
        Assert.Null(vm.DeviceHint);
        Assert.Equal(("weg", "box"), (vm.ToSettings(new ClientSettings()).InputDeviceId, vm.ToSettings(new ClientSettings()).OutputDeviceId)); // saving early keeps them

        vm.ShowDevices([new AudioDevice("da", "Headset")], [new AudioDevice("box", "Lautsprecher")]);
        Assert.Equal(["Standardgerät", "Headset"], vm.Inputs.Select(i => i.Name));
        Assert.Null(vm.SelectedInput.Id); // "weg" is really gone
        Assert.Equal("box", vm.SelectedOutput.Id);
        Assert.NotNull(vm.DeviceHint);

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
        var vm = Vm(fresh);
        Assert.Equal((100d, false), (vm.BackgroundOpacityPercent, vm.BlurBackground));
        vm.BackgroundOpacityPercent = 40;
        vm.BlurBackground = true;
        vm.ToSettings(fresh).Save(dir);
        var loaded = ClientSettings.Load(dir, out _);
        Assert.Equal((0.4f, true), (loaded.BackgroundOpacity, loaded.BlurBackground));
        Assert.Equal(1f, new ClientSettings { BackgroundOpacity = 3 }.Clamp().BackgroundOpacity);
        Assert.Equal(0f, new ClientSettings { BackgroundOpacity = -1 }.Clamp().BackgroundOpacity);
        vm.BackgroundOpacityPercent = 150;
        Assert.Equal(100, vm.BackgroundOpacityPercent);
    }

    /// <summary>Package 99 (A114): animated by default, also for a settings file from before; "Vereinfacht" survives a restart.</summary>
    [Fact]
    public void Display_DefaultsToAnimated_MissingKeyIsAnimated_SimplifiedRoundTrips()
    {
        Assert.Equal(DisplayMode.Animated, new ClientSettings().Display);
        File.WriteAllText(Path.Combine(dir, ClientSettings.FileName), """{ "theme": "dark" }""");
        Assert.Equal(DisplayMode.Animated, ClientSettings.Load(dir, out _).Display);
        var vm = Vm(new ClientSettings());
        Assert.Equal(DisplayMode.Animated, vm.SelectedDisplay.Value);
        vm.SelectedDisplay = SettingsViewModel.Displays.Single(d => d.Value == DisplayMode.Simplified);
        vm.ToSettings(new ClientSettings()).Save(dir);
        Assert.Equal(DisplayMode.Simplified, ClientSettings.Load(dir, out _).Display);
        Assert.Equal(DisplayMode.Simplified, Vm(ClientSettings.Load(dir, out _)).SelectedDisplay.Value);
    }

    /// <summary>Package 123 (A131): noise suppression on, also for a file from before; Package 127 (A136): automatic gain on too. Both survive a restart.</summary>
    [Fact]
    public void VoiceFilters_DefaultsAndRoundTrip()
    {
        Assert.Equal((true, true), (new ClientSettings().NoiseSuppression, new ClientSettings().AutoGain));
        File.WriteAllText(Path.Combine(dir, ClientSettings.FileName), """{ "theme": "dark" }""");
        var old = ClientSettings.Load(dir, out _);
        Assert.Equal((true, true), (old.NoiseSuppression, old.AutoGain));
        var vm = Vm(old);
        Assert.Equal((true, true), (vm.NoiseSuppression, vm.AutoGain));
        vm.NoiseSuppression = false;
        vm.AutoGain = false;
        vm.ToSettings(old).Save(dir);
        var loaded = ClientSettings.Load(dir, out _);
        Assert.Equal((false, false), (loaded.NoiseSuppression, loaded.AutoGain));
        Assert.Equal((false, false), (Vm(loaded).NoiseSuppression, Vm(loaded).AutoGain));
    }

    /// <summary>
    /// Package 127 (A135): a file without a version (from before) runs migration step 1 once and is saved with the
    /// new version; a new file starts at the newest version; a migrated file keeps the user's choice.
    /// </summary>
    [Fact]
    public void Migration_TurnsAutoGainOnOnce()
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
    public void ToSettings_KeepsSettingsVersion()
    {
        var newer = new ClientSettings { SettingsVersion = ClientSettings.CurrentVersion + 5 };
        Assert.Equal(ClientSettings.CurrentVersion + 5, Vm(newer).ToSettings(newer).SettingsVersion);
    }

    /// <summary>Package 128 (A135 step 2): volumes above 100 % go back to 100 % once, since 200 % now means four times.</summary>
    [Fact]
    public void Migration_ResetsVolumesAbove100Once()
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
    public void Migration_VadAutoOnlyForDefault()
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
    public void VadAuto_LiveAndSliderDisabled()
    {
        var vm = Vm(new ClientSettings { Mode = TransmitMode.VoiceActivation, VadThresholdDb = -40 });
        var live = new List<ClientSettings>();
        vm.LivePreview = live.Add;
        var window = new Window { Width = 900, Height = 900, Content = new OVS.Client.Views.SettingsView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var slider = window.GetVisualDescendants().OfType<Slider>().Single(s => AutomationProperties.GetName(s) == Strings.Ui_ThresholdName);
        Assert.True(vm.VadAutomatic);
        Assert.False(slider.IsEnabled);

        vm.ThresholdInUseDb = -47; // what the engine reports
        vm.InputLevelDb = -45;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(-47, slider.Value);
        Assert.True(vm.IsAboveThreshold); // above the threshold in use, below the manual -40

        vm.VadAutomatic = false;
        Dispatcher.UIThread.RunJobs();
        Assert.False(live[^1].VadAutomatic);
        Assert.True(slider.IsEnabled);
        Assert.Equal(-40, slider.Value);
        Assert.False(vm.IsAboveThreshold);
        slider.Value = -50; // dragged
        Assert.Equal(-50, vm.VadThresholdDb);
        Assert.False(vm.ToSettings(new ClientSettings()).VadAutomatic);
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
    public void PttReleaseDelay_DefaultClampLive()
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

        var vm = Vm(loaded);
        var live = new List<ClientSettings>();
        vm.LivePreview = live.Add;
        var window = new Window { Width = 900, Height = 900, Content = new OVS.Client.Views.SettingsView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var slider = window.GetVisualDescendants().OfType<Slider>().Single(s => AutomationProperties.GetName(s) == Strings.Ui_PttReleaseDelay);
        Assert.True(slider.IsEffectivelyVisible);
        Assert.Equal(300, slider.Value);
        slider.Value = 500; // dragged
        Assert.Equal(500, live[^1].PttReleaseDelayMs);
        Assert.Equal(500, vm.ToSettings(loaded).PttReleaseDelayMs);
        Assert.Equal(loaded.SettingsVersion, vm.ToSettings(loaded).SettingsVersion);
        vm.PttReleaseDelayMs = 2500;
        Assert.Equal(2000, vm.PttReleaseDelayMs);
        vm.VoiceActivation = true;
        Dispatcher.UIThread.RunJobs();
        Assert.False(slider.IsEffectivelyVisible); // nothing it would act on
        vm.KeyBindings.Add(new KeyBindingViewModel(new KeyBinding(KeyAction.LinkPushToTalk, new KeyChord(0x71)), vm));
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
    public void Volume_SliderValueStored()
    {
        var vm = Vm(new ClientSettings());
        vm.OutputVolumePercent = 150;
        var s = vm.ToSettings(new ClientSettings());
        s.SetVolume("fpA", 1.5f);
        s.Save(dir);
        var loaded = ClientSettings.Load(dir, out _);
        Assert.Equal((1.5f, 1.5f), (loaded.OutputVolume, loaded.VolumeFor("fpA")));
        Assert.Equal(150, Vm(loaded).OutputVolumePercent, 3);
        vm.OutputVolumePercent = 250;
        Assert.Equal(200, vm.OutputVolumePercent);
    }

    /// <summary>
    /// Package 140 (A149, AC4): echo cancellation is on by default, also for a file without the key; it round-trips, is
    /// previewed live and reaches the running capture without a restart; a missing library turns it off (its one log line:
    /// <c>NativeCaptureFiltersTests.AttachTo_LibrariesMissing_NoFiltersAndOneLogLineEach</c>).
    /// </summary>
    [Fact]
    public async Task EchoCancellation_DefaultLiveMissingLib()
    {
        Assert.True(new ClientSettings().EchoCancellation);
        File.WriteAllText(Path.Combine(dir, ClientSettings.FileName), """{ "theme": "dark" }""");
        var old = ClientSettings.Load(dir, out _);
        Assert.True(old.EchoCancellation);
        var vm = Vm(old);
        Assert.True(vm.EchoCancellation);
        var live = new List<ClientSettings>();
        vm.LivePreview = live.Add;
        vm.EchoCancellation = false;
        Assert.False(live[^1].EchoCancellation);
        Assert.True(vm.HasChanges);
        vm.ToSettings(old).Save(dir);
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
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (Volatile.Read(ref levels) == 0 && watch.ElapsedMilliseconds < 5000) await Task.Delay(10);
        Assert.True(Volatile.Read(ref levels) > 0, "the capture stopped");
        Assert.Null(missing.DeviceLifecycle.CaptureSource.Capture!.Canceller);
    }

    /// <summary>
    /// Package 142 (A151, AC6): "Übertragungsart" is speech by default, also for a file without the key; music is saved,
    /// previewed live and reaches the running capture without a restart; while it is chosen the filter switches are
    /// disabled (they keep their values for speech).
    /// </summary>
    [AvaloniaFact]
    public void TransmissionMode_DefaultLive()
    {
        Assert.Equal(AudioContent.Voice, new ClientSettings().AudioContent);
        File.WriteAllText(Path.Combine(dir, ClientSettings.FileName), """{ "theme": "dark" }""");
        var old = ClientSettings.Load(dir, out _);
        Assert.Equal(AudioContent.Voice, old.AudioContent);

        var vm = Vm(old);
        var live = new List<ClientSettings>();
        vm.LivePreview = live.Add;
        var window = new Window { Width = 900, Height = 900, Content = new OVS.Client.Views.SettingsView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var radios = window.GetVisualDescendants().OfType<RadioButton>().ToList();
        var voice = radios.Single(r => r.Content as string == Strings.Ui_TransmissionVoice);
        var music = radios.Single(r => r.Content as string == Strings.Ui_TransmissionMusic);
        var filters = window.GetVisualDescendants().OfType<CheckBox>()
            .Where(c => c.Content as string is { } text && (text == Strings.Ui_NoiseSuppression || text == Strings.Ui_AutoGain || text == Strings.Ui_EchoCancellation)).ToList();
        Assert.Equal(3, filters.Count);
        Assert.True(voice.IsChecked);
        Assert.All(filters, f => Assert.True(f.IsEffectivelyEnabled));

        music.IsChecked = true; // clicked
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.IsMusic);
        Assert.Equal(AudioContent.Music, live[^1].AudioContent);
        Assert.True(vm.HasChanges);
        Assert.All(filters, f => Assert.False(f.IsEffectivelyEnabled));
        Assert.All(filters, f => Assert.True(f.IsChecked)); // still on for speech
        window.Close();
        vm.ToSettings(old).Save(dir);
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
    public void VoiceFilters_LivePreviewAndCancel()
    {
        var vm = Vm(new ClientSettings());
        var live = new List<ClientSettings>();
        vm.LivePreview = live.Add;
        vm.NoiseSuppression = false;
        Assert.False(live[^1].NoiseSuppression);
        Assert.True(vm.HasChanges);
        vm.AutoGain = false;
        Assert.False(live[^1].AutoGain);
        Assert.False(live[^1].NoiseSuppression); // every preview carries all values
        vm.NoiseSuppression = true;
        vm.AutoGain = true;
        Assert.Equal((true, true), (live[^1].NoiseSuppression, live[^1].AutoGain));
        Assert.False(vm.HasChanges);
        // "Abbrechen" applies the saved settings to the engine again (MainViewModel.CloseSettings), see SendPathTests
    }

    /// <summary>Package 99: both choices have a name and a hint.</summary>
    [Fact]
    public void Display_ChoicesHaveNamesAndHints()
    {
        Assert.Equal([DisplayMode.Animated, DisplayMode.Simplified], SettingsViewModel.Displays.Select(d => d.Value));
        Assert.All(SettingsViewModel.Displays, d => Assert.False(string.IsNullOrWhiteSpace(d.Name) || string.IsNullOrWhiteSpace(d.Hint)));
    }

    /// <summary>Package 53: the sliders are heard at once, not only after "Speichern".</summary>
    [Fact]
    public void Sliders_ApplyLive()
    {
        var vm = Vm(new ClientSettings());
        var live = new List<ClientSettings>();
        vm.LivePreview = live.Add;
        vm.InputGainPercent = 50;
        Assert.Equal(0.5f, live[^1].InputGain);
        vm.OutputVolumePercent = 30;
        Assert.Equal(0.3f, live[^1].OutputVolume, 3);
        vm.VadThresholdDb = -25;
        Assert.Equal(-25f, live[^1].VadThresholdDb);
        vm.VoiceActivation = true;
        Assert.Equal(TransmitMode.VoiceActivation, live[^1].Mode);
        Assert.Equal(0.5f, live[^1].InputGain); // every preview carries all values
        vm.BackgroundOpacityPercent = 30; // Package 61: the background is seen at once as well
        Assert.Equal(0.3f, live[^1].BackgroundOpacity, 3);
        vm.BlurBackground = true;
        Assert.True(live[^1].BlurBackground);
        vm.SelectedDisplay = SettingsViewModel.Displays.Single(d => d.Value == DisplayMode.Simplified); // Package 99
        Assert.Equal(DisplayMode.Simplified, live[^1].Display);
    }

    /// <summary>Package 59: push-to-talk without a key cannot work; the settings say so and offer to set one.</summary>
    [Fact]
    public void PttHint_OnlyForPushToTalkWithoutKey()
    {
        var vm = Vm(new ClientSettings());
        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        Assert.True(vm.ShowPttHint);
        vm.VoiceActivation = true;
        Assert.False(vm.ShowPttHint);
        vm.VoiceActivation = false;
        Assert.True(vm.ShowPttHint);
        var ptt = new KeyBindingViewModel(new KeyBinding(KeyAction.PushToTalk, new KeyChord(KeyPoller.VkXButton1)), vm);
        vm.KeyBindings.Add(ptt);
        Assert.False(vm.ShowPttHint);
        vm.KeyBindings.Remove(ptt);
        vm.KeyBindings.Add(new KeyBindingViewModel(new KeyBinding(KeyAction.ToggleMute, new KeyChord(0x4D)), vm));
        Assert.True(vm.ShowPttHint); // another key does not help
        Assert.True(changes.Count(c => c == nameof(SettingsViewModel.ShowPttHint)) >= 4);
    }

    [Fact]
    public async Task SetPttKey_OpensDialogWithPushToTalk()
    {
        var vm = Vm(new ClientSettings());
        KeyBinding? shown = null;
        vm.EditKeyBinding = (current, _) =>
        {
            shown = current;
            return Task.FromResult<KeyBinding?>(new KeyBinding(KeyAction.PushToTalk, new KeyChord(KeyPoller.VkRControl)));
        };
        await vm.SetPttKeyCommand.ExecuteAsync(null);
        Assert.Equal(KeyAction.PushToTalk, shown!.Action);
        Assert.Equal(0, shown.Chord.Key); // no key yet: the user presses one
        Assert.Equal([new KeyBinding(KeyAction.PushToTalk, new KeyChord(KeyPoller.VkRControl))], vm.KeyBindings.Select(b => b.Binding));
        Assert.False(vm.ShowPttHint);
    }

    /// <summary>
    /// Package 121: a push-to-talk key that is also a key of the focused self test button (Space, Enter) talks and
    /// does not stop the test. Without such a binding the button still works with the keyboard.
    /// </summary>
    [AvaloniaFact]
    public void SelfTest_SpaceAsPttKey_DoesNotToggleButton()
    {
        static (Window Window, SettingsViewModel Vm) Focused(ClientSettings s)
        {
            var vm = Vm(s);
            var window = new Window { Width = 900, Height = 900, Content = new OVS.Client.Views.SettingsView { DataContext = vm } };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var button = window.GetVisualDescendants().OfType<Button>().Single(b => b.Command == vm.ToggleSelfTestCommand);
            vm.IsSelfTesting = true;
            Assert.True(button.Focus());
            return (window, vm);
        }
        static void Press(Window window, Key key, PhysicalKey physical)
        {
            window.KeyPress(key, RawInputModifiers.None, physical, null);
            window.KeyRelease(key, RawInputModifiers.None, physical, null);
            Dispatcher.UIThread.RunJobs();
        }

        var (bound, vm) = Focused(new ClientSettings
        {
            KeyBindings = [Bind(KeyAction.PushToTalk, 0x20), Bind(KeyAction.LinkPushToTalk, 0x0D)],
        });
        Press(bound, Key.Space, PhysicalKey.Space);
        Assert.True(vm.IsSelfTesting);
        Press(bound, Key.Enter, PhysicalKey.Enter); // checked on its own: two toggles would cancel out
        Assert.True(vm.IsSelfTesting);
        bound.Close();

        var (unbound, plain) = Focused(new ClientSettings());
        Press(unbound, Key.Space, PhysicalKey.Space);
        Assert.False(plain.IsSelfTesting);
        unbound.Close();
    }

    [Fact]
    public void LevelMeter_MarksThreshold()
    {
        var vm = Vm(new ClientSettings { VadThresholdDb = -40, VadAutomatic = false }); // Package 129: the manual threshold
        vm.InputLevelDb = -30;
        Assert.True(vm.IsAboveThreshold);
        vm.VadThresholdDb = -20;
        Assert.False(vm.IsAboveThreshold);
    }

    [Fact]
    public void ToSettings_KeepsBookmarks_MapsMode()
    {
        var basis = new ClientSettings { Bookmarks = [new Bookmark("a", "h", 1, "n")] };
        var vm = Vm(basis);
        vm.VoiceActivation = true;
        var result = vm.ToSettings(basis);
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

        var vm = Vm(loaded);
        Assert.Equal(AppTheme.Dark, vm.SelectedTheme.Value);
        vm.SelectedTheme = SettingsViewModel.Themes.Single(t => t.Value == AppTheme.Light);
        Assert.Equal(AppTheme.Light, vm.ToSettings(loaded).Theme);
    }

    [Fact]
    public void PushToTalkRadio_IsTheOppositeOfVoiceActivation()
    {
        var vm = Vm(new ClientSettings());
        Assert.True(vm.IsPushToTalk);
        vm.IsPushToTalk = false;
        Assert.True(vm.VoiceActivation);
        vm.VoiceActivation = false;
        Assert.True(vm.IsPushToTalk);
    }

    [Fact]
    public void ErrorTexts_EveryCodeHasText()
    {
        foreach (var code in ReasonCodes.All()) Assert.True(ErrorTexts.Has(code), code);
        Assert.Equal("Dafür fehlt dir das Recht.", ErrorTexts.For(ReasonCodes.PermissionDenied, "x")); // server details stay out of the UI (A45)
    }

    /// <summary>Package 98 (AC5): "Speichern" is busy while the audio devices restart and closes afterwards; the update check spins.</summary>
    [Fact]
    public async Task Save_BusyWhileDevicesRestart()
    {
        var time = new OVS.Tests.TestSupport.ManualTimeProvider();
        var vm = new SettingsViewModel(new ClientSettings(), [], [], time: time);
        var restart = new TaskCompletionSource();
        vm.ApplySaved = () => restart.Task;
        bool? closed = null;
        vm.CloseRequested += ok => closed = ok;
        var saving = vm.SaveCommand.ExecuteAsync(null);
        time.Advance(TimeSpan.FromMilliseconds(150));
        Assert.True(vm.Saving.IsBusy);
        Assert.Null(closed);
        restart.SetResult();
        await saving;
        Assert.False(vm.Saving.IsBusy);
        Assert.True(closed);

        var check = new TaskCompletionSource<string>();
        vm.CheckNow = () => check.Task;
        var checking = vm.CheckUpdatesCommand.ExecuteAsync(null);
        time.Advance(TimeSpan.FromMilliseconds(150));
        Assert.True(vm.Checking.IsBusy);
        check.SetResult("Aktuell");
        await checking;
        Assert.False(vm.Checking.IsBusy);
        Assert.Equal("Aktuell", vm.UpdateStatus);
    }
}
