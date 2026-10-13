using OVS.Client.Audio.Sounds;
using OVS.Client.Settings;

namespace OVS.Tests.Client.Audio.Sounds;

/// <summary>
/// Package 47, 48: which notification sound plays, how loud, from which samples, the preview of one sound's setting, and
/// the list of the last ones played.
/// </summary>
public class SoundPlayerTests
{
    /// <summary>Decimal places the mixed float samples are compared to.</summary>
    const int Precision = 6;

    readonly SoundQueue queue = new();
    readonly List<(SoundEvent Sound, SoundSetting Setting)> asked = [];
    ClientSettings settings = new() { SoundVolume = 0.5f };
    bool deafened;
    int wakes;

    /// <summary>A player on the fields above; its samples source notes what it is asked for and answers <paramref name="tone"/>.</summary>
    SoundPlayer Create(float[]? tone = null) => new(queue, () => settings, () => deafened, () => wakes++, (sound, setting) =>
    {
        asked.Add((sound, setting));
        return tone ?? [0.5f];
    });

    /// <summary>The queued tones mixed into one silent stereo frame; the left side of its first <paramref name="samples"/>.</summary>
    float[] MixLeftSide(int samples)
    {
        var frame = new float[2 * samples];
        queue.MixInto(frame);
        return [.. Enumerable.Range(0, samples).Select(index => frame[2 * index])];
    }

    static void AssertSamples(IEnumerable<float> expected, float[] actual)
    {
        float[] wanted = [.. expected];
        Assert.Equal(wanted.Length, actual.Length);
        for (int index = 0; index < wanted.Length; index++) Assert.Equal(wanted[index], actual[index], Precision);
    }

    /// <summary>Package 48: the sound's own volume times the overall one; muted or all off is silent.</summary>
    [Fact]
    public void Volume_PerSoundTimesGlobal_MutedSilent()
    {
        var settings = new ClientSettings
        {
            SoundVolume = 0.5f,
            Sounds = { [SoundEvent.MicOn] = new SoundSetting(Volume: 0.4f), [SoundEvent.MicOff] = new SoundSetting(Muted: true) },
        };
        Assert.Equal(0.2f, SoundPlayer.GainFor(settings, SoundEvent.MicOn), 3);
        Assert.Equal(0.5f, SoundPlayer.GainFor(settings, SoundEvent.Connected), 3); // not set: 100 %
        Assert.Equal(0f, SoundPlayer.GainFor(settings, SoundEvent.MicOff));
        settings.SoundsEnabled = false;
        Assert.Equal(0f, SoundPlayer.GainFor(settings, SoundEvent.MicOn));
    }

    [Fact]
    public void Play_AudibleSound_QueuesItsSamplesAtItsGainAndWakesThePlayback()
    {
        settings.Sounds[SoundEvent.UserJoined] = new SoundSetting(Volume: 0.4f);
        var player = Create([0.5f, -0.5f]);

        player.Play(SoundEvent.UserJoined);

        Assert.Equal(1, wakes);
        AssertSamples([0.1f, -0.1f], MixLeftSide(2)); // 0.5 x the sound's 0.4 x the overall 0.5
    }

    [Fact]
    public void Play_AudibleSound_AsksTheSamplesForItsSetting()
    {
        var own = new SoundSetting(Volume: 0.4f, File: "own.wav");
        settings.Sounds[SoundEvent.UserJoined] = own;
        var player = Create();

        player.Play(SoundEvent.UserJoined);

        Assert.Equal([(SoundEvent.UserJoined, own)], asked);
    }

    [Fact]
    public void Play_NoSamplesSourceGiven_PlaysTheDefaultTone()
    {
        var player = new SoundPlayer(queue, () => settings, () => deafened, () => wakes++);

        player.Play(SoundEvent.MicOn);

        AssertSamples(SoundSynth.Render(SoundEvent.MicOn).Take(100).Select(value => value * 0.5f), MixLeftSide(100));
    }

    /// <summary>The settings are read once per sound, so its gain and its samples never come from two different saves.</summary>
    [Fact]
    public void Play_SettingsReplacedDuringPlay_GainAndSamplesFromOneSnapshot()
    {
        var first = new ClientSettings { SoundVolume = 0.5f, Sounds = { [SoundEvent.UserJoined] = new SoundSetting(File: "first.wav") } };
        var second = new ClientSettings { SoundVolume = 0.25f, Sounds = { [SoundEvent.UserJoined] = new SoundSetting(File: "second.wav") } };
        int reads = 0;
        var player = new SoundPlayer(queue, () => reads++ == 0 ? first : second, () => deafened, () => wakes++, (sound, setting) =>
        {
            asked.Add((sound, setting));
            return [0.5f];
        });

        player.Play(SoundEvent.UserJoined);

        Assert.Equal("first.wav", Assert.Single(asked).Setting.File);
        AssertSamples([0.25f], MixLeftSide(1)); // 0.5 x the first save's 0.5
    }

    [Fact]
    public void Play_MutedSound_NeitherRenderedNorQueuedNorRecorded()
    {
        settings.Sounds[SoundEvent.UserLeft] = new SoundSetting(Muted: true);
        var player = Create();

        player.Play(SoundEvent.UserLeft);

        Assert.Empty(asked);
        Assert.Equal(0, queue.Count);
        Assert.Equal(0, wakes);
        Assert.Empty(player.Recent);
    }

    [Fact]
    public void Play_AllSoundsOff_NothingPlayed()
    {
        settings.SoundsEnabled = false;
        var player = Create();

        foreach (var sound in Enum.GetValues<SoundEvent>()) player.Play(sound);

        Assert.Empty(asked);
        Assert.Equal(0, queue.Count);
        Assert.Equal(0, wakes);
        Assert.Empty(player.Recent);
    }

    /// <summary>A46: while deafened only the tones of the own microphone and sound switches play.</summary>
    [Fact]
    public void Play_Deafened_OnlyOwnMicAndSoundTonesPlay()
    {
        deafened = true;
        var player = Create();

        foreach (var sound in Enum.GetValues<SoundEvent>()) player.Play(sound);

        Assert.Equal([SoundEvent.MicOff, SoundEvent.MicOn, SoundEvent.SoundOff, SoundEvent.SoundOn], player.Recent);
        Assert.Equal(4, wakes);
    }

    [Fact]
    public void Play_DeafenedEndedAfterCreation_PlaysAgain()
    {
        var player = Create();
        deafened = true;
        player.Play(SoundEvent.UserJoined); // deafened: not played

        deafened = false;
        player.Play(SoundEvent.UserJoined);

        Assert.Equal([SoundEvent.UserJoined], player.Recent);
    }

    [Fact]
    public void Play_SettingsReplacedAfterCreation_PlaysAtTheNewGain()
    {
        var player = Create();
        settings = new ClientSettings { SoundVolume = 0.2f };

        player.Play(SoundEvent.UserJoined);

        AssertSamples([0.1f], MixLeftSide(1)); // 0.5 x the new overall 0.2
    }

    /// <summary>Package 48: the settings page previews a row: its samples, at the page's overall volume times the row's.</summary>
    [Fact]
    public void Preview_GivenSoundAndSetting_PlaysItsSamplesAtOverallTimesItsVolume()
    {
        var row = new SoundSetting(Volume: 0.5f, File: "own.wav");
        var player = Create();

        player.Preview(SoundEvent.UserJoined, row, 0.8f);

        Assert.Equal([(SoundEvent.UserJoined, row)], asked);
        AssertSamples([0.2f], MixLeftSide(1)); // 0.5 x the row's 0.5 x the page's 0.8
        Assert.Equal(1, wakes);
    }

    [Fact]
    public void Recent_MoreThanTwentyPlayed_KeepsTheLastTwentyInOrder()
    {
        var player = Create();
        var audible = Enum.GetValues<SoundEvent>().Where(soundEvent => SoundPlayer.GainFor(settings, soundEvent) > 0).ToArray();
        var played = Enumerable.Range(0, 25).Select(index => audible[index % audible.Length]).ToList();

        foreach (var sound in played) player.Play(sound);

        Assert.Equal(played.Skip(5), player.Recent);
    }
}
