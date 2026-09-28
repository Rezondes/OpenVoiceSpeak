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
        Assert.Equal("Dafür fehlt dir das Recht. (x)", ErrorTexts.For(Codes.PermissionDenied, "x"));
    }
}
