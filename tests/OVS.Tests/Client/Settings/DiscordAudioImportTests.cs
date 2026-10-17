using OVS.Client.Settings;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Settings;

/// <summary>Package 171: Discord's and Fluxer's audio settings as their local storage keeps them (keys and shapes from a real installation, A174).</summary>
public sealed class DiscordAudioImportTests() : TempDirectoryTest("ovs-chatapp-", quiet: true)
{
    const string MediaEngine = """
        {"default":{"mode":"PUSH_TO_TALK","modeOptions":{"threshold":-50.4,"autoThreshold":false,"delay":120,"shortcut":[]},
        "echoCancellation":false,"noiseSuppression":false,"noiseCancellation":true,"automaticGainControl":true,
        "inputVolume":150,"outputVolume":99.6,"inputDeviceId":"{0.0.1.00000000}.{mic}","outputDeviceId":"default","localVolumes":{"1":115}},
        "stream":{"mode":"VOICE_ACTIVITY"}}
        """;

    const string Keybinds = """
        {"_state":{"3":{"action":"SAVE_CLIP","enabled":true,"shortcut":[[0,164,1],[0,67,1]]},
        "8":{"action":"PUSH_TO_TALK","enabled":true,"shortcut":[[1,4,"0:0"]]},
        "9":{"action":"PUSH_TO_TALK","enabled":true,"shortcut":[[0,86,"0:0"]]}},"_version":5}
        """;

    string Storage(string app)
    {
        var folder = Path.Combine(dir, app, "Local Storage", "leveldb");
        Directory.CreateDirectory(folder);
        return folder;
    }

    [Fact]
    public void Read_Fixture_AudioValuesFound()
    {
        LevelDbFiles.WriteLog(Path.Combine(Storage("discord"), "000003.log"),
            [new(LevelDbFiles.Key("https://discord.com", "MediaEngineStore"), LevelDbFiles.Text(MediaEngine), 1),
             new(LevelDbFiles.Key("https://discord.com", "keybinds"), LevelDbFiles.Text(Keybinds), 2)]);

        var audio = new DiscordAudioImport().Read(dir).Audio!;

        Assert.Equal(("Discord", "{0.0.1.00000000}.{mic}", "default"), (audio.Source, audio.InputDeviceId, audio.OutputDeviceId));
        Assert.Equal((false, false, -50.4, 120), (audio.VoiceActivation, audio.VadAutomatic, audio.VadThresholdDb, audio.ReleaseDelayMs));
        Assert.Equal((150d, 99.6, false, true, true), (audio.InputPercent, audio.OutputPercent, audio.EchoCancellation, audio.NoiseSuppression, audio.AutoGain));
        Assert.Equal(86, audio.PushToTalkKey); // the keyboard key; the mouse button stays out
    }

    [Fact]
    public void Read_FluxerOnly_ItsValuesFound()
    {
        LevelDbFiles.WriteLog(Path.Combine(Storage("fluxer"), "000003.log"), [new(LevelDbFiles.Key("https://web.fluxer.app", "VoiceSettings"),
            LevelDbFiles.Text("""{"inputDeviceId":"default","inputVolume":100,"outputVolume":25,"echoCancellation":true,"autoGainControl":false,"vadAutoSensitivity":true}"""), 1)]);

        Assert.False(new DiscordAudioImport().IsInstalled(dir));
        var audio = new FluxerAudioImport().Read(dir).Audio!;

        Assert.Equal(("Fluxer", "default", 25d, false, true), (audio.Source, audio.InputDeviceId, audio.OutputPercent, audio.AutoGain, audio.VadAutomatic));
        Assert.Null(audio.VoiceActivation);
    }

    [Fact]
    public void Read_NoInstallation_NoneFound()
    {
        Assert.All(ChatAppAudioImport.All, app => Assert.False(app.IsInstalled(dir)));
        var result = new DiscordAudioImport().Read(dir);
        Assert.Equal((null, "Discord is not installed"), (result.Audio, result.Problem));
    }

    [Fact]
    public void Read_UnknownFormat_ReasonReported()
    {
        LevelDbFiles.WriteLog(Path.Combine(Storage("discord"), "000003.log"),
            [new(LevelDbFiles.Key("https://discord.com", "MediaEngineStore"), LevelDbFiles.Text("{nicht json"), 1)]);

        var result = new DiscordAudioImport().Read(dir);

        Assert.True(new DiscordAudioImport().IsInstalled(dir));
        Assert.Null(result.Audio);
        Assert.StartsWith("discord: ", result.Problem, StringComparison.Ordinal);
    }
}
