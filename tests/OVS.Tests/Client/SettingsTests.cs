using OVS.Client;
using OVS.Client.Audio;
using OVS.Client.Input;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Shared.Protocol;

namespace OVS.Tests.Client;

public sealed class SettingsTests : IDisposable
{
    readonly string dir = Directory.CreateTempSubdirectory("ovs-settings-").FullName;

    public void Dispose() => Directory.Delete(dir, true);

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
        Assert.Equal(0f, AudioEngine.GainFor(saved, SoundEvent.ServerMessage));
        Assert.Equal(saved.SoundVolume * 0.4f, AudioEngine.GainFor(saved, SoundEvent.ChannelMessage), 3);
        Assert.Equal(saved.SoundVolume, AudioEngine.GainFor(saved, SoundEvent.PrivateMessage), 3);
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

    static SettingsViewModel Vm(ClientSettings? s = null, params AudioDevice[] inputs) => new(s ?? new ClientSettings(), inputs, []);

    /// <summary>Package 29: a new profile starts without any key.</summary>
    [Fact]
    public void Load_NoFile_NoBindings()
    {
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
        var vm = new SettingsViewModel(new ClientSettings { InputDeviceId = "weg", OutputDeviceId = "box" }, null, null);
        Assert.Equal(["Standardgerät", "Geräte werden geladen ..."], vm.Inputs.Select(i => i.Name));
        Assert.Equal(("weg", "box"), (vm.SelectedInput.Id, vm.SelectedOutput.Id));
        Assert.Null(vm.DeviceHint);
        Assert.Equal(("weg", "box"), (vm.ToSettings(new ClientSettings()).InputDeviceId, vm.ToSettings(new ClientSettings()).OutputDeviceId)); // saving early keeps them

        vm.ShowDevices([new AudioDevice("da", "Headset")], [new AudioDevice("box", "Lautsprecher")]);
        Assert.Equal(["Standardgerät", "Headset"], vm.Inputs.Select(i => i.Name));
        Assert.Null(vm.SelectedInput.Id); // "weg" is really gone
        Assert.Equal("box", vm.SelectedOutput.Id);
        Assert.NotNull(vm.DeviceHint);

        var chosen = new SettingsViewModel(new ClientSettings { InputDeviceId = "weg" }, null, null);
        chosen.SelectedInput = chosen.Inputs[0]; // the user picks "Standard" while loading
        chosen.ShowDevices([new AudioDevice("da", "Headset")], []);
        Assert.Null(chosen.SelectedInput.Id);
        Assert.Null(chosen.DeviceHint); // nothing went missing: the user chose

        var reload = Vm(new ClientSettings { InputDeviceId = "da" }, new AudioDevice("da", "Headset"));
        reload.ShowDevices([new AudioDevice("neu", "Webcam"), new AudioDevice("da", "Headset")], []);
        Assert.Equal("da", reload.SelectedInput.Id);
        Assert.Equal(3, reload.Inputs.Count);
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
    }

    [Fact]
    public void LevelMeter_MarksThreshold()
    {
        var vm = Vm(new ClientSettings { VadThresholdDb = -40 });
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
        foreach (var code in Codes.All()) Assert.True(ErrorTexts.Has(code), code);
        Assert.Equal("Dafür fehlt dir das Recht.", ErrorTexts.For(Codes.PermissionDenied, "x")); // server details stay out of the UI (A45)
    }
}
