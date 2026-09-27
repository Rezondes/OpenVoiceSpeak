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
}
