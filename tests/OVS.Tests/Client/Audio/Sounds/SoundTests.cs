using OVS.Client.Audio;
using OVS.Client.Audio.Codec;
using OVS.Client.Audio.Sounds;
using OVS.Client.Audio.Transmit;
using OVS.Client.Input;
using OVS.Client.Settings;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Audio.Sounds;

/// <summary>Package 47: the default tones, how they mix, and when they are allowed to play.</summary>
public class SoundTests
{
    [Fact]
    public void Defaults_EveryEvent_ShortAudibleDistinct()
    {
        var tones = Enum.GetValues<SoundEvent>().ToDictionary(soundEvent => soundEvent, SoundSynth.Render);
        Assert.All(tones, pair =>
        {
            Assert.InRange(pair.Value.Length, AudioFormat.SampleRate / 20, AudioFormat.SampleRate); // 50 ms to 1 s
            Assert.True(VoiceActivityDetector.LevelDb(pair.Value) > -30, pair.Key.ToString());
            Assert.True(pair.Value.Max(Math.Abs) <= 0.5f, pair.Key.ToString()); // gentle, never at full scale
        });
        // Package 56: the three chat sounds share the tone of a private message by default, all others differ
        // Package 60: the own link tone sounds like the one for others by default
        // Package 73: both group tones share one tone by default
        SoundEvent[][] shared =
        [
            [SoundEvent.PrivateMessage, SoundEvent.ServerMessage, SoundEvent.ChannelMessage], [SoundEvent.LinkVoice, SoundEvent.OwnLinkVoice],
            [SoundEvent.GroupChanged, SoundEvent.GroupChangedByMe],
        ];
        foreach (var group in shared) Assert.All(group, soundEvent => Assert.Equal(tones[group[0]], tones[soundEvent]));
        bool SameGroup(SoundEvent soundEvent, SoundEvent otherSoundEvent) => shared.Any(soundEvents => soundEvents.Contains(soundEvent) && soundEvents.Contains(otherSoundEvent));
        foreach (var pair in tones)
            foreach (var entry in tones.Where(tone => tone.Key > pair.Key && !SameGroup(pair.Key, tone.Key)))
                Assert.False(pair.Value.SequenceEqual(entry.Value), $"{pair.Key} = {entry.Key}");
        Assert.Same(tones[SoundEvent.MicOn], SoundSynth.Render(SoundEvent.MicOn)); // rendered once
        // Package 57: the link tone plays over speech, so it is short and softer than the others
        Assert.True(tones[SoundEvent.LinkVoice].Length <= AudioFormat.SampleRate / 10);
        Assert.True(tones[SoundEvent.LinkVoice].Max(Math.Abs) < tones[SoundEvent.PrivateMessage].Max(Math.Abs) * 0.7f);
    }

    [Fact]
    public void Queue_OverlappingSounds_MixedWithVolume()
    {
        var queue = new SoundQueue();
        queue.Add([0.5f, 0.5f, 0.5f], gain: 0.5f);
        queue.Add([0.9f, 0.9f], gain: 1f);
        var frame = new float[] { 0.1f, 0.1f, 0.1f, 0.1f, 0.1f, 0.1f, 0.1f, 0.1f }; // Package 142: interleaved stereo
        queue.MixInto(frame);
        Assert.Equal([1f, 1f, 1f, 1f, 0.35f, 0.35f, 0.1f, 0.1f], frame.Select(value => MathF.Round(value, 3))); // voice kept, sum clamped to 1
        Assert.Equal(0, queue.Count); // both finished

        queue.Add(new float[1000], gain: 0f); // silent: not even queued
        Assert.Equal(0, queue.Count);
    }

    /// <summary>
    /// The engine's sounds play on what the engine holds: its configured settings, its deafened flag and the samples
    /// source it was given (the rules themselves are <see cref="SoundPlayerTests"/>).
    /// </summary>
    [Fact]
    public void Sounds_EngineConfiguredThenDeafened_PlayerFollowsBoth()
    {
        using var keys = new KeyPoller(TimeProvider.System);
        var asked = new List<(SoundEvent Sound, SoundSetting Setting)>();
        using var engine = new AudioEngine(keys, TimeProvider.System, hardware: null, samples: (sound, setting) =>
        {
            asked.Add((sound, setting));
            return [0.1f];
        });
        var own = new SoundSetting(Volume: 0.5f, File: "own.wav");
        engine.Configure(new ClientSettings { Sounds = { [SoundEvent.UserJoined] = own, [SoundEvent.UserLeft] = new SoundSetting(Muted: true) } });

        engine.Sounds.Play(SoundEvent.UserJoined);
        engine.Sounds.Play(SoundEvent.UserLeft); // muted in the configured settings
        engine.Deafened = true;
        engine.Sounds.Play(SoundEvent.ChannelEntered); // deafened

        Assert.Equal([(SoundEvent.UserJoined, own)], asked);
        Assert.Equal([SoundEvent.UserJoined], engine.Sounds.Recent);
    }

    /// <summary>
    /// Package 131 (A140): the talk key tones have rows on the settings page, are off by default and, once turned on (and
    /// saved), play on press and release of push-to-talk and link push-to-talk while connected.
    /// </summary>
    [Fact]
    public async Task TalkKeySounds_OffByDefault_PlayWhenOn()
    {
        SoundEvent[] talk = [SoundEvent.TalkKeyDown, SoundEvent.TalkKeyUp];
        Assert.All(talk, soundEvent => Assert.Equal(0f, SoundPlayer.GainFor(new ClientSettings(), soundEvent)));
        var page = new OVS.Client.ViewModels.SettingsViewModel(new ClientSettings(), [], [], TimeProvider.System);
        var rows = page.SoundRows.Where(soundRow => talk.Contains(soundRow.Event)).ToList();
        Assert.Equal(["Sprechtaste gedrückt", "Sprechtaste losgelassen"], rows.Select(soundRow => soundRow.Label));
        Assert.All(rows, soundRow => Assert.True(soundRow.Muted));
        Assert.Empty(page.ToSettings(new ClientSettings()).Sounds); // off is the default: nothing stored
        rows.ForEach(soundRow => soundRow.Muted = false);
        var on = page.ToSettings(new ClientSettings());
        Assert.All(talk, soundEvent => Assert.False(on.SoundFor(soundEvent).Muted));
        rows[0].ResetCommand.Execute(null); // back to the default: off again
        Assert.True(rows[0].Muted);

        using var keys = new KeyPoller(TimeProvider.System);
        using var engine = new AudioEngine(keys, TimeProvider.System, hardware: null) { SelfMuted = false, HasSpeakLinked = true, ChannelLinked = true };
        engine.Configure(new ClientSettings());
        engine.Connected = true;
        async Task PressAndReleaseAsync(KeyAction action, int expected)
        {
            keys.Simulate(action, true);
            await AsyncWait.UntilAsync(() => keys.IsDown(action));
            keys.Simulate(action, false);
            await AsyncWait.UntilAsync(() => engine.Sounds.Recent.Count >= expected && !keys.IsDown(action));
            await AsyncWait.AssertStaysFalseAsync(() => engine.Sounds.Recent.Count > expected, TimeSpan.FromMilliseconds(50), "a few more polls: nothing else plays");
        }
        await PressAndReleaseAsync(KeyAction.PushToTalk, 0);
        Assert.Empty(engine.Sounds.Recent); // off by default

        engine.Configure(on);
        await PressAndReleaseAsync(KeyAction.PushToTalk, 2);
        await PressAndReleaseAsync(KeyAction.LinkPushToTalk, 4);
        Assert.Equal([.. talk, .. talk], engine.Sounds.Recent);

        engine.Connected = false; // not connected: the keys do nothing, so no tone
        await PressAndReleaseAsync(KeyAction.PushToTalk, 4);
        Assert.Equal(4, engine.Sounds.Recent.Count);
    }

    // ---- Package 189 (A201): the talk key tone only while a held key really sends ----

    static readonly ClientSettings TalkTonesOn = new()
    {
        Sounds = { [SoundEvent.TalkKeyDown] = new SoundSetting(), [SoundEvent.TalkKeyUp] = new SoundSetting() },
    };

    /// <summary>A connected, unmuted engine with the talk key tones on, in a channel with a link and the right to use it.</summary>
    static AudioEngine TalkEngine(KeyPoller keys)
    {
        var engine = new AudioEngine(keys, TimeProvider.System, hardware: null) { SelfMuted = false, HasSpeakLinked = true, ChannelLinked = true };
        engine.Configure(TalkTonesOn);
        engine.Connected = true;
        return engine;
    }

    static async Task HoldAsync(KeyPoller keys, KeyAction action, bool down)
    {
        keys.Simulate(action, down);
        await AsyncWait.UntilAsync(() => keys.IsDown(action) == down);
    }

    /// <summary>Package 189 (AC1): link push-to-talk in a channel without a link sends nothing, so no tone.</summary>
    [Fact]
    public async Task TalkKeyTone_LinkPttWithoutLink_Silent()
    {
        using var keys = new KeyPoller(TimeProvider.System);
        using var engine = TalkEngine(keys);
        engine.ChannelLinked = false;

        await HoldAsync(keys, KeyAction.LinkPushToTalk, true);
        await HoldAsync(keys, KeyAction.LinkPushToTalk, false);

        await AsyncWait.AssertStaysFalseAsync(() => engine.Sounds.Recent.Count > 0, TimeSpan.FromMilliseconds(50), "no tone without a link");
    }

    /// <summary>Package 189 (AC4): muted, server muted or in a muted channel, a talk key plays no tone.</summary>
    [Theory]
    [InlineData("self")]
    [InlineData("server")]
    [InlineData("channel")]
    public async Task TalkKeyTone_MutedServerMutedOrMutedChannel_Silent(string muted)
    {
        using var keys = new KeyPoller(TimeProvider.System);
        using var engine = TalkEngine(keys);
        engine.SelfMuted = muted == "self";
        engine.ServerMuted = muted == "server";
        engine.ChannelMuted = muted == "channel";

        await HoldAsync(keys, KeyAction.PushToTalk, true);
        await HoldAsync(keys, KeyAction.PushToTalk, false);

        await AsyncWait.AssertStaysFalseAsync(() => engine.Sounds.Recent.Count > 0, TimeSpan.FromMilliseconds(50), $"no tone while muted ({muted})");
    }

    /// <summary>Package 189 (AC5): unmuting while push-to-talk is held starts sending, so the down tone, and the up tone on release.</summary>
    [Fact]
    public async Task TalkKeyTone_SendingStartsWhileHeld_DownThenUp()
    {
        using var keys = new KeyPoller(TimeProvider.System);
        using var engine = TalkEngine(keys);
        engine.SelfMuted = true;
        await HoldAsync(keys, KeyAction.PushToTalk, true);
        Assert.Empty(engine.Sounds.Recent);

        engine.SelfMuted = false;
        Assert.Equal([SoundEvent.TalkKeyDown], engine.Sounds.Recent);
        await HoldAsync(keys, KeyAction.PushToTalk, false);
        await AsyncWait.UntilAsync(() => engine.Sounds.Recent.Count == 2);

        Assert.Equal([SoundEvent.TalkKeyDown, SoundEvent.TalkKeyUp], engine.Sounds.Recent);
    }

    /// <summary>Package 189 (AC6): in the self test the tones play on the key as before, also while muted.</summary>
    [Fact]
    public async Task TalkKeyTone_SelfTest_Plays()
    {
        using var keys = new KeyPoller(TimeProvider.System);
        using var engine = TalkEngine(keys);
        engine.SelfMuted = true;
        engine.SelfTest = true;

        await HoldAsync(keys, KeyAction.PushToTalk, true);
        await HoldAsync(keys, KeyAction.PushToTalk, false);
        await AsyncWait.UntilAsync(() => engine.Sounds.Recent.Count == 2);

        Assert.Equal([SoundEvent.TalkKeyDown, SoundEvent.TalkKeyUp], engine.Sounds.Recent);
    }
    /// <summary>Package 159: a speaker test player on a queue, with all sounds off and deafened, so only the test can play.</summary>
    static SoundPlayer TestPlayer(SoundQueue queue, Action woken) =>
        new(queue, () => new ClientSettings { SoundsEnabled = false }, () => true, woken);

    /// <summary>Package 159: the test plays one second of its tone at the output volume and wakes the playback.</summary>
    [Fact]
    public void SpeakerTest_Played_OneSecondAtVolume()
    {
        var queue = new SoundQueue();
        int woken = 0;
        TestPlayer(queue, () => woken++).PlayTest(0.5f);
        Assert.Equal((1, 1), (queue.Count, woken));
        Assert.Equal(AudioFormat.SampleRate, SoundSynth.TestTone.Length);
        var frame = new float[2 * AudioFormat.SampleRate];
        queue.MixInto(frame);
        Assert.Equal(SoundSynth.TestTone.Max(Math.Abs) * 0.5f, frame.Max(Math.Abs), 4);
        Assert.Equal(0, queue.Count); // one second, then it ends
    }

    /// <summary>Package 159: a second test while one plays starts it anew instead of a second tone on top.</summary>
    [Fact]
    public void SpeakerTest_Twice_NoOverlap()
    {
        var queue = new SoundQueue();
        var player = TestPlayer(queue, () => { });
        player.PlayTest(1f);
        queue.MixInto(new float[200]);
        player.PlayTest(1f);
        Assert.Equal(1, queue.Count);
        var frame = new float[2];
        queue.MixInto(frame);
        Assert.Equal(SoundSynth.TestTone[0], frame[0]); // from its start again
    }

    /// <summary>Package 159: all sounds off and deafened do not silence the test, which is no notification.</summary>
    [Fact]
    public void SpeakerTest_AllSoundsOff_StillPlays()
    {
        var queue = new SoundQueue();
        var player = TestPlayer(queue, () => { });
        player.Play(SoundEvent.PrivateMessage); // a notification: silent
        Assert.Equal(0, queue.Count);
        player.PlayTest(0.8f);
        Assert.Equal(1, queue.Count);
    }

}
