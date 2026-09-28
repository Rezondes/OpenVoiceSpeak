using NAudio.Wave;
using OVS.Client.Audio;
using OVS.Client.Settings;

namespace OVS.Tests.Client;

/// <summary>Package 48: own sound files are checked, converted and kept in the profile.</summary>
public sealed class SoundImportTests : IDisposable
{
    readonly string dir = Directory.CreateTempSubdirectory("ovs-sounds-").FullName;
    string Profile => Path.Combine(dir, "profil");

    public void Dispose() => Directory.Delete(dir, true);

    /// <summary>A 16 bit stereo sine at 44.1 kHz: a different format than the client's, on purpose.</summary>
    string Wav(string name, double seconds)
    {
        var path = Path.Combine(dir, name);
        using var writer = new WaveFileWriter(path, new WaveFormat(44100, 16, 2));
        for (int i = 0; i < (int)(44100 * seconds); i++)
        {
            short sample = (short)(8000 * Math.Sin(2 * Math.PI * 440 * i / 44100));
            writer.WriteSample(sample / 32768f);
            writer.WriteSample(sample / 32768f);
        }
        return path;
    }

    [Fact]
    public void Wav_UpToFiveSeconds_Accepted_CopiedAsMono48k()
    {
        var source = Wav("klick.wav", 1);
        var (file, error) = SoundImport.Prepare(source, Profile, SoundEvent.UserJoined);
        Assert.Null(error);
        File.Delete(source); // the copy does not need the original
        var copy = Path.Combine(Profile, SoundLibrary.Folder, file!);
        Assert.True(File.Exists(copy));
        using (var reader = new WaveFileReader(copy))
            Assert.Equal((48000, 1), (reader.WaveFormat.SampleRate, reader.WaveFormat.Channels));
        var samples = new SoundLibrary(Profile, _ => { }).Samples(SoundEvent.UserJoined, new SoundSetting(File: file));
        Assert.InRange(samples.Length, 47_990, 48_010);
        Assert.True(VoiceActivityDetector.LevelDb(samples) > -20);
    }

    [Fact]
    public void TooLong_NotAudio_WrongType_Missing_Rejected()
    {
        Assert.Contains("5 Sekunden", SoundImport.Prepare(Wav("lang.wav", 6), Profile, SoundEvent.MicOn).Error);
        var fake = Path.Combine(dir, "kaputt.wav");
        File.WriteAllText(fake, "kein Audio");
        Assert.Contains("WAV oder MP3", SoundImport.Prepare(fake, Profile, SoundEvent.MicOn).Error);
        var ogg = Path.Combine(dir, "ton.ogg");
        File.Copy(Wav("x.wav", 0.5), ogg);
        Assert.Contains("WAV oder MP3", SoundImport.Prepare(ogg, Profile, SoundEvent.MicOn).Error);
        Assert.Contains("gibt es nicht", SoundImport.Prepare(Path.Combine(dir, "fehlt.wav"), Profile, SoundEvent.MicOn).Error);
        Assert.False(Directory.Exists(Path.Combine(Profile, SoundLibrary.Folder)) && Directory.GetFiles(Path.Combine(Profile, SoundLibrary.Folder)).Length > 0);
    }

    [Fact]
    public void CustomSound_MissingFile_FallsBackToDefault_LoggedOnce()
    {
        var logged = new List<string>();
        var library = new SoundLibrary(Profile, logged.Add);
        var setting = new SoundSetting(File: "UserJoined-weg.wav");
        Assert.Same(SoundSynth.Render(SoundEvent.UserJoined), library.Samples(SoundEvent.UserJoined, setting));
        library.Samples(SoundEvent.UserJoined, setting);
        Assert.Single(logged);
        Assert.Contains("Standardton", logged[0]);
    }

    [Fact]
    public void CleanUp_RemovesCopiesNobodyUses()
    {
        var (keep, _) = SoundImport.Prepare(Wav("a.wav", 0.3), Profile, SoundEvent.MicOn);
        var (drop, _) = SoundImport.Prepare(Wav("b.wav", 0.3), Profile, SoundEvent.MicOff);
        var settings = new ClientSettings { Sounds = { [SoundEvent.MicOn] = new SoundSetting(File: keep) } };
        new SoundLibrary(Profile, _ => { }).CleanUp(settings);
        Assert.Equal([keep], Directory.GetFiles(Path.Combine(Profile, SoundLibrary.Folder)).Select(Path.GetFileName));
        Assert.NotEqual(keep, drop);
    }
}
