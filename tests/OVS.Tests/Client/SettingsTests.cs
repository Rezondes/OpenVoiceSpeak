using OVS.Client;
using OVS.Client.Audio;
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
            PttKey = 0x70,
            LinkPttKey = 0x71,
            VadThresholdDb = -30,
        };
        s.Save(dir);
        var loaded = ClientSettings.Load(dir, out _);
        Assert.Equal(s.Bookmarks, loaded.Bookmarks);
        Assert.Equal((s.InputDeviceId, s.OutputDeviceId, s.InputGain, s.OutputVolume, s.Mode, s.PttKey, s.LinkPttKey, s.VadThresholdDb),
            (loaded.InputDeviceId, loaded.OutputDeviceId, loaded.InputGain, loaded.OutputVolume, loaded.Mode, loaded.PttKey, loaded.LinkPttKey, loaded.VadThresholdDb));
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

    [Fact]
    public void SamePttKeys_ErrorAndSaveDisabled()
    {
        var vm = Vm();
        Assert.True(vm.CanSave);
        vm.LinkPttKey = vm.PttKey;
        Assert.False(vm.CanSave);
        Assert.NotNull(vm.Error);
        bool? closed = null;
        vm.CloseRequested += ok => closed = ok;
        vm.SaveCommand.Execute(null);
        Assert.Null(closed);
    }

    [Theory]
    [InlineData(500, 200)]
    [InlineData(-5, 0)]
    [InlineData(150, 150)]
    public void OutOfRange_Clamped(double input, double expected)
    {
        var vm = Vm();
        vm.InputGainPercent = input;
        Assert.Equal(expected, vm.InputGainPercent);
        Assert.Equal((float)(expected / 100), vm.ToSettings(new ClientSettings()).InputGain);
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
    public void ErrorTexts_EveryCodeHasText()
    {
        foreach (var code in Codes.All()) Assert.True(ErrorTexts.Has(code), code);
        Assert.Equal("Dafür fehlt dir das Recht. (x)", ErrorTexts.For(Codes.PermissionDenied, "x"));
    }
}
